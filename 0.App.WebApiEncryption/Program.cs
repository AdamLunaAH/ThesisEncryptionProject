#if USE_SCALAR
using Scalar.AspNetCore;
using Domain.Services.Encryptions.Benchmark;
using System.Text.Json.Nodes;
#elif USE_SWAGGER
using Microsoft.OpenApi.Models;
#endif
using DataAccess.DbContext.Extensions;
using DataAccess.DbContext.Factory;
using DataAccess.DbContext;
using DataAccess.Repositories.Database.Benchmark;
using Benchmark.Client.Services;
using CrossCut.Secrets.Extensions;
using Npgsql;
using Orleans;
using Orleans.Configuration;
using Orleans.Dashboard;
using App.WebApiEncryption.Hubs;
using App.WebApiEncryption.Services;
using CrossCut.Concerns.Monitor;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring;

var builder = WebApplication.CreateBuilder(args);

#if DEBUG
// Configure Kestrel with a local dev certificate that covers IP address SANs.
// Only active in Debug builds - Azure App Service manages TLS termination in production.
// Generate dev-cert.pfx by running (as Administrator):
//   _scripts/dotnet/generate-ip-dev-certificate.ps1
var certPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "dev-cert.pfx");
var absoluteCertPath = Path.GetFullPath(certPath);

System.Console.WriteLine($"DEBUG: Certificate path = {absoluteCertPath}");
System.Console.WriteLine($"DEBUG: Certificate exists = {File.Exists(absoluteCertPath)}");

if (File.Exists(absoluteCertPath))
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(5183);
        options.ListenLocalhost(7258, listenOptions =>
        {
            listenOptions.UseHttps(absoluteCertPath, "DevCertPassword123");
        });
    });
}
else
{
    // Fallback to default self-signed certificate if custom cert not found
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(5183);
        options.ListenLocalhost(7258, listenOptions =>
        {
            listenOptions.UseHttps();
        });
    });
}
#endif

#if USE_SCALAR
builder.Services.AddOpenApi(options =>
{
    // OpenApi3_0 kept for broad client compatibility.
    options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;

    options.AddDocumentTransformer((doc, ctx, ct) =>
    {
        doc.Info = new()
        {
            Title = "App.WebApiEncryption",
            Version = "v1"
        };

        doc.Components ??= new();
        doc.Components.SecuritySchemes ??= new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>();

        doc.Components.SecuritySchemes["Bearer"] = new Microsoft.OpenApi.OpenApiSecurityScheme
        {
            Type = Microsoft.OpenApi.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste JWT token here"
        };

        return Task.CompletedTask;
    });

    // Inject enum dropdowns for the algorithm/auth selectors on the /run endpoint.
    options.AddOperationTransformer((operation, context, ct) =>
    {
        if (context.Description.ActionDescriptor is not
            Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor descriptor
            || descriptor.ActionName != "Run"
            || descriptor.ControllerName != "EncryptionBenchmark")
        {
            return Task.CompletedTask;
        }

        foreach (var param in operation.Parameters ?? [])
        {
            switch (param.Name)
            {
                case "algorithms":
                    if (param.Schema?.Items is Microsoft.OpenApi.OpenApiSchema itemsSchema)
                    {
                        itemsSchema.Enum = AlgorithmCatalog.AllCipherIds
                            .Select(id => (JsonNode)JsonValue.Create(id)!)
                            .ToList();
                    }
                    break;

                case "authIds":
                    if (param.Schema?.Items is Microsoft.OpenApi.OpenApiSchema authItemsSchema)
                    {
                        authItemsSchema.Enum = AlgorithmCatalog.AllAuthIds
                            .Select(id => (JsonNode)JsonValue.Create(id)!)
                            .ToList();
                    }
                    break;

                case "tlsVersion":
                    // The enum schema may be inlined or a $ref component (OpenApiSchemaReference
                    // does not inherit OpenApiSchema). Resolve to the concrete target first.
                    var tlsConcreteSchema = param.Schema switch
                    {
                        Microsoft.OpenApi.OpenApiSchemaReference refSchema => refSchema.RecursiveTarget,
                        Microsoft.OpenApi.OpenApiSchema inline => inline,
                        _ => null
                    };
                    if (tlsConcreteSchema is not null)
                    {
                        tlsConcreteSchema.Type = Microsoft.OpenApi.JsonSchemaType.String;
                        tlsConcreteSchema.Enum = Enum.GetNames<BenchmarkTlsVersion>()
                            .Select(name => (JsonNode)JsonValue.Create(name)!)
                            .ToList();
                    }
                    break;
            }
        }

        // Remove the legacy single-value authId parameter — the UI uses the
        // authIds multi-select instead. Kept in BenchmarkOptions for backward
        // compatibility with direct HTTP calls, but hidden from Scalar.
        var legacyAuthIdParam = operation.Parameters?.FirstOrDefault(p => p.Name == "authId");
        if (legacyAuthIdParam is not null)
            operation.Parameters!.Remove(legacyAuthIdParam);

        return Task.CompletedTask;
    });
});
#elif USE_SWAGGER
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "App.WebApiEncryption", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste JWT token here"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});
#endif

builder.Services.AddControllers();


// // Add Orleans Dashboard for monitoring grain activity in both Debug and Release builds
// builder.Services.AddOrleansDashboard();

//Initiate Stack
builder.Configuration.AddSecrets(builder.Environment);
builder.Services.AddUserDbContextFactory();
builder.Services.AddScoped<MainDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory>().CreateDbContextAsync().GetAwaiter().GetResult());



#if DEBUG
// Configure SignalR with server-side logging for debugging
builder.Services.AddSignalR(hubOptions =>
{
    // Limit maximum message size
    hubOptions.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
});
#else
// Configure SignalR with server-side logging for debugging with Azure SignalR Service in production
builder.Services.AddSignalR(hubOptions =>
{
    // Limit maximum message size
    hubOptions.MaximumReceiveMessageSize = 64 * 1024; // 64KB
}).AddAzureSignalR();
#endif

// Orleans Silo configuration for clustering and grain storage.
builder.Host.UseOrleans(silo =>
{

    // Clustering configuration: Use Localhost clustering for development and ADO.NET clustering with PostgreSQL for production. This allows the same silo code to run in both environments with appropriate clustering based on the build configuration.
    // Currently the application uses PostgreSQL clustering even in development for simplicity and to ensure the clustering configuration is tested during development. However, the code is structured to allow easy switching to localhost clustering for development if desired by removing the comment from the `DEBUG_LOCALHOST` symbol definition in App.WebApiEncryption.csproj.
#if DEBUG_LOCALHOST
    // Development: Uses Localhost clustering for development and testing.
    silo.UseLocalhostClustering();

    // Configure cluster options with ClusterId and ServiceId for development.
    silo.Configure<ClusterOptions>(options =>
    {
        options.ClusterId = "dev";
        options.ServiceId = "EncryptionProject";
    });
#else
    // Production/Release: Use PostgreSQL for distributed clustering across multiple instances
    // Get database configuration from appsettings.json
    var dbConnConfig = builder.Configuration.GetSection("DatabaseConnections");
    var useDataSetWithTag = dbConnConfig["UseDataSetWithTag"]
        ?? throw new InvalidOperationException("DatabaseConnections:UseDataSetWithTag is not configured in appsettings.json");
    var defaultDataUser = dbConnConfig["DefaultDataUser"]
        ?? throw new InvalidOperationException("DatabaseConnections:DefaultDataUser is not configured in appsettings.json");

    // Construct the connection string key: ConnectionStrings:{UseDataSetWithTag}.{DefaultDataUser}
    var connectionStringKey = $"ConnectionStrings:{useDataSetWithTag}.{defaultDataUser}";
    var postgresConnectionString = builder.Configuration.GetConnectionString(useDataSetWithTag)
        ?? builder.Configuration[$"{connectionStringKey}"]
        ?? throw new InvalidOperationException($"Connection string '{connectionStringKey}' is not configured in user secrets or appsettings.json");

    // Ensure search_path includes gstusr schema where Orleans tables are stored
    var connectionStringBuilder = new NpgsqlConnectionStringBuilder(postgresConnectionString);
    if (string.IsNullOrEmpty(connectionStringBuilder.SearchPath))
    {
        connectionStringBuilder.SearchPath = "gstusr,public";
    }

    silo.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = connectionStringBuilder.ToString();
    });

    // Configure cluster options with environment-aware ClusterId and ServiceId for production.
    silo.Configure<ClusterOptions>(options =>
    {
        options.ClusterId = $"encryptionproject-{builder.Environment.EnvironmentName.ToLower()}";
        options.ServiceId = "EncryptionProject";
    });
#endif

    silo.AddDashboard();

    // Use in-memory grain storage for simplicity and for only saving the Orleans Grains during run time. Relevant for the GameSession and ChatSession grains which are transient and only need to persist during runtime. Data connected to ChatSession and GameSession is being saved to the database, but the grain state itself is only stored in memory for quick access during runtime.
    silo.AddMemoryGrainStorageAsDefault();

    // Configure grain collection for handling stale grains. This helps clean up transient session grains that are no longer active.
    silo.Configure<GrainCollectionOptions>(options =>
    {
        options.CollectionAge = TimeSpan.FromMinutes(5);
    });
});



// Benchmark repositories
builder.Services.AddTransient<IBenchmarkRepository, BenchmarkRepository>();
builder.Services.AddTransient<IBenchmarkPayloadRepository, BenchmarkPayloadRepository>();
builder.Services.AddTransient<IBenchmarkResourceRepository, BenchmarkResourceRepository>();
builder.Services.Configure<App.WebApiEncryption.Controllers.BenchmarkPayloadOptions>(
    builder.Configuration.GetSection(App.WebApiEncryption.Controllers.BenchmarkPayloadOptions.SectionName));

// Hub URL for SignalR echo benchmark tests
builder.Services.Configure<ChatSettings>(
    builder.Configuration.GetSection("Chat"));

// Resource monitoring — registers the hosted service that publishes CPU and memory
// utilisation as observable metric instruments consumed by ResourceMonitorBroadcaster.
builder.Services.AddResourceMonitoring();

// Resource capture service (singleton so the background broadcaster and the
// benchmark slice share the same capture session dictionary).
builder.Services.AddSingleton<IResourceCaptureService, ResourceCaptureService>();

// SignalR connection count tracking (shared across ChatHub and MonitorHub).
builder.Services.AddSingleton<ConnectionTracker>();

// Background service that samples metrics and broadcasts them over MonitorHub.
builder.Services.AddHostedService<ResourceMonitorBroadcaster>();


var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
{
#if USE_SCALAR
    app.MapOpenApi(); // generates /openapi/v1.json

    // Scalar natively supports Microsoft.AspNetCore.OpenApi. It reads the Bearer scheme
    // from components.securitySchemes and sends the Authorization header automatically.
    app.MapScalarApiReference(options =>
    {
        options.Title = "App.WebApiEncryption";
        options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
        options.Authentication = new ScalarAuthenticationOptions
        {
            PreferredSecuritySchemes = ["Bearer"]
        };
    });
#elif USE_SWAGGER
    app.UseSwagger(); // generates /swagger/v1/swagger.json
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "App.WebApiEncryption v1");
    });
#endif
}

app.UseHttpsRedirection();

// Orleans Dashboard (available at /dashboard)
app.MapOrleansDashboard(routePrefix: "/dashboard");

app.MapControllers();

// Benchmark echo hub — anonymous, no auth required.
app.MapHub<BenchmarkHub>("/benchmarkhub").AllowAnonymous();

// Live resource monitor hub — push-only, console clients connect here.
app.MapHub<MonitorHub>("/hubs/monitor").AllowAnonymous();

app.Run();

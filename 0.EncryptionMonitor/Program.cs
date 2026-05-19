using EncryptionMonitor;
using Microsoft.AspNetCore.SignalR.Client;
using Spectre.Console;

// ── Settings — change here to point at a different server ────────────────────
const string HubUrl = "https://localhost:7258/hubs/monitor";

// Whether to skip TLS certificate validation (only for local dev with self-signed certs).
const bool BypassCertificateValidation = true;

// How long to wait before attempting to reconnect after a dropped connection (ms).
const int ReconnectDelayMs = 3_000;

// ── Build SignalR connection ───────────────────────────────────────────────────
var connection = new HubConnectionBuilder()
    .WithUrl(HubUrl, options =>
    {
        if (BypassCertificateValidation)
        {
            options.HttpMessageHandlerFactory = _ =>
                new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
        }
    })
    .WithAutomaticReconnect(new RetryPolicy(ReconnectDelayMs))
    .Build();

// ── Dashboard ─────────────────────────────────────────────────────────────────
var dashboard = new MonitorDashboard();

connection.On<ResourceMetricsSnapshot>("Metrics", snapshot =>
{
    dashboard.Update(snapshot);
});

connection.Reconnecting += _ =>
{
    dashboard.SetStatus("[yellow]Reconnecting…[/]");
    return Task.CompletedTask;
};

connection.Reconnected += _ =>
{
    dashboard.SetStatus("[green]Reconnected[/]");
    return Task.CompletedTask;
};

connection.Closed += _ =>
{
    dashboard.SetStatus("[red]Connection closed.[/]  Press Ctrl+C to exit.");
    return Task.CompletedTask;
};

// ── Connect ───────────────────────────────────────────────────────────────────
AnsiConsole.MarkupLine($"[grey]Connecting to[/] [yellow]{HubUrl}[/][grey]…[/]");

try
{
    await connection.StartAsync();
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Could not connect:[/] {Markup.Escape(ex.Message)}");
    AnsiConsole.MarkupLine("[grey]Make sure the server is running and HubUrl is correct.[/]");
    Environment.Exit(1);
}

// ── Live display loop ─────────────────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await AnsiConsole.Live(dashboard.Root)
    .AutoClear(false)
    .Overflow(VerticalOverflow.Ellipsis)
    .StartAsync(async ctx =>
    {
        while (!cts.IsCancellationRequested)
        {
            ctx.UpdateTarget(dashboard.Refresh());
            await Task.Delay(500, cts.Token).ConfigureAwait(false);
        }
    }).ConfigureAwait(false);

await connection.DisposeAsync();

AnsiConsole.MarkupLine("[grey]Monitor stopped.[/]");

// ── Helpers ───────────────────────────────────────────────────────────────────

/// <summary>
/// Simple fixed-interval reconnect policy.
/// </summary>
file sealed class RetryPolicy : IRetryPolicy
{
    private readonly TimeSpan _delay;

    public RetryPolicy(int delayMs) => _delay = TimeSpan.FromMilliseconds(delayMs);

    public TimeSpan? NextRetryDelay(RetryContext retryContext) => _delay;
}

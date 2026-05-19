using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
namespace CrossCut.Concerns.Loggers;

//Implements a customer logger based on Microsoft Logger design pattern
//Called Provider instead of Service to match Microsoft pattern

//InMemoryLoggerProvider keeps a list of log messages and creates a Logger to add
//messages to the list. Messages are prepared for JSON serialization
[ProviderAlias("InMemory")]
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly object _locker = new object();
    private readonly List<LogMessage> _messages = new List<LogMessage>();

    public Task<List<LogMessage>> MessagesAsync => Task.Run(() =>
    {
        lock (_locker)
        {

            //to create a a copy is simple using linq and copy constructor pattern
            var list = (_messages != null) ? _messages.Select(i => new LogMessage(i)).ToList<LogMessage>() : null;
            return list;
        }
    });
    public List<LogMessage> Messages => _messages.ToList();

    //Not used here, but could be used to close files etc
    void IDisposable.Dispose() { }

    //Needed by ILoggerProvider
    //Creates the Logger, notice the creation of an instance of InMemoryLogger (not provider)
    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(this, categoryName);

    private void Log<TState>(string categoryName, LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        var message = new LogMessage
        {
            Type = logLevel,
            Timestamp = DateTimeOffset.UtcNow,
            Message = formatter(state, exception) + (exception == null ? "" : "\r\n" + exception),
            Category = categoryName,
            EventId = eventId.Id,
        };

        lock (_locker)
            _messages.Add(message);
    }


    private sealed class InMemoryLogger : ILogger
    {
        private readonly InMemoryLoggerProvider _provider;
        private readonly string _categoryName;

        public InMemoryLogger(InMemoryLoggerProvider provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        //Needed by ILogger to logg a message. In our case add the log message to the list _messages
        //in InMemoryLoggerProvider 
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => _provider.Log(_categoryName, logLevel, eventId, state, exception, formatter);

        //Needed by ILogger, not used here
        public bool IsEnabled(LogLevel logLevel) => true;

        //Needed by ILogger, not used here
        public IDisposable BeginScope<TState>(TState state) => null;
    }
}


//The message structure to log, follows Microsoft guidelines
public sealed class LogMessage
{
    public LogLevel Type { get; set; }

    //Convert to Unix time stamp when JSON serialized
    [JsonConverter(typeof(TimestampJsonConverter))]
    public DateTimeOffset Timestamp { get; set; }

    public string Message { get; set; }

    public string Category { get; set; }

    public int EventId { get; set; }

    public override string ToString() => $"{Category}: {Message}";

    #region contructors
    public LogMessage() { }

    public LogMessage(LogMessage org)
    {
        Type = org.Type;
        Timestamp = org.Timestamp;
        Message = org.Message;
        Category = org.Category;
        EventId = org.EventId;
    }
    #endregion
}

public sealed class TimestampJsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(DateTimeOffset);

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        var dto = (DateTimeOffset)value;
        var delta = dto - DateTimeOffset.UnixEpoch;
        writer.WriteValue((long)delta.TotalMilliseconds);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var ticks = (long)reader.Value;
        return DateTimeOffset.UnixEpoch.AddMilliseconds(ticks);
    }
}


namespace Benchmark.Client.Services;
// Static class containing configuration settings for the chat client service. This allows for easy configuration of the client without needing to hardcode values in the code.
//Currently it get the URL of the SignalR hub from appsettings.json.
public class ChatSettings
{
    public string HubUrl { get; set; } = string.Empty;
}
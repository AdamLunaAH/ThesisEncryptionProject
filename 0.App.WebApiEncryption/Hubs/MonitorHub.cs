using App.WebApiEncryption.Services;
using Microsoft.AspNetCore.SignalR;

namespace App.WebApiEncryption.Hubs;

/// <summary>
/// Push-only hub that broadcasts <c>ResourceMetricsSnapshot</c> objects to
/// connected monitor clients once per broadcast interval.
///
/// No client-to-server methods are defined — clients simply connect and listen.
/// The hub is intentionally unauthenticated so the console monitor tool can
/// connect without needing a JWT token.
/// </summary>
public sealed class MonitorHub : Hub
{
    private readonly ConnectionTracker _tracker;

    public MonitorHub(ConnectionTracker tracker)
    {
        _tracker = tracker;
    }

    public override Task OnConnectedAsync()
    {
        _tracker.Add(Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _tracker.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

using System.Collections.Concurrent;

namespace App.WebApiEncryption.Services;

/// <summary>
/// Singleton service that tracks active SignalR connection IDs across all hubs.
/// Each hub calls <see cref="Add"/> in <c>OnConnectedAsync</c> and
/// <see cref="Remove"/> in <c>OnDisconnectedAsync</c>.
/// </summary>
public sealed class ConnectionTracker
{
    private readonly ConcurrentDictionary<string, byte> _connections = new();

    public void Add(string connectionId) =>
        _connections.TryAdd(connectionId, 0);

    public void Remove(string connectionId) =>
        _connections.TryRemove(connectionId, out _);

    /// <summary>Total number of currently active connections across all tracked hubs.</summary>
    public int Count => _connections.Count;
}

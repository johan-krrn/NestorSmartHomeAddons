namespace NestorBridge.Web;

public enum ConnectionState
{
  Disconnected,
  Connecting,
  Connected
}

public sealed record ConnectionStatus(
    string Name,
    ConnectionState State,
    DateTime? LastConnected = null);

/// <summary>
/// Tracks the connection state of all bridge components.
/// Updated by the various clients/bridges and exposed via the API.
/// </summary>
public sealed class ConnectionStatusTracker
{
  private readonly object _lock = new();
  private readonly Dictionary<string, ConnectionStatus> _statuses = new();

  public const string HaWebSocket = "ha_websocket";
  public const string CloudMqtt = "cloud_mqtt";
  public const string LocalMqtt = "local_mqtt";

  public ConnectionStatusTracker()
  {
    _statuses[HaWebSocket] = new("Home Assistant WebSocket", ConnectionState.Disconnected);
    _statuses[CloudMqtt] = new("Event Grid MQTT", ConnectionState.Disconnected);
    _statuses[LocalMqtt] = new("Mosquitto (local)", ConnectionState.Disconnected);
  }

  public void SetState(string key, ConnectionState state)
  {
    lock (_lock)
    {
      var current = _statuses.GetValueOrDefault(key);
      var lastConnected = state == ConnectionState.Connected
          ? DateTime.UtcNow
          : current?.LastConnected;
      _statuses[key] = current is not null
          ? current with { State = state, LastConnected = lastConnected }
          : new ConnectionStatus(key, state, lastConnected);
    }
  }

  public IReadOnlyList<ConnectionStatus> GetAll()
  {
    lock (_lock)
    {
      return _statuses.Values.ToList();
    }
  }
}

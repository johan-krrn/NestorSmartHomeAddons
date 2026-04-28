namespace NestorBridge.Mqtt;

/// <summary>
/// Subscribe-only MQTT client for the local Home Assistant Mosquitto broker.
/// Receives pairing and bridge events from zigbee2mqtt (or any local publisher)
/// and raises them for relay to the cloud.
/// </summary>
public interface ILocalMqttBridge
{
  /// <summary>Raised when a message is received on any subscribed local topic.</summary>
  event Func<string, byte[], Task>? MessageReceived;

  /// <summary>Connect to the local broker and subscribe to configured topics.</summary>
  Task ConnectAsync(CancellationToken cancellationToken);

  /// <summary>Disconnect cleanly.</summary>
  Task DisconnectAsync(CancellationToken cancellationToken);
}

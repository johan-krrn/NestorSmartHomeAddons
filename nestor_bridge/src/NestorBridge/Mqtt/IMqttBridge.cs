using MQTTnet.Protocol;

namespace NestorBridge.Mqtt;

public interface IMqttBridge
{
  /// <summary>Raised when a message is received on a subscribed topic.</summary>
  event Func<string, byte[], Task>? MessageReceived;

  /// <summary>Connect to the MQTT broker and subscribe to command topics.</summary>
  Task ConnectAsync(CancellationToken cancellationToken);

  /// <summary>Publish a message to the given topic.</summary>
  Task PublishAsync(string topic, byte[] payload, MqttQualityOfServiceLevel qos, CancellationToken cancellationToken);

  /// <summary>Disconnect cleanly.</summary>
  Task DisconnectAsync(CancellationToken cancellationToken);
}

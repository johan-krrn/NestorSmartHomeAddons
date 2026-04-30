using Microsoft.Extensions.Configuration;

namespace NestorBridge.Configuration;

public sealed class TelemetryFilterOptions
{
  [ConfigurationKeyName("domains")]
  public List<string> Domains { get; set; } = new();
}

public sealed class LocalMqttOptions
{
  /// <summary>Whether the local Mosquitto relay is active. Defaults to false (opt-in).</summary>
  [ConfigurationKeyName("enabled")]
  public bool Enabled { get; set; } = false;

  [ConfigurationKeyName("host")]
  public string Host { get; set; } = "core-mosquitto";

  [ConfigurationKeyName("port")]
  public int Port { get; set; } = 1883;

  [ConfigurationKeyName("username")]
  public string Username { get; set; } = string.Empty;

  [ConfigurationKeyName("password")]
  public string Password { get; set; } = string.Empty;

  /// <summary>Local Mosquitto topics to subscribe to and relay to the cloud.</summary>
  [ConfigurationKeyName("topics")]
  public List<string> Topics { get; set; } = new()
  {
    "zigbee2mqtt/bridge/event",
    "zigbee2mqtt/bridge/state"
  };
}

public sealed class BridgeOptions
{
  [ConfigurationKeyName("mqtt_client_id")]
  public string MqttClientId { get; set; } = string.Empty;

  [ConfigurationKeyName("box_id")]
  public string BoxId { get; set; } = string.Empty;

  [ConfigurationKeyName("log_level")]
  public string LogLevel { get; set; } = "info";

  [ConfigurationKeyName("telemetry_filter")]
  public TelemetryFilterOptions TelemetryFilter { get; set; } = new();

  [ConfigurationKeyName("local_mqtt")]
  public LocalMqttOptions LocalMqtt { get; set; } = new();

  /// <summary>
  /// Validates required configuration fields. Throws if invalid.
  /// </summary>
  public void Validate()
  {
    if (string.IsNullOrWhiteSpace(BoxId))
      throw new InvalidOperationException("box_id is required in options.json");
    if (string.IsNullOrWhiteSpace(MqttClientId))
      throw new InvalidOperationException("mqtt_client_id is required in options.json");
  }
}

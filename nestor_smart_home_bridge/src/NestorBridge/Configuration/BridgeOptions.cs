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
    "zigbee2mqtt/bridge/logging",
    "zigbee2mqtt/bridge/event",
    "zigbee2mqtt/bridge/state"
  };
}

public sealed class BridgeOptions
{
  [ConfigurationKeyName("mqtt_host")]
  public string MqttHost { get; set; } = "eg-nestorsmarthome-poc.westeurope-1.ts.eventgrid.azure.net";

  [ConfigurationKeyName("mqtt_port")]
  public int MqttPort { get; set; } = 8883;

  [ConfigurationKeyName("mqtt_client_id")]
  public string MqttClientId { get; set; } = string.Empty;

  [ConfigurationKeyName("box_id")]
  public string BoxId { get; set; } = string.Empty;

  [ConfigurationKeyName("cert_path")]
  public string CertPath { get; set; } = "/ssl/certs/device.crt";

  [ConfigurationKeyName("key_path")]
  public string KeyPath { get; set; } = "/ssl/certs/device.key";

  [ConfigurationKeyName("ca_path")]
  public string CaPath { get; set; } = "/ssl/certs/ca.crt";

  [ConfigurationKeyName("log_level")]
  public string LogLevel { get; set; } = "info";

  [ConfigurationKeyName("auth_mode")]
  public string AuthMode { get; set; } = "sas"; // "sas" | "x509"

  [ConfigurationKeyName("sas_username")]
  public string SasUsername { get; set; } = string.Empty;

  [ConfigurationKeyName("sas_password")]
  public string SasPassword { get; set; } = string.Empty;

  /// <summary>Disable TLS for local test against plain Mosquitto (never use in production).</summary>
  [ConfigurationKeyName("no_tls")]
  public bool NoTls { get; set; } = false;

  /// <summary>Override HA WebSocket endpoint (default: ws://supervisor/core/websocket).</summary>
  [ConfigurationKeyName("ha_ws_endpoint")]
  public string HaWsEndpoint { get; set; } = string.Empty;

  [ConfigurationKeyName("telemetry_filter")]
  public TelemetryFilterOptions TelemetryFilter { get; set; } = new();

  [ConfigurationKeyName("local_mqtt")]
  public LocalMqttOptions LocalMqtt { get; set; } = new();

  /// <summary>
  /// Validates required configuration fields. Throws if invalid.
  /// </summary>
  public void Validate()
  {
    if (string.IsNullOrWhiteSpace(MqttHost))
      throw new InvalidOperationException("mqtt_host is required in options.json");
    if (string.IsNullOrWhiteSpace(BoxId))
      throw new InvalidOperationException("box_id is required in options.json");
    if (string.IsNullOrWhiteSpace(MqttClientId))
      throw new InvalidOperationException("mqtt_client_id is required in options.json");
  }
}

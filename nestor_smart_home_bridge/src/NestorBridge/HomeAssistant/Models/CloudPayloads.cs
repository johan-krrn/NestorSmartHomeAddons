using System.Text.Json;
using System.Text.Json.Serialization;

namespace NestorBridge.HomeAssistant.Models;

/// <summary>
/// Cloud request received on devices/{boxId}/commands/requests.
/// </summary>
public sealed class CloudRequest
{
  [JsonPropertyName("Command")]
  public string Command { get; set; } = string.Empty;

  [JsonPropertyName("Payload")]
  public JsonElement? Payload { get; set; }

  [JsonPropertyName("TargetConnectionId")]
  public string TargetConnectionId { get; set; } = string.Empty;
}

/// <summary>
/// Response published on ha/commands/responses.
/// </summary>
public sealed class CloudRequestResponse
{
  [JsonPropertyName("TargetConnectionId")]
  public string TargetConnectionId { get; set; } = string.Empty;

  [JsonPropertyName("Data")]
  public JsonElement Data { get; set; }
}

/// <summary>
/// MQTT command payload received from the cloud.
/// </summary>
public sealed class CloudCommand
{
  [JsonPropertyName("commandId")]
  public string CommandId { get; set; } = string.Empty;

  [JsonPropertyName("issuedAt")]
  public string? IssuedAt { get; set; }

  [JsonPropertyName("targetEntityId")]
  public string TargetEntityId { get; set; } = string.Empty;

  [JsonPropertyName("action")]
  public string Action { get; set; } = string.Empty;

  [JsonPropertyName("parameters")]
  public Dictionary<string, object>? Parameters { get; set; }
}

/// <summary>
/// Ack payload sent back to cloud after command execution.
/// </summary>
public sealed class CommandAck
{
  [JsonPropertyName("commandId")]
  public string CommandId { get; set; } = string.Empty;

  [JsonPropertyName("status")]
  public string Status { get; set; } = string.Empty; // "success" | "error"

  [JsonPropertyName("error")]
  public string? Error { get; set; }

  [JsonPropertyName("completedAt")]
  public string CompletedAt { get; set; } = string.Empty;

  [JsonPropertyName("haResultContextId")]
  public string? HaResultContextId { get; set; }
}

/// <summary>
/// Telemetry payload for state changes sent to cloud.
/// </summary>
public sealed class TelemetryPayload
{
  [JsonPropertyName("entityId")]
  public string EntityId { get; set; } = string.Empty;

  [JsonPropertyName("state")]
  public string State { get; set; } = string.Empty;

  [JsonPropertyName("attributes")]
  public Dictionary<string, object>? Attributes { get; set; }

  [JsonPropertyName("lastChanged")]
  public string? LastChanged { get; set; }

  [JsonPropertyName("boxId")]
  public string BoxId { get; set; } = string.Empty;
}

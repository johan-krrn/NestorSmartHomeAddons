using System.Text.Json.Serialization;

namespace NestorBridge.HomeAssistant.Models;

/// <summary>
/// Represents a call_service command sent via HA WebSocket.
/// </summary>
public sealed class HaCallServiceMessage
{
  [JsonPropertyName("id")]
  public int Id { get; set; }

  [JsonPropertyName("type")]
  public string Type { get; set; } = "call_service";

  [JsonPropertyName("domain")]
  public string Domain { get; set; } = string.Empty;

  [JsonPropertyName("service")]
  public string Service { get; set; } = string.Empty;

  [JsonPropertyName("service_data")]
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, object>? ServiceData { get; set; }

  [JsonPropertyName("target")]
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public HaTarget? Target { get; set; }
}

public sealed class HaTarget
{
  [JsonPropertyName("entity_id")]
  public string EntityId { get; set; } = string.Empty;
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace NestorBridge.HomeAssistant.Models;

public sealed class HaMessage
{
  [JsonPropertyName("id")]
  public int? Id { get; set; }

  [JsonPropertyName("type")]
  public string Type { get; set; } = string.Empty;

  [JsonPropertyName("success")]
  public bool? Success { get; set; }

  [JsonPropertyName("ha_version")]
  public string? HaVersion { get; set; }

  [JsonPropertyName("event")]
  public HaEvent? Event { get; set; }

  [JsonPropertyName("error")]
  public HaError? Error { get; set; }

  [JsonPropertyName("result")]
  public JsonElement? Result { get; set; }
}

public sealed class HaError
{
  [JsonPropertyName("code")]
  public string Code { get; set; } = string.Empty;

  [JsonPropertyName("message")]
  public string Message { get; set; } = string.Empty;
}

public sealed class HaEvent
{
  [JsonPropertyName("event_type")]
  public string EventType { get; set; } = string.Empty;

  [JsonPropertyName("data")]
  public HaEventData? Data { get; set; }

  [JsonPropertyName("context")]
  public HaContext? Context { get; set; }
}

public sealed class HaEventData
{
  [JsonPropertyName("entity_id")]
  public string? EntityId { get; set; }

  [JsonPropertyName("old_state")]
  public HaState? OldState { get; set; }

  [JsonPropertyName("new_state")]
  public HaState? NewState { get; set; }
}

public sealed class HaState
{
  [JsonPropertyName("entity_id")]
  public string EntityId { get; set; } = string.Empty;

  [JsonPropertyName("state")]
  public string State { get; set; } = string.Empty;

  [JsonPropertyName("attributes")]
  public Dictionary<string, object>? Attributes { get; set; }

  [JsonPropertyName("last_changed")]
  public string? LastChanged { get; set; }

  [JsonPropertyName("last_updated")]
  public string? LastUpdated { get; set; }
}

public sealed class HaContext
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("parent_id")]
  public string? ParentId { get; set; }

  [JsonPropertyName("user_id")]
  public string? UserId { get; set; }
}

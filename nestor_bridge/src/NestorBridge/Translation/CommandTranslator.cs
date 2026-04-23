using System.Text.Json;
using Microsoft.Extensions.Logging;
using NestorBridge.HomeAssistant.Models;

namespace NestorBridge.Translation;

/// <summary>
/// Translates incoming MQTT command payloads into HA service call parameters.
/// </summary>
public sealed class CommandTranslator
{
  private readonly ILogger<CommandTranslator> _logger;

  public CommandTranslator(ILogger<CommandTranslator> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Deserialize an MQTT payload into a CloudCommand.
  /// Returns null if the payload is malformed.
  /// </summary>
  public CloudCommand? Translate(byte[] payload)
  {
    try
    {
      var command = JsonSerializer.Deserialize<CloudCommand>(payload);
      if (command is null || string.IsNullOrWhiteSpace(command.CommandId))
      {
        _logger.LogWarning("Received null or invalid command payload");
        return null;
      }

      if (string.IsNullOrWhiteSpace(command.TargetEntityId))
      {
        _logger.LogWarning("Command {CommandId} has no targetEntityId", command.CommandId);
        return null;
      }

      if (string.IsNullOrWhiteSpace(command.Action))
      {
        _logger.LogWarning("Command {CommandId} has no action", command.CommandId);
        return null;
      }

      return command;
    }
    catch (JsonException ex)
    {
      _logger.LogWarning(ex, "Failed to deserialize MQTT command payload");
      return null;
    }
  }

  /// <summary>
  /// Build an ack payload for a command result.
  /// </summary>
  public byte[] BuildAck(string commandId, bool success, string? error, string? haContextId)
  {
    var ack = new CommandAck
    {
      CommandId = commandId,
      Status = success ? "success" : "error",
      Error = error,
      CompletedAt = DateTime.UtcNow.ToString("o"),
      HaResultContextId = haContextId
    };

    return JsonSerializer.SerializeToUtf8Bytes(ack);
  }
}

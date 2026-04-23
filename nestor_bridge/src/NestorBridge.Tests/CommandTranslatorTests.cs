using NestorBridge.HomeAssistant.Models;
using NestorBridge.Translation;
using Microsoft.Extensions.Logging.Abstractions;

namespace NestorBridge.Tests;

public class CommandTranslatorTests
{
  private readonly CommandTranslator _translator = new(NullLogger<CommandTranslator>.Instance);

  [Fact]
  public void Translate_ValidPayload_ReturnsCommand()
  {
    var json = """
        {
            "commandId": "c7f3-test",
            "issuedAt": "2026-04-16T10:12:34Z",
            "targetEntityId": "light.salon",
            "action": "turn_on",
            "parameters": {
                "brightness_pct": 80
            }
        }
        """u8.ToArray();

    var result = _translator.Translate(json);

    Assert.NotNull(result);
    Assert.Equal("c7f3-test", result.CommandId);
    Assert.Equal("light.salon", result.TargetEntityId);
    Assert.Equal("turn_on", result.Action);
    Assert.NotNull(result.Parameters);
    Assert.True(result.Parameters.ContainsKey("brightness_pct"));
  }

  [Fact]
  public void Translate_MalformedJson_ReturnsNull()
  {
    var json = "not json"u8.ToArray();
    Assert.Null(_translator.Translate(json));
  }

  [Fact]
  public void Translate_MissingCommandId_ReturnsNull()
  {
    var json = """
        {
            "targetEntityId": "light.salon",
            "action": "turn_on"
        }
        """u8.ToArray();

    Assert.Null(_translator.Translate(json));
  }

  [Fact]
  public void Translate_MissingAction_ReturnsNull()
  {
    var json = """
        {
            "commandId": "c7f3-test",
            "targetEntityId": "light.salon"
        }
        """u8.ToArray();

    Assert.Null(_translator.Translate(json));
  }

  [Fact]
  public void BuildAck_Success_ReturnsValidPayload()
  {
    var ack = _translator.BuildAck("cmd-1", true, null, "ctx-abc");

    var parsed = System.Text.Json.JsonSerializer.Deserialize<CommandAck>(ack);
    Assert.NotNull(parsed);
    Assert.Equal("cmd-1", parsed.CommandId);
    Assert.Equal("success", parsed.Status);
    Assert.Null(parsed.Error);
    Assert.Equal("ctx-abc", parsed.HaResultContextId);
  }

  [Fact]
  public void BuildAck_Error_IncludesErrorMessage()
  {
    var ack = _translator.BuildAck("cmd-2", false, "Entity not found", null);

    var parsed = System.Text.Json.JsonSerializer.Deserialize<CommandAck>(ack);
    Assert.NotNull(parsed);
    Assert.Equal("error", parsed.Status);
    Assert.Equal("Entity not found", parsed.Error);
  }
}

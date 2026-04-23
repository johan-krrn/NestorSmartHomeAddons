using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NestorBridge.Configuration;
using NestorBridge.HomeAssistant.Models;
using NestorBridge.Translation;

namespace NestorBridge.Tests;

public class TelemetryTranslatorTests
{
  private static TelemetryTranslator CreateTranslator(params string[] domains)
  {
    var options = Options.Create(new BridgeOptions
    {
      BoxId = "nestor-test-box",
      TelemetryFilter = new TelemetryFilterOptions { Domains = domains.ToList() }
    });
    return new TelemetryTranslator(options, NullLogger<TelemetryTranslator>.Instance);
  }

  [Fact]
  public void Translate_AllowedDomain_ReturnsPayload()
  {
    var translator = CreateTranslator("light", "sensor");
    var haEvent = CreateStateChangedEvent("light.salon", "on");

    var result = translator.Translate(haEvent);

    Assert.NotNull(result);
    Assert.Equal("light.salon", result.Value.EntityId);
    Assert.NotEmpty(result.Value.Payload);
  }

  [Fact]
  public void Translate_FilteredDomain_ReturnsNull()
  {
    var translator = CreateTranslator("light");
    var haEvent = CreateStateChangedEvent("automation.test", "on");

    Assert.Null(translator.Translate(haEvent));
  }

  [Fact]
  public void Translate_EmptyFilter_AllowsAll()
  {
    var translator = CreateTranslator(); // No filter
    var haEvent = CreateStateChangedEvent("automation.test", "on");

    Assert.NotNull(translator.Translate(haEvent));
  }

  [Fact]
  public void Translate_NullNewState_ReturnsNull()
  {
    var translator = CreateTranslator("light");
    var haEvent = new HaEvent
    {
      EventType = "state_changed",
      Data = new HaEventData { NewState = null }
    };

    Assert.Null(translator.Translate(haEvent));
  }

  [Fact]
  public void Translate_PayloadContainsBoxId()
  {
    var translator = CreateTranslator("sensor");
    var haEvent = CreateStateChangedEvent("sensor.temp", "21.5");

    var result = translator.Translate(haEvent);
    Assert.NotNull(result);

    var json = System.Text.Encoding.UTF8.GetString(result.Value.Payload);
    Assert.Contains("nestor-test-box", json);
  }

  private static HaEvent CreateStateChangedEvent(string entityId, string state)
  {
    return new HaEvent
    {
      EventType = "state_changed",
      Data = new HaEventData
      {
        NewState = new HaState
        {
          EntityId = entityId,
          State = state,
          Attributes = new Dictionary<string, object>
          {
            ["friendly_name"] = "Test Entity"
          },
          LastChanged = "2026-04-16T10:10:02Z"
        }
      }
    };
  }
}

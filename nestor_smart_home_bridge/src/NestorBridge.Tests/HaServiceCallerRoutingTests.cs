using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NestorBridge.HomeAssistant;
using NestorBridge.HomeAssistant.Models;

namespace NestorBridge.Tests;

public class HaServiceCallerRoutingTests
{
  private readonly IHaWebSocketClient _ws = Substitute.For<IHaWebSocketClient>();
  private readonly IHaRestClient _rest = Substitute.For<IHaRestClient>();
  private readonly HaServiceCaller _caller;

  public HaServiceCallerRoutingTests()
  {
    _caller = new HaServiceCaller(_ws, _rest, NullLogger<HaServiceCaller>.Instance);
  }

  [Theory]
  [InlineData("create")]
  [InlineData("update")]
  [InlineData("CREATE")]   // case-insensitive
  [InlineData("Update")]
  public async Task AutomationCrudAction_RoutesToRestApi_NotWebSocket(string action)
  {
    var config = new Dictionary<string, object> { ["alias"] = "My Automation" };
    _rest
        .CreateOrUpdateAutomationAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
        .Returns((true, (string?)null));

    var command = new CloudCommand
    {
      CommandId = "cmd-1",
      TargetEntityId = "automation.my_automation",
      Action = action,
      Parameters = config
    };

    var (success, _, error) = await _caller.ExecuteCommandAsync(command, CancellationToken.None);

    Assert.True(success);
    Assert.Null(error);
    await _rest.Received(1).CreateOrUpdateAutomationAsync(
        "my_automation", Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
    await _ws.DidNotReceive().CallServiceAsync(
        Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
        Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>());
  }

  [Fact]
  public async Task Delete_AutomationDomain_RoutesToRestApi()
  {
    _rest
        .DeleteAutomationAsync("morning_routine", Arg.Any<CancellationToken>())
        .Returns((true, (string?)null));

    var command = new CloudCommand
    {
      CommandId = "cmd-2",
      TargetEntityId = "automation.morning_routine",
      Action = "delete"
    };

    var (success, _, error) = await _caller.ExecuteCommandAsync(command, CancellationToken.None);

    Assert.True(success);
    Assert.Null(error);
    await _rest.Received(1).DeleteAutomationAsync("morning_routine", Arg.Any<CancellationToken>());
    await _ws.DidNotReceive().CallServiceAsync(
        Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
        Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>());
  }

  [Theory]
  [InlineData("trigger")]
  [InlineData("turn_on")]
  [InlineData("reload")]
  public async Task AutomationNonCrudAction_RoutesToWebSocket(string action)
  {
    _ws
        .CallServiceAsync("automation", action, "automation.my_automation",
            Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>())
        .Returns(new HaMessage { Success = true });

    var command = new CloudCommand
    {
      CommandId = "cmd-3",
      TargetEntityId = "automation.my_automation",
      Action = action
    };

    await _caller.ExecuteCommandAsync(command, CancellationToken.None);

    await _ws.Received(1).CallServiceAsync(
        "automation", action, "automation.my_automation",
        Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>());
    await _rest.DidNotReceive().CreateOrUpdateAutomationAsync(
        Arg.Any<string>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
    await _rest.DidNotReceive().DeleteAutomationAsync(
        Arg.Any<string>(), Arg.Any<CancellationToken>());
  }

  [Fact]
  public async Task OtherDomain_AlwaysRoutesToWebSocket()
  {
    _ws
        .CallServiceAsync("light", "turn_on", "light.salon",
            Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>())
        .Returns(new HaMessage { Success = true });

    var command = new CloudCommand
    {
      CommandId = "cmd-4",
      TargetEntityId = "light.salon",
      Action = "turn_on"
    };

    await _caller.ExecuteCommandAsync(command, CancellationToken.None);

    await _ws.Received(1).CallServiceAsync(
        Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
        Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>());
    await _rest.DidNotReceive().CreateOrUpdateAutomationAsync(
        Arg.Any<string>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
  }

  [Fact]
  public async Task CreateWithNullParameters_ReturnsFalseWithError()
  {
    var command = new CloudCommand
    {
      CommandId = "cmd-5",
      TargetEntityId = "automation.my_automation",
      Action = "create",
      Parameters = null
    };

    var (success, _, error) = await _caller.ExecuteCommandAsync(command, CancellationToken.None);

    Assert.False(success);
    Assert.NotNull(error);
    Assert.Contains("parameters", error, StringComparison.OrdinalIgnoreCase);
    await _rest.DidNotReceive().CreateOrUpdateAutomationAsync(
        Arg.Any<string>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
  }

  [Fact]
  public async Task RestApiError_IsReturnedAsCommandError()
  {
    _rest
        .CreateOrUpdateAutomationAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
        .Returns((false, "HTTP 400: invalid automation config"));

    var command = new CloudCommand
    {
      CommandId = "cmd-6",
      TargetEntityId = "automation.my_automation",
      Action = "create",
      Parameters = new Dictionary<string, object> { ["alias"] = "Bad" }
    };

    var (success, _, error) = await _caller.ExecuteCommandAsync(command, CancellationToken.None);

    Assert.False(success);
    Assert.Equal("HTTP 400: invalid automation config", error);
  }
}

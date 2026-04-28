using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NestorBridge.HomeAssistant;

namespace NestorBridge.Tests;

public class HaRestClientTests
{
  private static (HaRestClient Client, MockHttpMessageHandler Handler) CreateClient(
      HttpStatusCode statusCode = HttpStatusCode.OK,
      string responseBody = "{}")
  {
    var handler = new MockHttpMessageHandler(statusCode, responseBody);
    var httpClient = new HttpClient(handler)
    {
      BaseAddress = new Uri("http://supervisor/core/api/")
    };
    return (new HaRestClient(httpClient, NullLogger<HaRestClient>.Instance), handler);
  }

  // ── CreateOrUpdate ────────────────────────────────────────────────

  [Fact]
  public async Task CreateOrUpdate_Success_ReturnsTrue()
  {
    var (client, _) = CreateClient(HttpStatusCode.OK);
    var config = new Dictionary<string, object> { ["alias"] = "Test" };

    var (success, error) = await client.CreateOrUpdateAutomationAsync(
        "test_automation", config, CancellationToken.None);

    Assert.True(success);
    Assert.Null(error);
  }

  [Fact]
  public async Task CreateOrUpdate_UsesPostMethodAndCorrectUrl()
  {
    var (client, handler) = CreateClient(HttpStatusCode.OK);
    var config = new Dictionary<string, object> { ["alias"] = "Test" };

    await client.CreateOrUpdateAutomationAsync("my_automation", config, CancellationToken.None);

    Assert.NotNull(handler.LastRequest);
    Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
    Assert.Equal(
        "http://supervisor/core/api/config/automation/config/my_automation",
        handler.LastRequest.RequestUri!.AbsoluteUri);
  }

  [Fact]
  public async Task CreateOrUpdate_AutomationIdIsUrlEncoded()
  {
    var (client, handler) = CreateClient(HttpStatusCode.OK);
    var config = new Dictionary<string, object> { ["alias"] = "Test" };

    await client.CreateOrUpdateAutomationAsync("my automation", config, CancellationToken.None);

    // AbsoluteUri preserves percent-encoding; ToString() decodes it.
    Assert.Equal(
        "http://supervisor/core/api/config/automation/config/my%20automation",
        handler.LastRequest!.RequestUri!.AbsoluteUri);
  }

  [Fact]
  public async Task CreateOrUpdate_ServerError_ReturnsFalseWithStatusCode()
  {
    var (client, _) = CreateClient(HttpStatusCode.BadRequest, "{\"message\":\"invalid config\"}");
    var config = new Dictionary<string, object> { ["alias"] = "Bad" };

    var (success, error) = await client.CreateOrUpdateAutomationAsync(
        "bad_automation", config, CancellationToken.None);

    Assert.False(success);
    Assert.NotNull(error);
    Assert.Contains("400", error);
  }

  // ── Delete ────────────────────────────────────────────────────────

  [Fact]
  public async Task Delete_Success_ReturnsTrue()
  {
    var (client, handler) = CreateClient(HttpStatusCode.OK);

    var (success, error) = await client.DeleteAutomationAsync("my_automation", CancellationToken.None);

    Assert.True(success);
    Assert.Null(error);
    Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
    Assert.Equal(
        "http://supervisor/core/api/config/automation/config/my_automation",
        handler.LastRequest.RequestUri!.AbsoluteUri);
  }

  [Fact]
  public async Task Delete_NotFound_ReturnsTrue_Idempotent()
  {
    var (client, _) = CreateClient(HttpStatusCode.NotFound);

    var (success, error) = await client.DeleteAutomationAsync("ghost_automation", CancellationToken.None);

    Assert.True(success);   // 404 treated as already-deleted
    Assert.Null(error);
  }

  [Fact]
  public async Task Delete_ServerError_ReturnsFalseWithStatusCode()
  {
    var (client, _) = CreateClient(HttpStatusCode.InternalServerError, "internal error");

    var (success, error) = await client.DeleteAutomationAsync("my_automation", CancellationToken.None);

    Assert.False(success);
    Assert.NotNull(error);
    Assert.Contains("500", error);
  }
}

// ---------------------------------------------------------------------------
// Minimal mock — no external package required.
// ---------------------------------------------------------------------------
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
  private readonly HttpStatusCode _statusCode;
  private readonly string _responseBody;

  public HttpRequestMessage? LastRequest { get; private set; }

  public MockHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
  {
    _statusCode = statusCode;
    _responseBody = responseBody;
  }

  protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
  {
    LastRequest = request;
    return Task.FromResult(new HttpResponseMessage(_statusCode)
    {
      Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json")
    });
  }
}

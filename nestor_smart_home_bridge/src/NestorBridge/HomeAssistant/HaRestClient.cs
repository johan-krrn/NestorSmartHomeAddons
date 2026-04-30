using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NestorBridge.HomeAssistant;

/// <summary>
/// HTTP client for Home Assistant's Config REST API.
/// The <see cref="HttpClient"/> is pre-configured (BaseAddress, Authorization header)
/// by the DI factory in Program.cs — this class is intentionally free of env-var reads.
/// </summary>
public sealed class HaRestClient : IHaRestClient
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<HaRestClient> _logger;

  public HaRestClient(HttpClient httpClient, ILogger<HaRestClient> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  /// <inheritdoc/>
  public async Task<(bool Success, string? Error)> CreateOrUpdateAutomationAsync(
      string automationId,
      Dictionary<string, object> config,
      CancellationToken cancellationToken)
  {
    var url = $"config/automation/config/{Uri.EscapeDataString(automationId)}";
    var body = JsonSerializer.Serialize(config);

    _logger.LogInformation("REST POST {Url}", url);

    HttpResponseMessage response;
    try
    {
      using var content = new StringContent(body, Encoding.UTF8, "application/json");
      response = await _httpClient.PostAsync(url, content, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "HTTP request failed for POST {Url}", url);
      return (false, ex.Message);
    }

    if (response.IsSuccessStatusCode)
      return (true, null);

    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
    var error = $"HTTP {(int)response.StatusCode}: {responseBody}";
    _logger.LogError("REST POST {Url} failed: {Error}", url, error);
    return (false, error);
  }

  /// <inheritdoc/>
  public async Task<(bool Success, string? Error)> DeleteAutomationAsync(
      string automationId,
      CancellationToken cancellationToken)
  {
    var url = $"config/automation/config/{Uri.EscapeDataString(automationId)}";

    _logger.LogInformation("REST DELETE {Url}", url);

    HttpResponseMessage response;
    try
    {
      response = await _httpClient.DeleteAsync(url, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "HTTP request failed for DELETE {Url}", url);
      return (false, ex.Message);
    }

    // 404 = already deleted — treat as success (idempotent delete)
    if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
      return (true, null);

    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
    var error = $"HTTP {(int)response.StatusCode}: {responseBody}";
    _logger.LogError("REST DELETE {Url} failed: {Error}", url, error);
    return (false, error);
  }

  /// <inheritdoc/>
  public async Task<(bool Success, JsonElement? Data, string? Error)> GetRawAsync(
      string path,
      CancellationToken cancellationToken)
  {
    _logger.LogInformation("REST GET {Path}", path);

    HttpResponseMessage response;
    try
    {
      response = await _httpClient.GetAsync(path, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "HTTP GET request failed for {Path}", path);
      return (false, null, ex.Message);
    }

    var body = await response.Content.ReadAsStringAsync(cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
      var error = $"HTTP {(int)response.StatusCode}: {body}";
      _logger.LogError("REST GET {Path} failed: {Error}", path, error);
      return (false, null, error);
    }

    using var doc = JsonDocument.Parse(body);
    return (true, doc.RootElement.Clone(), null);
  }
}

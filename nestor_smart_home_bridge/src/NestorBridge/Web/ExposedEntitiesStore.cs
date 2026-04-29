using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NestorBridge.Web;

public sealed record ExposedEntity(
    string EntityId,
    string? FriendlyName,
    string? Domain,
    DateTime AddedAt);

/// <summary>
/// Persists the list of entities the user wants to expose via the special command.
/// Stored as JSON on disk so it survives restarts.
/// </summary>
public sealed class ExposedEntitiesStore
{
  private readonly string _filePath;
  private readonly ILogger<ExposedEntitiesStore> _logger;
  private readonly object _lock = new();
  private List<ExposedEntity> _entities = new();

  private static readonly JsonSerializerOptions JsonOpts = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
  };

  public ExposedEntitiesStore(ILogger<ExposedEntitiesStore> logger)
  {
    _logger = logger;
    // Store in /data/ (persistent HA addon storage) or fallback to current dir
    var dataDir = Directory.Exists("/data") ? "/data" : AppContext.BaseDirectory;
    _filePath = Path.Combine(dataDir, "exposed_entities.json");
    Load();
  }

  public IReadOnlyList<ExposedEntity> GetAll()
  {
    lock (_lock) return _entities.ToList();
  }

  public ExposedEntity Add(string entityId, string? friendlyName)
  {
    lock (_lock)
    {
      if (_entities.Any(e => e.EntityId == entityId))
        throw new InvalidOperationException($"Entity '{entityId}' is already exposed.");

      var domain = entityId.Contains('.') ? entityId.Split('.')[0] : null;
      var entity = new ExposedEntity(entityId, friendlyName, domain, DateTime.UtcNow);
      _entities.Add(entity);
      Save();
      return entity;
    }
  }

  public bool Remove(string entityId)
  {
    lock (_lock)
    {
      var removed = _entities.RemoveAll(e => e.EntityId == entityId) > 0;
      if (removed) Save();
      return removed;
    }
  }

  private void Load()
  {
    try
    {
      if (File.Exists(_filePath))
      {
        var json = File.ReadAllText(_filePath);
        _entities = JsonSerializer.Deserialize<List<ExposedEntity>>(json, JsonOpts) ?? new();
        _logger.LogInformation("Loaded {Count} exposed entities from {Path}", _entities.Count, _filePath);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to load exposed entities from {Path}", _filePath);
      _entities = new();
    }
  }

  private void Save()
  {
    try
    {
      var json = JsonSerializer.Serialize(_entities, JsonOpts);
      File.WriteAllText(_filePath, json);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to save exposed entities to {Path}", _filePath);
    }
  }
}

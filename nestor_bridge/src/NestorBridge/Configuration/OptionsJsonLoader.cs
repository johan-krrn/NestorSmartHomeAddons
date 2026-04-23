using Microsoft.Extensions.Configuration;

namespace NestorBridge.Configuration;

/// <summary>
/// Loads /data/options.json injected by HA Supervisor.
/// Snake_case keys in the JSON file are mapped to C# properties via [ConfigurationKeyName] on BridgeOptions.
/// </summary>
public static class OptionsJsonLoader
{
  public static IConfigurationBuilder AddHaOptionsJson(
      this IConfigurationBuilder builder,
      string? path = null)
  {
    // Allow overriding the path via env var for local development
    path ??= Environment.GetEnvironmentVariable("OPTIONS_JSON_PATH") ?? "/data/options.json";

    if (File.Exists(path))
    {
      builder.AddJsonFile(path, optional: false, reloadOnChange: false);
    }

    return builder;
  }
}

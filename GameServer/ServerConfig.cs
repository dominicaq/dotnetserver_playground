using System.Text.Json;

namespace GameServer;

public class ServerConfig {
  private static readonly JsonSerializerOptions _deserializeOptions = new() {
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip
  };

  private static readonly JsonSerializerOptions _serializeOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  // Server settings
  public string ServerName { get; set; } = "Game Server";
  public int ServerPort { get; set; } = 7777;
  public string ServerConnectionKey { get; set; } = "default_key";
  public int ServerMaxPlayers { get; set; } = 100;
  public int ServerGameMode { get; set; } = 0;
  public bool ServerAllowCheats { get; set; } = false;

  // Network settings
  public int NetworkTickRate { get; set; } = 60;
  public int NetworkDisconnectTimeout { get; set; } = 5000;
  public bool NetworkEnableHeartbeat { get; set; } = true;
  public int NetworkHeartbeatInterval { get; set; } = 1000;

  // Logging
  public bool LoggingEnableConsole { get; set; } = true;
  public bool LoggingEnableTick { get; set; } = false;
  public bool LoggingPlayerEvents { get; set; } = true;
  public bool LoggingNetworkEvents { get; set; } = false;
  public bool LoggingEnableFile { get; set; } = false;
  public string LoggingFilePath { get; set; } = "server.log";
  public bool LoggingEnableErrors { get; set; } = true;
  public bool LoggingEnablePerformance { get; set; } = false;

  public static ServerConfig LoadFromFile(string configPath = "server_config.json") {
    try {
      if (!File.Exists(configPath)) {
        Console.WriteLine($"Config file not found at {configPath}. Creating default config...");
        ServerConfig defaultConfig = new();
        defaultConfig.SaveToFile(configPath);
        return defaultConfig;
      }

      string jsonString = File.ReadAllText(configPath);
      ServerConfig? config = JsonSerializer.Deserialize<ServerConfig>(jsonString, _deserializeOptions);

      if (config == null) {
        throw new InvalidOperationException("Failed to deserialize config file - result was null");
      }

      return config;
    } catch (Exception ex) {
      throw new InvalidOperationException($"Error loading config from {configPath}: {ex.Message}", ex);
    }
  }

  public void SaveToFile(string configPath = "server_config.json") {
    try {
      string jsonString = JsonSerializer.Serialize(this, _serializeOptions);
      File.WriteAllText(configPath, jsonString);
      Console.WriteLine($"Config saved to {configPath}");
    } catch (Exception ex) {
      throw new InvalidOperationException($"Error saving config to {configPath}: {ex.Message}", ex);
    }
  }
}

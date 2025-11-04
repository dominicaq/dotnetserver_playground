using System.Text.Json;

namespace GameNetworking;

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
    public string ServerName { get; set; } = "default_name";
    public int ServerPort { get; set; } = 7777;
    public string ServerConnectionKey { get; set; } = "default_key";
    public int ServerMaxPlayers { get; set; } = 10;
    public int ServerGameMode { get; set; } = 0;
    public bool ServerAllowCheats { get; set; } = false;

    // Network settings
    public int NetworkTickRate { get; set; } = 60;
    public int NetworkDisconnectTimeout { get; set; } = 5000;

    // Logging
    public bool LoggingEnable { get; set; } = true;
    public bool LoggingPlayerEvents { get; set; } = true;
    public bool LoggingNetworkEvents { get; set; } = false;
    public string LoggingFilePath { get; set; } = "";

    public static ServerConfig LoadFromFile(string configPath = "server_config.json") {
        try {
            if (!File.Exists(configPath)) {
                Console.WriteLine($"Config file not found at {configPath}. Creating default config...");
                ServerConfig defaultConfig = new();
                defaultConfig.SaveToFile(configPath);
                return defaultConfig;
            }

            string jsonString = File.ReadAllText(configPath);
            ServerConfig? config = JsonSerializer.Deserialize<ServerConfig>(jsonString, _deserializeOptions)
                ?? throw new InvalidOperationException("Failed to deserialize config file - result was null");
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

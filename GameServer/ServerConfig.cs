using System;
using System.IO;
using System.Text.Json;

namespace GameServer;

public class ServerConfig
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Server settings
    public string? ServerName { get; set; }
    public int ServerPort { get; set; }
    public string? ServerConnectionKey { get; set; }
    public int ServerMaxPlayers { get; set; }
    public int ServerGameMode { get; set; }
    public bool ServerAllowCheats { get; set; }
    public string[]? ServerAdminList { get; set; }
    public string[]? ServerBanList { get; set; }

    // Network settings
    public int NetworkTickRate { get; set; }
    public int NetworkDisconnectTimeout { get; set; }
    public bool NetworkEnableHeartbeat { get; set; }
    public int NetworkHeartbeatInterval { get; set; }

    // Logging
    public bool LoggingEnableConsole { get; set; }
    public bool LoggingEnableTick { get; set; }
    public bool LoggingPlayerEvents { get; set; }
    public bool LoggingNetworkEvents { get; set; }
    public bool LoggingEnableFile { get; set; }
    public string? LoggingFilePath { get; set; }
    public bool LoggingEnableErrors { get; set; }
    public bool LoggingEnablePerformance { get; set; }

    public static ServerConfig? LoadFromFile(string configPath = "server_config.json")
    {
        try
        {
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Config file not found at {configPath}.");
                return null;
            }

            var jsonString = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<ServerConfig>(jsonString, DeserializeOptions);

            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading config: {ex.Message}");
            return null;
        }
    }

    public void SaveToFile(string configPath = "server_config.json")
    {
        try
        {
            var jsonString = JsonSerializer.Serialize(this, SerializeOptions);
            File.WriteAllText(configPath, jsonString);
            Console.WriteLine($"Config saved to {configPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving config: {ex.Message}");
        }
    }
}

using System;
using GameServer;
using LiteNetLib;

namespace TestBench;

public class TestServer {
  public static void Run(string[] args) {
    Console.WriteLine("Starting Game Server Test Host...");

    ServerConfig config = ServerConfig.LoadFromFile("server_config.json");
    Server server = new(config);
    server.ServerEvent += OnServerEvent;
    server.Start();

    Console.WriteLine("Press 'q' to quit, 's' to show server info");

    bool running = true;
    while (running) {
      ConsoleKeyInfo key = Console.ReadKey(true);
      switch (key.KeyChar) {
        case 'q':
        case 'Q':
          running = false;
          break;
        case 's':
        case 'S':
          ShowServerInfo(server);
          break;
      }
    }

    server.Stop();
    Console.WriteLine("Server shutdown complete.");
  }

  private static void OnServerEvent(string eventType, NetPeer? peer, string message) {
    string timestamp = DateTime.Now.ToString("HH:mm:ss");
    string peerInfo = peer != null ? $"[{peer.Address}:{peer.Port}]" : "[SERVER]";

    ConsoleColor originalColor = Console.ForegroundColor;
    Console.ForegroundColor = eventType switch {
      "PlayerJoined" => ConsoleColor.Green,
      "PlayerLeft" => ConsoleColor.Yellow,
      "PlayerBanned" => ConsoleColor.Red,
      "ConnectionRejected" => ConsoleColor.Magenta,
      "ServerShutdown" => ConsoleColor.Cyan,
      _ => ConsoleColor.White
    };

    Console.WriteLine($"[{timestamp}] {eventType} {peerInfo}: {message}");
    Console.ForegroundColor = originalColor;
  }

  private static void ShowServerInfo(Server server) {
    Console.WriteLine("\n=== Server Info ===");
    Console.WriteLine($"Server Name: {server.Config.ServerName}");
    Console.WriteLine($"Port: {server.Config.ServerPort}");
    Console.WriteLine($"Max Players: {server.Config.ServerMaxPlayers}");
    Console.WriteLine($"Tick Rate: {server.Config.NetworkTickRate}");
    Console.WriteLine($"Game Mode: {server.Config.ServerGameMode}");
    Console.WriteLine($"Allow Cheats: {server.Config.ServerAllowCheats}");
    Console.WriteLine("===================\n");
  }
}

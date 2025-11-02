using System;
using GameServer;

namespace TestHost;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Starting Game Server Test Host...");

        var config = ServerConfig.LoadFromFile("server_config.json");
        if (config == null)
        {
            Console.WriteLine("Failed to load server configuration. Exiting...");
            return;
        }

        var server = new Server(config);
        server.Start();

        Console.WriteLine("Press 'q' to quit, 's' to show server info");

        bool running = true;
        while (running)
        {
            var key = Console.ReadKey(true);
            switch (key.KeyChar)
            {
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

    static void ShowServerInfo(Server server)
    {
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

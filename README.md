# GameServer
A .NET 9.0 game server/client abstraction built with LiteNetLib.

## Building GameNetworking.dll for general use
```bash
./export-dll.sh
```

## Test Bench Quickstart
*Note: Automatically builds and places the .dll*

```bash
cd TestBench

# Start the server
dotnet run

# Start the client (on another machine or in a separate terminal)
dotnet run client
```

### Connecting the Client
When launching the client, enter the server address provided.

Here's the special cases:
```bash
# Same machine
127.0.0.1:8473

# Different machine (same router / LAN)
192.168.x.x:8473
```
*Note: The default port is `8473`*

To find your machine’s local IP (for LAN testing), run:
```bash
ip addr show
```

Edit `server_config.json` to configure.

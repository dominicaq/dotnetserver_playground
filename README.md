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
When launching the client, enter the server code to connect. The client
automatically detects whether you're connecting from the same machine,
local network (LAN), or over the internet.

*Note: The default port is `8473`, if that port is already mapped you need to
edit the server config.*


Edit `server_config.json` to configure.

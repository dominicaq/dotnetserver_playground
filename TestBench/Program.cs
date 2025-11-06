namespace TestBench;

public class Program {
    public static async Task Main(string[] args) {
        if (args.Length > 0 && args[0].Equals("client", StringComparison.CurrentCultureIgnoreCase)) {
            await TestClient.Run(args);
        } else {
            await TestServer.Run(args);
        }
    }
}

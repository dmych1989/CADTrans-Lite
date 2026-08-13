// Program.cs
// TCP JSON-RPC server that exposes the CADTrans Lite translation pipeline to MCP clients.
// Protocol: each request is a single newline-delimited JSON object {command, params, id};
// each response is a newline-delimited JSON object {success, data?, error?, id}.
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CADTransLite.McpServer;

internal sealed class Program
{
    private static int _port = 8090;

    static async Task Main(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var pt)) _port = pt;
            else if (args[i].StartsWith("--port=") && int.TryParse(args[i].Substring(7), out var pt2)) _port = pt2;
            else if (args[i] == "--help" || args[i] == "-h")
            {
                Console.WriteLine("CADTrans Lite MCP Bridge");
                Console.WriteLine("Usage: CADTransLite.McpBridge.exe [--port=8090]");
                Console.WriteLine("Starts a TCP JSON-RPC server on 127.0.0.1:<port> for MCP clients.");
                return;
            }
        }

        Console.WriteLine($"[CADTrans MCP Bridge] starting on port {_port} ...");
        var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();
        Console.WriteLine($"[CADTrans MCP Bridge] listening at 127.0.0.1:{_port} (Ctrl+C to stop)");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        while (!cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _ = Task.Run(() => HandleClient(client, cts.Token), cts.Token);
        }

        listener.Stop();
        Console.WriteLine("[CADTrans MCP Bridge] stopped.");
    }

    static async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch
            {
                break;
            }

            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            BridgeResponse resp;
            try
            {
                var req = JsonSerializer.Deserialize<BridgeRequest>(line);
                resp = req == null
                    ? BridgeResponse.Fail("空请求")
                    : await BridgeHandler.Dispatch(req, ct);
            }
            catch (Exception ex)
            {
                resp = BridgeResponse.Fail($"请求解析/执行失败：{ex.Message}");
            }

            try
            {
                var json = JsonSerializer.Serialize(resp, JsonUtils.Options);
                await writer.WriteLineAsync(json);
            }
            catch
            {
                break;
            }
        }
    }
}

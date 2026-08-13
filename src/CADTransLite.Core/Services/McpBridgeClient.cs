// McpBridgeClient.cs
// TCP JSON-RPC client for the CADTrans Lite MCP bridge (CADTransLite.McpBridge.exe).
// Lets the WPF UI drive the same translation pipeline that the MCP server exposes,
// so a "一键 MCP 翻译" button can translate the currently loaded drawing via the bridge.
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CADTransLite.Core.Services;

/// <summary>Result of a bridge command.</summary>
public sealed class BridgeResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public JsonElement? Data { get; set; }
}

/// <summary>
/// Thin TCP JSON-RPC client speaking the same newline-delimited JSON protocol as the
/// C# MCP bridge. Opens a fresh connection per call (the bridge handles one client per task).
/// </summary>
public sealed class McpBridgeClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;

    public McpBridgeClient(string host = "127.0.0.1", int port = 8090)
    {
        _host = host;
        _port = port;
    }

    public async Task<BridgeResult> SendAsync(
        string command,
        Dictionary<string, object>? parameters = null,
        int timeoutMs = 600000,
        CancellationToken ct = default)
    {
        using var client = new TcpClient();
        // Connect with an overall timeout so a missing bridge fails fast with a clear error.
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(Math.Min(timeoutMs, 5000));
        try
        {
            await client.ConnectAsync(_host, _port, connectCts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"无法连接 MCP 桥接服务（{_host}:{_port}）。请先启动 CADTransLite.McpBridge.exe。");
        }

        using var stream = client.GetStream();
        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var req = new Dictionary<string, object?>
        {
            ["command"] = command,
            ["params"] = parameters ?? new Dictionary<string, object>()
        };
        var json = JsonSerializer.Serialize(req) + "\n";
        await writer.WriteAsync(json.AsMemory(), ct);

        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(timeoutMs);
        var line = await reader.ReadLineAsync(readCts.Token);
        if (line == null)
            throw new InvalidOperationException("MCP 桥接连接已关闭（服务可能已退出）。");

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        string? error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
        // Clone the data node so the returned JsonElement is independent of this (disposed) document.
        // Otherwise the caller reading Data later throws "Cannot access a disposed object. Object name: 'JsonDocument'".
        JsonElement? data = root.TryGetProperty("data", out var d) ? d.Clone() : null;
        return new BridgeResult { Success = success, Error = error, Data = data };
    }

    public void Dispose()
    {
        // TcpClient/streams are disposed per-call inside SendAsync; nothing persistent to clean.
        GC.SuppressFinalize(this);
    }
}

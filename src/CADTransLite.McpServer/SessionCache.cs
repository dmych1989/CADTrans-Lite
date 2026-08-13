// SessionCache.cs
// Keeps extracted drawings in memory so a subsequent write_translation can reuse the same
// TranslationItem set (mirrors how CADMcp keeps the read drawing keyed by file path).
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using CADTransLite.Core.Models;

namespace CADTransLite.McpServer;

internal sealed class DrawingSession
{
    public string OriginalPath { get; set; } = "";
    public string DxfPath { get; set; } = "";
    public List<TranslationItem> Items { get; set; } = new();
    public bool WasDwg { get; set; }
}

internal static class SessionCache
{
    private static readonly ConcurrentDictionary<string, DrawingSession> Sessions = new();

    public static void Set(string key, DrawingSession s) => Sessions[Path.GetFullPath(key)] = s;
    public static DrawingSession? Get(string key) =>
        Sessions.TryGetValue(Path.GetFullPath(key), out var s) ? s : null;
}

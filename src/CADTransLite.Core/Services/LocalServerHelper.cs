// Services/LocalServerHelper.cs
// Helpers to detect and (optionally) auto-start a bundled local translation server
// (e.g. deeplx_windows_amd64.exe shipped alongside the app) so the user does not
// have to start it manually before translating.
//
// Used by DeepLXTranslator to recover from "service not running" errors.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CADTransLite.Core.Services;

/// <summary>
/// Utility for probing and launching a local translation server process.
/// </summary>
public static class LocalServerHelper
{
    /// <summary>每个已启动子进程最近一条 stderr 内容（PID → 文本），用于在 UI 上直接展示启动失败原因。</summary>
    private static readonly ConcurrentDictionary<int, string> _lastErrors = new();

    /// <summary>本应用会话内启动过的本地服务子进程，退出时统一回收，避免残留进程占用端口。</summary>
    private static readonly object _ownedLock = new();
    private static readonly List<Process> _ownedProcesses = new();

    /// <summary>单个子进程最多写入日志的输出行数，避免长时间运行的服务刷爆日志。</summary>
    private const int MaxLoggedLines = 200;

    /// <summary>
    /// 取得指定子进程最近一条标准错误输出（若有）。进程启动失败时用于给出可读的原因。
    /// </summary>
    public static string? GetLastError(int pid)
        => _lastErrors.TryGetValue(pid, out var msg) ? msg : null;
    /// <summary>
    /// Returns true if a TCP connection to <paramref name="host"/>:<paramref name="port"/> succeeds
    /// within <paramref name="timeoutMs"/>. Uses a real, time-bounded Socket connect so an
    /// unreachable (silently dropped) host cannot hang the call. Treat false as "likely down".
    /// </summary>
    public static bool IsPortOpen(string host, int port, int timeoutMs = 1500)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Blocking = true;
            var ar = socket.BeginConnect(host, port, null, null);
            // WaitOne returns true only if the connect completed within the timeout.
            if (ar.AsyncWaitHandle.WaitOne(timeoutMs))
            {
                try
                {
                    socket.EndConnect(ar);
                    return socket.Connected;
                }
                catch
                {
                    return false;
                }
            }
            // Timed out — connection still pending; don't block waiting for TCP retransmits.
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a base URL like "http://127.0.0.1:1188" into host/port.
    /// Returns false when the URL cannot be parsed.
    /// </summary>
    public static bool TryParseHostPort(string baseUrl, out string host, out int port)
    {
        host = "127.0.0.1";
        port = 1188;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return false;

        try
        {
            var uri = new Uri(baseUrl);
            host = uri.Host;
            if (uri.Port > 0) port = uri.Port;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to launch a bundled server executable. Searches a few common locations relative
    /// to the running application. Returns the started <see cref="Process"/>, or null if the exe
    /// could not be found or launched.
    /// </summary>
    /// <param name="exeName">File name to look for, e.g. "deeplx_windows_amd64.exe".</param>
    public static Process? TryStartBundledServer(string exeName)
        => TryStartBundledServer(exeName, null, null);

    /// <summary>
    /// Attempts to launch a bundled server executable, passing <paramref name="args"/> on the
    /// command line and (optionally) looking inside <paramref name="subDirectory"/> of each
    /// candidate directory. Used for the Argos Translate engine, which launches
    /// <c>python.exe argos_server.py --port 5001</c> from the bundled <c>tools/py</c> folder.
    /// </summary>
    /// <param name="exeName">File name to look for, e.g. "python.exe".</param>
    /// <param name="args">Optional command-line arguments for the executable.</param>
    /// <param name="subDirectory">Optional sub-folder (relative to each candidate dir) to also scan.</param>
    public static Process? TryStartBundledServer(string exeName, string[]? args, string? subDirectory)
    {
        string[] baseDirs =
        {
            AppContext.BaseDirectory,
            AppDomain.CurrentDomain.BaseDirectory,
            // When running from source/bin, the bundled exe sits at the repo root
            // (repo_root/src/CADTransLite.UI/bin/Debug/net9.0 -> 5 levels up).
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..")),
            Directory.GetCurrentDirectory(),
        };

        // Expand the candidate directories with the optional sub-directory.
        var candidateDirs = subDirectory is null
            ? baseDirs
            : baseDirs.Concat(baseDirs.Select(d => Path.Combine(d, subDirectory))).ToArray();

        foreach (var dir in candidateDirs)
        {
            try
            {
                var full = Path.Combine(dir, exeName);
                if (!File.Exists(full))
                    continue;

                var psi = new ProcessStartInfo
                {
                    FileName = full,
                    // 必须设为 false 才能使用 EnvironmentVariables（为 Argos/LibreTranslate 注入
                    // ARGOS_PACKAGES_DIR 等离线环境变量）；同时配合 CreateNoWindow 隐藏控制台窗口。
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // 捕获子进程输出：窗口是隐藏的，若不重定向就完全看不到 Python 的报错，
                    // 导致「服务起不来」时无从排查。
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                // Run from the exe's own directory so relative script/arg paths (e.g. argos_server.py)
                // resolve correctly.
                psi.WorkingDirectory = Path.GetDirectoryName(full) ?? dir;

                // Point Argos Translate / LibreTranslate at the bundled language packs so they
                // load offline and never re-download models at runtime.
                var argosPkgs = Path.Combine(psi.WorkingDirectory, "argos_packages");
                psi.EnvironmentVariables["ARGOS_PACKAGES_DIR"] = argosPkgs;

                // Use the bundled MiniSBD sentence splitter (no Stanza download from HuggingFace)
                // and redirect its ONNX model cache to the bundled dir via XDG_DATA_HOME.
                psi.EnvironmentVariables["ARGOS_CHUNK_TYPE"] = "MINISBD";
                psi.EnvironmentVariables["XDG_DATA_HOME"] = psi.WorkingDirectory;

                // 让 Python 以 UTF-8 且不缓冲地输出，否则重定向后中文乱码、日志也会延迟很久才出现。
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

                if (args is { Length: > 0 })
                    psi.Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

                var proc = Process.Start(psi);
                ErrorLogger.Instance.Info("LocalServer",
                    $"已自动启动本地服务: {full}{(psi.Arguments.Length > 0 ? " " + psi.Arguments : "")} (PID={proc?.Id})");
                if (proc != null)
                {
                    TrackOwnedProcess(proc);
                    AttachOutputLogging(proc, exeName, args);
                }
                return proc;
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Warn("LocalServer",
                    $"尝试启动 {exeName} 于 {dir} 失败: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// 把子进程的标准输出/错误重定向到 <see cref="ErrorLogger"/>，并记录最近一条 stderr 供 UI 展示。
    /// 使用 <see cref="Process.BeginOutputReadLine"/> 异步读取，不会阻塞主线程，也不会因管道缓冲而卡死。
    /// </summary>
    private static void AttachOutputLogging(Process proc, string exeName, string[]? args)
    {
        int loggedStdout = 0;
        int loggedStderr = 0;
        var label = $"{exeName}{(args is { Length: > 0 } ? " " + string.Join(" ", args) : "")}";

        void OnOut(string? line)
        {
            if (line == null) return;
            if (loggedStdout < MaxLoggedLines)
            {
                loggedStdout++;
                ErrorLogger.Instance.Info("LocalServer", $"[{label}] {line}");
            }
        }
        void OnErr(string? line)
        {
            if (line == null) return;
            // 始终保存最近一条 stderr，用于启动失败时向用户展示。
            _lastErrors[proc.Id] = line.Trim();
            if (loggedStderr < MaxLoggedLines)
            {
                loggedStderr++;
                ErrorLogger.Instance.Warn("LocalServer", $"[{label}] {line}");
            }
        }

        proc.OutputDataReceived += (_, e) => OnOut(e.Data);
        proc.ErrorDataReceived  += (_, e) => OnErr(e.Data);
        // 进程退出后保留其最后一条 stderr（调用方正是在探测到退出后才来读原因的），
        // 只在缓存过多时清理最早的条目，避免长时间运行后无限增长。
        proc.Exited += (_, _) =>
        {
            ErrorLogger.Instance.Info("LocalServer", $"[{label}] 进程已退出 (PID={proc.Id}, ExitCode={proc.ExitCode})");
            if (_lastErrors.Count > 32)
            {
                foreach (var key in _lastErrors.Keys.Take(_lastErrors.Count - 16))
                    _lastErrors.TryRemove(key, out _);
            }
        };
        proc.EnableRaisingEvents = true;

        try { proc.BeginOutputReadLine(); } catch { /* 已退出等极端情况忽略 */ }
        try { proc.BeginErrorReadLine(); } catch { /* 已退出等极端情况忽略 */ }
    }

    /// <summary>
    /// 登记一个由本会话启动的子进程，供 <see cref="ShutdownAllOwned"/> 统一回收。
    /// </summary>
    private static void TrackOwnedProcess(Process proc)
    {
        lock (_ownedLock)
            _ownedProcesses.Add(proc);
    }

    /// <summary>
    /// 结束本会话内启动的所有本地服务子进程（无论是否仍在运行），用于应用退出时清理，
    /// 防止遗留进程继续占用端口（例如 5001）导致下次启动的实例连到已失效的旧服务。
    /// </summary>
    public static void ShutdownAllOwned()
    {
        List<Process> snapshot;
        lock (_ownedLock)
        {
            snapshot = _ownedProcesses.ToList();
            _ownedProcesses.Clear();
        }

        foreach (var proc in snapshot)
        {
            try
            {
                if (proc != null && !proc.HasExited)
                {
                    try { proc.Kill(); }
                    catch { /* 已退出等竞争情况忽略 */ }
                }
            }
            catch { /* 忽略 */ }
            finally
            {
                try { proc?.Dispose(); }
                catch { /* 忽略 */ }
            }
        }
    }

    /// <summary>
    /// 探测指定地址是否为一个“健康的本应用翻译服务”。优先使用轻量的 <c>/ready</c> 端点
    /// （Argos 新版支持；返回 <c>{ ready: true }</c> 即视为就绪）；若该端点不可用，则退化为
    /// 一次真实翻译探测（en→zh），非空译文即视为健康。两者皆失败时返回 <c>false</c>，
    /// 调用方可据此判定该端口被非本应用/不健康的进程占用。
    /// </summary>
    public static async Task<bool> IsHealthyServerAsync(string baseUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var root = baseUrl.TrimEnd('/');

            // 1) 优先 /ready 轻量探活
            try
            {
                using var r = await probe.GetAsync($"{root}/ready", cancellationToken).ConfigureAwait(false);
                if (r.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(
                        await r.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (doc.RootElement.TryGetProperty("ready", out var v) && v.ValueKind == JsonValueKind.True)
                        return true;
                }
            }
            catch { /* 旧版无 /ready 或不可达，继续真实探测 */ }

            // 2) 退化：真实翻译探测
            var body = new Dictionary<string, string>
            {
                ["q"] = "hi",
                ["source"] = "en",
                ["target"] = "zh",
                ["format"] = "text"
            };
            using var pr = await probe.PostAsJsonAsync($"{root}/translate", body, JsonSerializerOptions.Web, cancellationToken)
                .ConfigureAwait(false);
            if (pr.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(
                    await pr.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("translatedText", out var t)
                    && !string.IsNullOrWhiteSpace(t.GetString()))
                    return true;
            }
        }
        catch { /* 任意异常均视为不健康 */ }

        return false;
    }
}

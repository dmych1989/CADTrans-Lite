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
    /// candidate directory. Used for the LibreTranslate / NLLB local engines, which launch
    /// <c>python.exe libretranslate_server.py</c> / <c>nllb_server.py</c> from the bundled <c>tools/py</c> folder.
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
                    // 必须设为 false 才能使用 EnvironmentVariables（为 LibreTranslate/NLLB 注入
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
                // Run from the exe's own directory so relative script/arg paths (e.g. libretranslate_server.py)
                // resolve correctly.
                psi.WorkingDirectory = Path.GetDirectoryName(full) ?? dir;

                // Point LibreTranslate / NLLB at the bundled language packs so they
                // load offline and never re-download models at runtime.
                var argosPkgs = Path.Combine(psi.WorkingDirectory, "argos_packages");
                psi.EnvironmentVariables["ARGOS_PACKAGES_DIR"] = argosPkgs;

                // 使用 argostranslate 默认的句子分句器（基于标点，完全离线）。
                // 注：此前注入的 ARGOS_CHUNK_TYPE=MINISBD 需要联网下载 MiniSBD 的
                // 中文/日/韩/俄文 ONNX 模型，离线环境下会导致除 en->zh 外的方向全部
                // 挂起/超时/500。改为去掉该变量；libretranslate_server.py 也会强制清除它，
                // 双重保险确保离线翻译可用。
                psi.EnvironmentVariables["XDG_DATA_HOME"] = psi.WorkingDirectory;

                // 让 Python 以 UTF-8 且不缓冲地输出，否则重定向后中文乱码、日志也会延迟很久才出现。
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                // 强制 Python 解释器以 UTF-8 模式运行（PEP 540）。这对 LibreTranslate 尤为关键：
                // 其 Flask jsonify 在 Windows 非 UTF-8 locale 下会把中文编码成乱码（如“哈啰”→“åå°”），
                // 导致 UI 拿到的是乱码译文而非异常，用户误以为“引擎不能用”。注入此变量后 Flask
                // 与所有 stdlib 文本处理均按 UTF-8 进行，根治本地引擎中文乱码（LibreTranslate/NLLB 一并受益）。
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";

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
    /// 自动定位并后台启动 MCP 桥接服务（CADTransLite.McpBridge.exe），供 UI 一键翻译时“集成启动”，
    /// 无需用户手动运行。依次在「应用同目录」「McpServer 项目的 bin 输出目录」等候选位置寻找可执行文件，
    /// 找到后以隐藏窗口方式拉起（不再登记为“拥有进程”，使其独立于 UI 进程存活——
    /// 这样 AI 客户端（Claude/Cursor）也能持续复用同一桥接，UI 退出也不会误杀它）。
    /// </summary>
    /// <param name="port">桥接监听端口，默认 8090。</param>
    /// <returns>已启动的 <see cref="Process"/>，或 null（未找到/启动失败）。</returns>
    public static Process? TryStartMcpBridge(int port)
    {
        const string exeName = "CADTransLite.McpBridge.exe";
        var baseDir = AppContext.BaseDirectory;

        // 候选目录：同目录优先（打包/便携版），其次 McpServer 工程的 Debug/Release 输出目录。
        var candidates = new[]
        {
            baseDir,
            Path.Combine(baseDir, "..", "CADTransLite.McpServer", "bin", "Debug", "net9.0-windows"),
            Path.Combine(baseDir, "..", "CADTransLite.McpServer", "bin", "Release", "net9.0-windows"),
            Path.Combine(baseDir, "..", "..", "CADTransLite.McpServer", "bin", "Debug", "net9.0-windows"),
            Path.Combine(baseDir, "..", "..", "CADTransLite.McpServer", "bin", "Release", "net9.0-windows"),
            Path.Combine(baseDir, "..", "..", "..", "..", "CADTransLite.McpServer", "bin", "Debug", "net9.0-windows"),
            Path.Combine(baseDir, "..", "..", "..", "..", "CADTransLite.McpServer", "bin", "Release", "net9.0-windows"),
        };

        foreach (var dir in candidates)
        {
            try
            {
                var full = Path.GetFullPath(Path.Combine(dir, exeName));
                if (!File.Exists(full))
                    continue;

                var psi = new ProcessStartInfo
                {
                    FileName = full,
                    Arguments = $"--port={port}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                psi.WorkingDirectory = Path.GetDirectoryName(full) ?? dir;

                var proc = Process.Start(psi);
                ErrorLogger.Instance.Info("McpBridge",
                    $"已集成启动 MCP 桥接: {full} --port={port} (PID={proc?.Id})");
                if (proc != null)
                {
                    // 仅挂日志与“最后一条错误”展示，不登记为拥有进程（UI 退出不杀它）。
                    AttachOutputLogging(proc, exeName, new[] { $"--port={port}" });
                }
                return proc;
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Warn("McpBridge",
                    $"尝试启动 {exeName} 于 {dir} 失败: {ex.Message}");
            }
        }

        return null;
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
    /// （本地引擎支持；返回 <c>{ ready: true }</c> 即视为就绪）；若该端点不可用，则退化为
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

    /// <summary>
    /// 结束任何占用指定端口的进程（不限于本应用会话启动的），用于「暂停服务」按钮
    /// 或「启动/重启服务」时清理掉残留/不健康的旧实例，避免端口被占用却连不上。
    /// 仅支持 Windows（通过 netstat -ano 解析 LISTENING 进程的 PID 后结束）。
    /// </summary>
    public static void StopServerOnPort(string host, int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c netstat -ano | findstr :{port} | findstr LISTENING",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            var output = p?.StandardOutput.ReadToEnd() ?? "";
            try { p?.WaitForExit(2000); } catch { /* 忽略 */ }

            foreach (var raw in output.Split('\n'))
            {
                var parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                // netstat 行格式：协议 本地地址 外部地址 状态 PID
                // 本地地址形如 127.0.0.1:5001（或 [::1]:5001），精确匹配 :port 且状态为 LISTENING。
                if (parts.Length >= 5
                    && parts[1].EndsWith(":" + port)
                    && parts[3] == "LISTENING"
                    && int.TryParse(parts[4], out var pid)
                    && pid > 0)
                {
                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        proc.Kill();
                        ErrorLogger.Instance.Info("LocalServer", $"已停止占用端口 {port} 的进程 (PID={pid})");
                    }
                    catch (Exception ex)
                    {
                        ErrorLogger.Instance.Warn("LocalServer", $"停止占用端口 {port} 的进程 (PID={pid}) 失败: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("LocalServer", $"清理端口 {port} 占用进程时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 决定翻译适配器是否应「启动/重启」一个干净的服务实例，而不是盲目复用现有端口占用者。
    /// 逻辑：
    ///   1) 端口未开放 → 返回 <c>true</c>（需要启动新实例）。
    ///   2) 端口被「本应用会话」启动的进程占用 → 返回 <c>false</c>（直接复用；其首次模型加载
    ///      由真实翻译请求的长超时覆盖，不应杀掉重启，否则会让加载中的实例从头开始、永远就绪不了）。
    ///   3) 端口被「非本会话」进程占用（如上次调试/测试遗留的坏实例、其它会话启动的实例）→
    ///      先清理占用者再返回 <c>true</c>，调用方会拉起一个干净实例。这正是「测试多次仍失败」
    ///      的根因修复：此前只要端口开着就永远复用坏实例，导致 NLLB 500 空译文反复出现。
    /// <para>
    /// 注意：不依赖「健康检查」来判定是否重启，避免误杀「正在冷加载模型」的健康实例
    /// （冷加载期间真实探测会超时，但实例其实可用，杀掉反而让它从头开始永远就绪不了）。
    /// </para>
    /// </summary>
    public static bool ShouldStartFreshServer(string host, int port)
    {
        if (!IsPortOpen(host, port))
            return true;

        if (IsPortOwnedByUs(host, port))
            return false;

        // 非本会话进程占用（很可能是上次调试/测试遗留的坏实例）→ 清理，交回给调用方拉起干净实例。
        ErrorLogger.Instance.Info("LocalServer",
            $"端口 {port} 被非本会话进程占用，将清理后由本应用重启干净实例。");
        StopServerOnPort(host, port);
        return true;
    }

    /// <summary>
    /// 判断占用指定端口的进程是否为本应用会话启动的（在 _ownedProcesses 中）。
    /// 翻译路径据此判断：若端口被“非本会话”的进程占用（如上次调试遗留的坏实例），
    /// 应当先清理再重新拉起干净实例，而不是盲目复用坏服务。
    /// </summary>
    public static bool IsPortOwnedByUs(string host, int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c netstat -ano | findstr :{port} | findstr LISTENING",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            var output = p?.StandardOutput.ReadToEnd() ?? "";
            try { p?.WaitForExit(2000); } catch { /* 忽略 */ }

            foreach (var raw in output.Split('\n'))
            {
                var parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5
                    && parts[1].EndsWith(":" + port)
                    && parts[3] == "LISTENING"
                    && int.TryParse(parts[4], out var pid)
                    && pid > 0)
                {
                    // 只要本会话进程持有该端口（任一监听 PID 属于我们）即视为“我们的服务”。
                    // 不能遇到第一个非本会话 PID 就 return false：并发竞态/端口复用可能让
                    // netstat 同时列出多个进程，误判会把健康的本会话实例连同一起杀掉重启，
                    // 造成 NLLB 服务反复崩溃（ExitCode=-1）从而“测试失败”。
                    foreach (var owned in _ownedProcesses)
                    {
                        try
                        {
                            if (owned != null && !owned.HasExited && owned.Id == pid)
                                return true;
                        }
                        catch { /* 进程已失效 */ }
                    }
                }
            }
        }
        catch { /* 忽略 */ }
        return false;
    }
}

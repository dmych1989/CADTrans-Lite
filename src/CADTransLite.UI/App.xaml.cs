// App.xaml.cs
// Application entry point for CADTrans Lite.

using System.IO;
using System.Windows;
using CADTransLite.Core.Models;
using CADTransLite.Core.Services;

namespace CADTransLite.UI;

/// <summary>
/// Interaction logic for App.xaml.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Handles unhandled exceptions at the application level to prevent silent crashes.
    /// All errors are logged to E:\CADTrans Lite\log\.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Purge logs older than 30 days on startup
        ErrorLogger.Instance.PurgeOldLogs(30);

        ErrorLogger.Instance.Info("App", "═══════════════════════════════════════════");
        ErrorLogger.Instance.Info("App", "CADTrans Lite 启动");
        ErrorLogger.Instance.Info("App", $"日志目录: {ErrorLogger.Instance.LogDir}");

        // UI thread unhandled exceptions
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLogger.Instance.Error("DispatcherUnhandled", args.Exception);
            MessageBox.Show(
                $"未处理的异常：\n{args.Exception.Message}\n\n详细信息已记录到日志。",
                "CADTrans Lite — 严重错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        // Background thread unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                ErrorLogger.Instance.Error("AppDomainUnhandled", ex);
        };

        // Unobserved task exceptions (async)
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            // 多数情况是「进程退出 / Cancellation 传播」时，fire-and-forget 的端口探测 socket
            // 被中止（"由于线程退出或应用程序请求，已中止 I/O 操作" / OperationCanceledException）。
            // 这类是正常副作用而非业务错误，降级为 Debug，避免刷屏误导用户。
            bool benign = args.Exception?.InnerExceptions != null &&
                          args.Exception.InnerExceptions.All(x =>
                              x is OperationCanceledException ||
                              x is System.Net.Sockets.SocketException ||
                              (x is IOException io && (io.Message.Contains("中止") || io.Message.Contains("aborted", StringComparison.OrdinalIgnoreCase))));

            if (benign)
            {
                // 良性异常（进程退出/取消期间的 socket 中止）属预期副作用，
                // 完全不记日志以免刷屏；仅标记已观察防止进程崩溃即可。
            }
            else
                ErrorLogger.Instance.Error("UnobservedTask", args.Exception ?? new Exception("(no exception object)"));

            args.SetObserved();
        };

        // Auto-start bundled local translation servers in the background so they are already
        // running by the time the user clicks "test"/"translate" (prevents connection hangs).
        StartLocalServersOnLaunch();
    }

    /// <summary>
    /// Launches any enabled local translation server executable in the background, without
    /// blocking application startup. DeepLX ships as deeplx_windows_amd64.exe next to the app.
    /// </summary>
    private void StartLocalServersOnLaunch()
    {
        try
        {
            // Make sure the bundled DeepLX exe is present on disk; recover it from the embedded
            // resource if an antivirus (or anything else) deleted it.
            EnsureBundledDeepLX();

            var api = new SettingsManager().Load().TranslationApi;

            if (api.EnableDeepLX)
            {
                Task.Run(() =>
                {
                    var proc = LocalServerHelper.TryStartBundledServer("deeplx_windows_amd64.exe");
                    if (proc is null)
                        ErrorLogger.Instance.Warn("App",
                            "未在本地找到 deeplx_windows_amd64.exe，DeepLX 需手动启动。");
                });
            }

            // 注：本地 Python 引擎（LibreTranslate / NLLB）不在启动时拉起嵌入式 Python 子进程；
            // 本地离线翻译现由「Bergamot (本地)」进程内引擎与按需启动的本地服务共同提供。

        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("App", $"启动本地服务时出错: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // 停止后台端口轮询定时器，避免退出阶段 socket 探测被中止而产生「未观察 Task 异常」噪声。
            if (MainWindow is MainWindow mw && mw.DataContext is MainWindowViewModel vm)
                vm.StopMonitoring();
        }
        catch { /* ignore */ }

        // 关闭软件时回收本会话启动的所有本地翻译引擎子进程（python.exe 等），
        // 避免它们残留后台继续占用 5000/5001/5002 端口，导致下次启动连到失效的旧服务
        // 而「测试失败」。此前 OnExit 漏调此方法，是引擎关不掉的根因。
        try
        {
            LocalServerHelper.ShutdownAllOwned();
        }
        catch { /* ignore */ }

        // 额外清理仍 LISTENING 的历史残留进程（如上次异常退出、未走本会话 ShutdownAllOwned
        // 而遗留的孤儿 python 实例）。本会话的已被上面回收，剩下的监听者即历史残留。
        // 端口优先读取用户自定义设置（LibreTranslate/NLLB 的「API 地址」可能改过端口），
        // 并兜底保留默认 5000/5001/5002，确保「自定义端口」也能在退出时被清理。
        try
        {
            var ports = new HashSet<int> { 5000, 5001, 5002, 1188 };
            try
            {
                var api = new SettingsManager().Load().TranslationApi;
                foreach (var url in new[] { api.LibreTranslateUrl, api.NllbUrl, api.DeepLXUrl })
                {
                    if (LocalServerHelper.TryParseHostPort(url, out _, out var p))
                        ports.Add(p);
                }
            }
            catch
            {
                // 读取失败则只用默认端口兜底。
            }

            foreach (var port in ports)
                LocalServerHelper.StopServerOnPort("127.0.0.1", port);
        }
        catch { /* ignore */ }

        ErrorLogger.Instance.Info("App", "CADTrans Lite 退出");
        ErrorLogger.Instance.Info("App", "═══════════════════════════════════════════");
        base.OnExit(e);
    }

    /// <summary>
    /// Ensures the bundled DeepLX server executable exists on disk. If it cannot be found in any
    /// of the locations <see cref="LocalServerHelper"/> scans, the bytes are recovered from the
    /// embedded resource (deeplx_backup.bin) inside this assembly and written back out, so DeepLX
    /// can still auto-start even after an antivirus removes the on-disk copy.
    /// </summary>
    private static void EnsureBundledDeepLX()
        => EnsureBundledExe("deeplx_windows_amd64.exe", "deeplx_backup");

    /// <summary>
    /// Generic self-healing recovery: if <paramref name="exeName"/> is not present in any of the
    /// locations LocalServerHelper scans, restore its bytes from an embedded resource whose name
    /// contains <paramref name="resourceSubstring"/>. Non-fatal — only logs if the backup is absent.
    /// </summary>
    private static void EnsureBundledExe(string exeName, string resourceSubstring)
    {
        string[] candidateDirs =
        {
            AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..")),
            Directory.GetCurrentDirectory(),
        };

        foreach (var dir in candidateDirs)
        {
            try { if (File.Exists(Path.Combine(dir, exeName))) return; } catch { /* ignore */ }
        }

        // Not found anywhere -> recover from the embedded resource.
        try
        {
            // Locate the backup resource by name (avoids hard-coding the exact manifest name).
            var resName = typeof(App).Assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.Contains(resourceSubstring, StringComparison.OrdinalIgnoreCase));
            if (resName is null)
            {
                ErrorLogger.Instance.Warn("App", $"内嵌资源中未找到 {exeName} 备份，无法自恢复。");
                return;
            }

            using var stream = typeof(App).Assembly.GetManifestResourceStream(resName);
            if (stream is null)
            {
                ErrorLogger.Instance.Warn("App", $"内嵌资源中未找到 {exeName} 备份，无法自恢复。");
                return;
            }

            // Prefer the repo root (5 levels up, where the build expects it), then fall back to the
            // app's own directory which LocalServerHelper always scans.
            var targets = new[]
            {
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", exeName)),
                Path.Combine(AppContext.BaseDirectory, exeName),
            };

            foreach (var target in targets)
            {
                try
                {
                    var dir = Path.GetDirectoryName(target)!;
                    if (!Directory.Exists(dir)) continue;
                    using var fs = File.Create(target);
                    stream.CopyTo(fs);
                    ErrorLogger.Instance.Info("App", $"已从内嵌资源恢复 {exeName}: {target}");
                    return;
                }
                catch (Exception ex)
                {
                    ErrorLogger.Instance.Warn("App", $"恢复 {exeName} 到 {target} 失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("App", $"内嵌资源恢复 {exeName} 失败: {ex.Message}");
        }
    }
}

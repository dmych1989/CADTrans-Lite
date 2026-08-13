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
            ErrorLogger.Instance.Error("UnobservedTask", args.Exception);
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

            // LibreTranslate: 内置嵌入式 Python，自动以模块方式拉起 libretranslate 服务 (默认端口 5000)。
            if (api.EnableLibreTranslate)
            {
                Task.Run(() =>
                {
                    var proc = LocalServerHelper.TryStartBundledServer(
                        "python.exe",
                        new[] { "-m", "libretranslate", "--host", "127.0.0.1", "--port", "5000" },
                        "tools/py");
                    if (proc is null)
                        ErrorLogger.Instance.Warn("App",
                            "未在 tools/py 找到 python.exe，LibreTranslate 需先运行 setup_engines.ps1 安装。");
                });
            }

            // Argos Translate: 内置 tools/py/python.exe，自动拉起 python 本地服务 (默认端口 5001)。
            if (api.EnableArgos)
            {
                Task.Run(() =>
                {
                    var proc = LocalServerHelper.TryStartBundledServer(
                        "python.exe",
                        new[] { "argos_server.py", "--port", "5001" },
                        "tools/py");
                    if (proc is null)
                        ErrorLogger.Instance.Warn("App",
                            "未在 tools/py 找到 python.exe，Argos 需先运行 setup_engines.ps1 安装。");
                });
            }


        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Warn("App", $"启动本地服务时出错: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
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

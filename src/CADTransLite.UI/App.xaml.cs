// App.xaml.cs
// Application entry point for CADTrans Lite.

using System.Windows;
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ErrorLogger.Instance.Info("App", "CADTrans Lite 退出");
        ErrorLogger.Instance.Info("App", "═══════════════════════════════════════════");
        base.OnExit(e);
    }
}

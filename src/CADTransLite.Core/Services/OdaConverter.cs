// Services/OdaConverter.cs
// Wraps the ODA File Converter CLI for DWG↔DXF conversion.
// v1.1 - Added debug logging to %TEMP%\CADTransLite_OdaDebug.log

using CADTransLite.Core.Models;
using System.Text;

namespace CADTransLite.Core.Services;

/// <summary>
/// Converts between DWG and DXF formats using the ODA File Converter CLI.
/// If ODA is not installed, conversion methods throw <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class OdaConverter
{
    private readonly OdaSettings _settings;
    // 使用相对路径，日志文件位于应用程序目录下的 log 文件夹
    private static readonly string LogPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log", "CADTransLite_OdaDebug.log");

    // 仅追加日志（线程安全写入）— 同时写入统一日志
    private static void Log(string message)
    {
        try
        {
            // 确保日志目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, line, Encoding.UTF8);
        }
        catch { /* best-effort */ }

        // 同步到统一运行日志
        if (message.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("no matched files", StringComparison.OrdinalIgnoreCase))
        {
            ErrorLogger.Instance.Error("ODA", message);
        }
        else if (message.StartsWith("===", StringComparison.OrdinalIgnoreCase)
                 || message.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            ErrorLogger.Instance.Info("ODA", message);
        }
    }

    /// <summary>
    /// Creates a new OdaConverter with default settings.
    /// </summary>
    public OdaConverter()
        : this(new OdaSettings())
    {
    }

    /// <summary>
    /// Creates a new OdaConverter with the specified settings.
    /// </summary>
    /// <param name="settings">ODA configuration settings.</param>
    public OdaConverter(OdaSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>Whether ODA File Converter is available on this system.</summary>
    public bool IsAvailable => _settings.IsAvailable;

    /// <summary>
    /// Gets or sets the path to the ODA File Converter executable.
    /// Updating this value immediately affects subsequent conversions.
    /// </summary>
    public string ExecutablePath
    {
        get => _settings.ExecutablePath;
        set => _settings.ExecutablePath = value;
    }

    /// <summary>
    /// Converts a DWG file to a DXF file using ODA File Converter.
    /// </summary>
    public async Task<string> DwgToDxfAsync(
        string dwgPath,
        string outputDir,
        CancellationToken cancellationToken = default)
    {
        Log($"=== DwgToDxfAsync START ===");
        Log($"Source DWG: {dwgPath}");
        Log($"Output dir: {outputDir}");
        Log($"ODA available: {_settings.IsAvailable}");
        Log($"ODA executable: {_settings.ExecutablePath}");
        Log($"AcadVersion: {_settings.AcadVersion}");

        if (!_settings.IsAvailable)
            throw new InvalidOperationException(
                "ODA File Converter 未安装或路径不正确。请安装 ODA File Converter 或仅使用 .dxf 文件。");

        if (!File.Exists(dwgPath))
        {
            Log($"ERROR: Source DWG not found: {dwgPath}");
            throw new FileNotFoundException($"找不到 DWG 文件：{dwgPath}", dwgPath);
        }

        Log($"Source DWG exists, size: {new FileInfo(dwgPath).Length} bytes");

        Directory.CreateDirectory(outputDir);
        Log($"Output dir created/confirmed: {outputDir}");

        string tempInputDir = Path.Combine(Path.GetTempPath(), $"CADTrans_Input_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempInputDir);
        Log($"Temp input dir: {tempInputDir}");

        const string safeFileName = "input.dwg";
        string tempDwgCopy = Path.Combine(tempInputDir, safeFileName);
        File.Copy(dwgPath, tempDwgCopy, overwrite: true);
        Log($"File copied to: {tempDwgCopy}");

        // 验证复制是否成功
        if (!File.Exists(tempDwgCopy))
        {
            Log($"ERROR: Copied file does NOT exist at: {tempDwgCopy}");
            throw new InvalidOperationException($"无法复制 DWG 文件到临时目录: {tempDwgCopy}");
        }
        var copiedInfo = new FileInfo(tempDwgCopy);
        Log($"Copied file verified: {copiedInfo.Length} bytes");

        // 列出输入目录内容
        var inputFiles = Directory.GetFiles(tempInputDir);
        Log($"Temp input dir contains {inputFiles.Length} file(s):");
        foreach (var f in inputFiles) Log($"  {f} ({new FileInfo(f).Length} bytes)");

        try
        {
            // ODA CLI: ODAFileConverter.exe "<input_dir>" "<output_dir>" <version> <format> <recurse> <audit> <filter>
            // 参数说明: recurse=0不递归, audit=0不审计, filter="*.dwg;*.dxf"
            string arguments = $"\"{tempInputDir}\" \"{outputDir}\" {_settings.AcadVersion} DXF 0 0 \"*.dwg;*.dxf\"";
            Log($"ODA arguments: {arguments}");

            await RunOdaAsync(arguments, cancellationToken);

            // ODA outputs using the safe filename with .dxf extension.
            string outputDxf = Path.Combine(outputDir,
                Path.GetFileNameWithoutExtension(safeFileName) + ".dxf");

            Log($"Expected output DXF: {outputDxf}");

            if (!File.Exists(outputDxf))
            {
                // 列出输出目录内容帮助调试
                var outFiles = Directory.Exists(outputDir) ? Directory.GetFiles(outputDir) : Array.Empty<string>();
                Log($"ERROR: Output DXF not found! Output dir contains {outFiles.Length} file(s):");
                foreach (var f in outFiles) Log($"  {f} ({new FileInfo(f).Length} bytes)");

                throw new InvalidOperationException(
                    $"ODA conversion completed but output DXF not found at: {outputDxf}");
            }

            Log($"Output DXF exists: {new FileInfo(outputDxf).Length} bytes");
            Log($"=== DwgToDxfAsync SUCCESS ===");
            return outputDxf;
        }
        finally
        {
            try { if (Directory.Exists(tempInputDir)) Directory.Delete(tempInputDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Converts a DXF file back to DWG using ODA File Converter.
    /// </summary>
    public async Task<string> DxfToDwgAsync(
        string dxfPath,
        string outputDir,
        string? outputVersion = null,
        CancellationToken cancellationToken = default)
    {
        Log($"=== DxfToDwgAsync START ===");
        Log($"Source DXF: {dxfPath}");
        Log($"Output dir: {outputDir}");

        if (!_settings.IsAvailable)
            throw new InvalidOperationException("ODA File Converter 未安装或路径不正确。");

        if (!File.Exists(dxfPath))
        {
            Log($"ERROR: Source DXF not found: {dxfPath}");
            throw new FileNotFoundException($"找不到 DXF 文件：{dxfPath}", dxfPath);
        }

        Directory.CreateDirectory(outputDir);
        string tempInputDir = Path.Combine(Path.GetTempPath(), $"CADTrans_DxfIn_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempInputDir);

        const string safeFileName = "input.dxf";
        string tempDxfCopy = Path.Combine(tempInputDir, safeFileName);
        File.Copy(dxfPath, tempDxfCopy, overwrite: true);

        try
        {
            // ODA CLI: ODAFileConverter.exe "<input_dir>" "<output_dir>" <version> <format> <recurse> <audit> <filter>
            string version = !string.IsNullOrWhiteSpace(outputVersion) ? outputVersion : _settings.AcadVersion;
            string arguments = $"\"{tempInputDir}\" \"{outputDir}\" {version} DWG 0 0 \"*.dwg;*.dxf\"";
            Log($"ODA arguments: {arguments}");
            Log($"Output version: {version}");
            await RunOdaAsync(arguments, cancellationToken);

            string outputDwg = Path.Combine(outputDir,
                Path.GetFileNameWithoutExtension(safeFileName) + ".dwg");

            if (!File.Exists(outputDwg))
                throw new InvalidOperationException($"ODA 转换完成，但在以下路径未找到输出 DWG：{outputDwg}");

            Log($"=== DxfToDwgAsync SUCCESS ===");
            return outputDwg;
        }
        finally
        {
            try { if (Directory.Exists(tempInputDir)) Directory.Delete(tempInputDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Runs the ODA File Converter process and waits for it to exit.
    /// Captures stdout/stderr and detects "no matched files" errors.
    /// </summary>
    private async Task RunOdaAsync(string arguments, CancellationToken cancellationToken = default)
    {
        Log($"RunOdaAsync: starting process...");
        Log($"  FileName: {_settings.ExecutablePath}");
        Log($"  Arguments: {arguments}");

        if (!File.Exists(_settings.ExecutablePath))
        {
            Log($"ERROR: ODA executable NOT found at: {_settings.ExecutablePath}");
            throw new InvalidOperationException($"ODA 可执行文件不存在: {_settings.ExecutablePath}");
        }

        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = _settings.ExecutablePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                stdoutBuilder.AppendLine(e.Data);
                Log($"  [ODA stdout] {e.Data}");
            }
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                stderrBuilder.AppendLine(e.Data);
                Log($"  [ODA stderr] {e.Data}");
            }
        };

        process.Start();
        Log($"  Process started, PID: {process.Id}");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);
        Log($"  Process exited, ExitCode: {process.ExitCode}");

        string stdout = stdoutBuilder.ToString().Trim();
        string stderr = stderrBuilder.ToString().Trim();

        if (!string.IsNullOrEmpty(stdout)) Log($"  Captured stdout ({stdout.Length} chars): {stdout[..Math.Min(500, stdout.Length)]}");
        if (!string.IsNullOrEmpty(stderr)) Log($"  Captured stderr ({stderr.Length} chars): {stderr[..Math.Min(500, stderr.Length)]}");

        bool noMatch = stdout.Contains("no matched files", StringComparison.OrdinalIgnoreCase)
                       || stderr.Contains("no matched files", StringComparison.OrdinalIgnoreCase);

        if (noMatch)
        {
            Log($"ERROR: ODA reported 'no matched files'");
            throw new InvalidOperationException(
                $"ODA File Converter 未在输入文件夹中找到匹配的文件。\nODA 输出: {stdout}\n{stderr}");
        }

        if (process.ExitCode != 0)
        {
            Log($"ERROR: ODA exited with code {process.ExitCode}");
            string hint = process.ExitCode == 1
                ? "（可能原因：DWG 文件损坏、ODA 版本不兼容、文件被其他程序占用、或输出目录权限不足）"
                : "";
            throw new InvalidOperationException(
                $"ODA File Converter 失败（退出码 {process.ExitCode}）{hint}:\n{stdout}\n{stderr}");
        }

        Log($"RunOdaAsync: success");
    }
}

// Models/OdaSettings.cs
// Configuration for the ODA File Converter executable.

namespace CADTransLite.Core.Models;

/// <summary>
/// ODA File Converter configuration. The executable path is resolved automatically on
/// construction: bundled copy (tools/ODAFileConverter) -> app directory -> installed copies
/// under Program Files -> developer-machine default path (fallback).
/// </summary>
public sealed class OdaSettings
{
    /// <summary>Developer-machine default path (fallback candidate only).</summary>
    public const string DefaultExecutablePath =
        @"D:\Program Files\ODA\ODAFileConverter 24.11.0\ODAFileConverter.exe";

    private const string ExeName = "ODAFileConverter.exe";

    /// <summary>Path to the ODA File Converter executable.</summary>
    public string ExecutablePath { get; set; }

    public OdaSettings()
    {
        ExecutablePath = ResolveExecutablePath();
    }

    /// <summary>AutoCAD version to target for conversion.</summary>
    public string AcadVersion { get; set; } = "ACAD2018";

    /// <summary>Output AutoCAD version for DXF to DWG conversion. Default "ACAD2018".</summary>
    public string OutputAcadVersion { get; set; } = "ACAD2018";

    /// <summary>Whether the ODA File Converter is available on this system.</summary>
    public bool IsAvailable => File.Exists(ExecutablePath);

    /// <summary>
    /// Resolves the ODA File Converter executable. Returns the first existing candidate:
    /// 1) bundled tools/ODAFileConverter next to the app exe (portable integration);
    /// 2) ODAFileConverter.exe next to the app exe;
    /// 3) any installed version under Program Files (ODA\ODAFileConverter *);
    /// 4) the developer-machine default path (fallback, usually missing).
    /// </summary>
    public static string ResolveExecutablePath()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "ODAFileConverter", ExeName),
            Path.Combine(AppContext.BaseDirectory, ExeName),
        };

        foreach (var root in new[] { @"C:\Program Files\ODA", @"C:\Program Files (x86)\ODA", @"D:\Program Files\ODA" })
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var dir in Directory.GetDirectories(root, "ODAFileConverter *"))
                    candidates.Add(Path.Combine(dir, ExeName));
            }
            catch { /* ignore */ }
        }

        candidates.Add(DefaultExecutablePath);
        return candidates.FirstOrDefault(File.Exists) ?? DefaultExecutablePath;
    }
}

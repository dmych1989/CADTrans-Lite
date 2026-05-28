// Models/OdaSettings.cs
// Configuration for the ODA File Converter executable.

namespace CADTransLite.Core.Models;

/// <summary>
/// ODA File Converter configuration.
/// </summary>
public sealed class OdaSettings
{
    /// <summary>Default path to the ODA File Converter executable (v24.11.0).</summary>
    public const string DefaultExecutablePath =
        @"D:\Program Files\ODA\ODAFileConverter 24.11.0\ODAFileConverter.exe";

    /// <summary>Path to the ODA File Converter executable.</summary>
    public string ExecutablePath { get; set; } = DefaultExecutablePath;

    /// <summary>AutoCAD version to target for conversion.</summary>
    public string AcadVersion { get; set; } = "ACAD2018";

    /// <summary>DXF→DWG 转换时使用的输出 AutoCAD 版本。默认 "ACAD2018"。</summary>
    public string OutputAcadVersion { get; set; } = "ACAD2018";

    /// <summary>Whether the ODA File Converter is available on this system.</summary>
    public bool IsAvailable => File.Exists(ExecutablePath);
}

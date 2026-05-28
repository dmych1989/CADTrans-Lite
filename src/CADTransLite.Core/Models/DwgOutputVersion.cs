namespace CADTransLite.Core.Models;

/// <summary>
/// DWG 输出版本选项定义。
/// </summary>
public sealed class DwgOutputVersion
{
    /// <summary>AutoCAD 版本代码（如 ACAD2018）。</summary>
    public string VersionCode { get; set; } = "ACAD2018";

    /// <summary>显示名称（如 "AutoCAD 2018 DWG"）。</summary>
    public string DisplayName { get; set; } = "AutoCAD 2018 DWG";

    /// <summary>获取所有支持的 DWG 输出版本。</summary>
    public static List<DwgOutputVersion> GetAllVersions() => new()
    {
        new() { VersionCode = "ACAD2018", DisplayName = "AutoCAD 2018 DWG" },
        new() { VersionCode = "ACAD2013", DisplayName = "AutoCAD 2013 DWG" },
        new() { VersionCode = "ACAD2010", DisplayName = "AutoCAD 2010 DWG" },
        new() { VersionCode = "ACAD2007", DisplayName = "AutoCAD 2007 DWG" },
        new() { VersionCode = "ACAD2004", DisplayName = "AutoCAD 2004 DWG" },
        new() { VersionCode = "ACAD2000", DisplayName = "AutoCAD 2000 DWG" },
    };

    /// <summary>获取默认版本（ACAD2018）。</summary>
    public static DwgOutputVersion GetDefault() => new() { VersionCode = "ACAD2018", DisplayName = "AutoCAD 2018 DWG" };

    public override string ToString() => DisplayName;
}

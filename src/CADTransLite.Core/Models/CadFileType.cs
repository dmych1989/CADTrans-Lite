// Models/CadFileType.cs
// Enum representing the type of CAD file being processed.

namespace CADTransLite.Core.Models;

/// <summary>
/// Represents the file type of the source CAD document.
/// </summary>
public enum CadFileType
{
    /// <summary>AutoCAD Drawing (.dwg) — requires ODA File Converter for extraction.</summary>
    DWG,

    /// <summary>AutoCAD Drawing Exchange Format (.dxf) — natively supported by netDxf.</summary>
    DXF,
}

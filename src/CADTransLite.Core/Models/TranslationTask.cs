// Models/TranslationTask.cs
// Encapsulates the full translation workflow state for a single CAD file.

namespace CADTransLite.Core.Models;

/// <summary>
/// Represents the current phase of a translation task.
/// </summary>
public enum TranslationTaskPhase
{
    /// <summary>No file loaded yet.</summary>
    None,

    /// <summary>Text has been extracted and exported to Excel.</summary>
    Extracted,

    /// <summary>Translations have been applied (via API or manual editing).</summary>
    Translated,

    /// <summary>Translated text has been imported back and written to a new CAD file.</summary>
    Imported,
}

/// <summary>
/// Encapsulates the complete state of a CAD translation task, from file selection
/// through extraction, translation, and final write-back.
/// </summary>
public sealed class TranslationTask
{
    /// <summary>Absolute path of the source CAD file (.dwg or .dxf).</summary>
    public string SourceFilePath { get; set; } = string.Empty;

    /// <summary>File type of the source document.</summary>
    public CadFileType FileType { get; set; }

    /// <summary>
    /// Path to the temporary DXF file created by ODA File Converter when the
    /// source is a .dwg file.  Null when the source is already .dxf.
    /// </summary>
    public string? TempDxfPath { get; set; }

    /// <summary>ISO 639-1 code of the source language (e.g., "ZH").</summary>
    public string SourceLanguage { get; set; } = "ZH";

    /// <summary>ISO 639-1 code of the target language (e.g., "EN").</summary>
    public string TargetLanguage { get; set; } = "EN";

    /// <summary>Merged translation items (after <see cref="TranslationMerger"/> runs).</summary>
    public List<TranslationItem> Items { get; set; } = new();

    /// <summary>Number of raw items before merging.</summary>
    public int RawItemCount { get; set; }

    /// <summary>Number of items after merging (convenience accessor).</summary>
    public int MergedItemCount => Items.Count;

    /// <summary>Path to the Excel translation file.</summary>
    public string? ExcelPath { get; set; }

    /// <summary>Current workflow phase.</summary>
    public TranslationTaskPhase Phase { get; set; }

    /// <summary>Whether the task uses ODA File Converter (true for .dwg files).</summary>
    public bool UsesOda => FileType == CadFileType.DWG;

    /// <summary>
    /// Path to use for DXF operations — the temp DXF for DWG sources,
    /// or the original path for DXF sources.
    /// </summary>
    public string EffectiveDxfPath => TempDxfPath ?? SourceFilePath;

    /// <inheritdoc/>
    public override string ToString() =>
        $"[{FileType}] {Path.GetFileName(SourceFilePath)} — Phase={Phase} Items={MergedItemCount}";
}

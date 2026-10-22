// Services/ModelLocations.cs
// Resolves where the local translation engines' model files actually live.
//
// Default (no custom directory set): models live under the app's bundled `tools` directory:
//   tools/bergamot
//   tools/py/argos_packages   (LibreTranslate / Argos)
//   tools/py/models/nllb-200-distilled-600M
//
// When the user sets a custom "本地模型根目录" (LocalModelRoot) in settings, the model data is
// loaded from that single directory using a flattened, channel-oriented layout:
//   <root>/bergamot
//   <root>/argos_packages
//   <root>/models/nllb-200-distilled-600M
//
// The Python runtime (tools/py) always stays with the app — only the (large) model *data* is
// relocatable. Both the UI and the out-of-process MCP bridge read this via SettingsManager, so a
// setting made in the UI is honored by the bridge as well.

using System;
using System.IO;

namespace CADTransLite.Core.Services;

/// <summary>
/// 解析本地翻译引擎模型文件的实际位置。读取 <see cref="UserSettings.LocalModelRoot"/>，
/// 在未设置时回退到程序自带的 tools 目录。
/// </summary>
public static class ModelLocations
{
    /// <summary>程序自带的 tools 目录（含 Python 运行时等引擎代码，不随模型迁移）。</summary>
    public static string ToolsDir => Path.Combine(AppContext.BaseDirectory, "tools");

    /// <summary>用户设置的自定义模型根目录；为空字符串表示使用默认 tools 目录。</summary>
    public static string CustomModelRoot()
    {
        try
        {
            var root = new SettingsManager().Load().LocalModelRoot;
            return string.IsNullOrWhiteSpace(root) ? string.Empty : root.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Bergamot 模型目录。</summary>
    public static string BergamotModelsDir()
    {
        var root = CustomModelRoot();
        return string.IsNullOrEmpty(root)
            ? Path.Combine(ToolsDir, "bergamot")
            : Path.Combine(root, "bergamot");
    }

    /// <summary>LibreTranslate / Argos 离线语言包目录（ARGOS_PACKAGES_DIR）。</summary>
    public static string ArgosPackagesDir()
    {
        var root = CustomModelRoot();
        return string.IsNullOrEmpty(root)
            ? Path.Combine(ToolsDir, "py", "argos_packages")
            : Path.Combine(root, "argos_packages");
    }

    /// <summary>NLLB 模型目录（NLLB_MODEL）。</summary>
    public static string NllbModelDir()
    {
        var root = CustomModelRoot();
        return string.IsNullOrEmpty(root)
            ? Path.Combine(ToolsDir, "py", "models", "nllb-200-distilled-600M")
            : Path.Combine(root, "models", "nllb-200-distilled-600M");
    }
}

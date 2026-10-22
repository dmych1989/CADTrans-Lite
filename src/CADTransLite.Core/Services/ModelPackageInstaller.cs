// Services/ModelPackageInstaller.cs
// Installs the distributed model packages into the local model directories.
//
// Supported packages (identified by file name, content used as fallback):
//   bergamot-models.zip        -> <bergamotDir>            (zip top-level: bergamot/)
//   nllb-model.zip             -> <nllbBase>\models\...     (zip top-level: models/nllb-200-distilled-600M/)
//   translate-*.zip            -> <argosDir>\<pair>\        (zip -> .argosmodel -> model/stanza/metadata.json)
//
// Target dirs follow the "本地模型根目录" setting: when a custom root is given the packages go to
// <root>\bergamot, <root>\models, <root>\argos_packages; otherwise into the bundled tools layout.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CADTransLite.Core.Services;

public static class ModelPackageInstaller
{
    public enum PackageKind { Unknown, Bergamot, Nllb, ArgosPair }

    /// <summary>解析安装目标目录（bergamotDir / nllb 基目录 / argos_packages）。</summary>
    public static (string BergamotDir, string NllbBaseDir, string ArgosDir) ResolveTargets(string? customRoot)
    {
        if (!string.IsNullOrWhiteSpace(customRoot))
        {
            var root = customRoot.Trim();
            return (
                Path.Combine(root, "bergamot"),
                root,                                              // nllb zip 顶层自带 models\
                Path.Combine(root, "argos_packages"));
        }
        return (
            Path.Combine(ModelLocations.ToolsDir, "bergamot"),
            Path.Combine(ModelLocations.ToolsDir, "py"),           // 同上
            Path.Combine(ModelLocations.ToolsDir, "py", "argos_packages"));
    }

    /// <summary>按文件名分类压缩包；无法识别时返回 Unknown（由调用方按内容兜底）。</summary>
    public static PackageKind Classify(string zipPath)
    {
        var name = Path.GetFileName(zipPath) ?? string.Empty;
        if (name.Contains("bergamot", StringComparison.OrdinalIgnoreCase)) return PackageKind.Bergamot;
        if (name.Contains("nllb", StringComparison.OrdinalIgnoreCase)) return PackageKind.Nllb;
        if (name.StartsWith("translate-", StringComparison.OrdinalIgnoreCase)) return PackageKind.ArgosPair;
        return PackageKind.Unknown;
    }

    /// <summary>安装一批模型压缩包。进度通过 <paramref name="progress"/> 汇报。</summary>
    public static Task InstallAsync(IEnumerable<string> zipPaths, string? customRoot,
        IProgress<string> progress, CancellationToken ct = default)
        => Task.Run(() => InstallCore(zipPaths, customRoot, progress, ct), ct);

    private static void InstallCore(IEnumerable<string> zipPaths, string? customRoot,
        IProgress<string> progress, CancellationToken ct)
    {
        var (bergamotDir, nllbBase, argosDir) = ResolveTargets(customRoot);
        var results = new List<string>();

        foreach (var zip in zipPaths)
        {
            ct.ThrowIfCancellationRequested();
            var kind = Classify(zip);
            progress.Report($"正在安装：{Path.GetFileName(zip)} …");
            try
            {
                switch (kind)
                {
                    case PackageKind.Bergamot:
                        InstallBergamot(zip, bergamotDir, progress);
                        results.Add($"✅ Bergamot → {bergamotDir}");
                        break;
                    case PackageKind.Nllb:
                        InstallNllb(zip, nllbBase, progress);
                        results.Add($"✅ NLLB → {Path.Combine(nllbBase, "models")}");
                        break;
                    case PackageKind.ArgosPair:
                        results.Add("✅ " + InstallArgos(zip, argosDir));
                        break;
                    default:
                        results.Add($"⚠️ 无法识别的压缩包（跳过）：{Path.GetFileName(zip)}");
                        break;
                }
            }
            catch (Exception ex)
            {
                results.Add($"❌ {Path.GetFileName(zip)}：{ex.Message}");
            }
        }

        progress.Report("模型包安装完成。\n" + string.Join("\n", results) +
                        "\n提示：如翻译服务此前已启动，建议重启软件后重新测试。");
    }

    // ── Bergamot：zip 顶层应为 bergamot/，合并到 bergamotDir 所在父目录 ──
    private static void InstallBergamot(string zip, string bergamotDir, IProgress<string> progress)
    {
        var parent = Directory.GetParent(bergamotDir)?.FullName
                     ?? throw new InvalidOperationException("无效的 Bergamot 目标目录。");
        using var stage = new TempDir();
        ZipFile.ExtractToDirectory(zip, stage.Path);

        var top = Directory.EnumerateFileSystemEntries(stage.Path).ToList();
        // 顶层单目录且名为 bergamot -> 解到父目录；否则直接把内容并入 bergamotDir
        if (top.Count == 1 && Directory.Exists(top[0]) &&
            string.Equals(Path.GetFileName(top[0]), "bergamot", StringComparison.OrdinalIgnoreCase))
        {
            MergeCopy(top[0], bergamotDir);
        }
        else
        {
            MergeCopy(stage.Path, bergamotDir);
        }
        progress.Report("Bergamot 模型已就位。");
    }

    // ── NLLB：zip 顶层应为 models/nllb-200-distilled-600M/，合并到 nllbBase 下 ──
    private static void InstallNllb(string zip, string nllbBase, IProgress<string> progress)
    {
        using var stage = new TempDir();
        ZipFile.ExtractToDirectory(zip, stage.Path);

        var top = Directory.EnumerateFileSystemEntries(stage.Path).ToList();
        if (top.Count == 1 && Directory.Exists(top[0]) &&
            string.Equals(Path.GetFileName(top[0]), "models", StringComparison.OrdinalIgnoreCase))
        {
            MergeCopy(top[0], Path.Combine(nllbBase, "models"));
        }
        else
        {
            // 顶层直接是 nllb-200-distilled-600M 之类
            MergeCopy(stage.Path, Path.Combine(nllbBase, "models"));
        }
        progress.Report("NLLB 模型已就位。");
    }

    // ── Argos 语言对：zip -> .argosmodel -> 包目录 ──
    private static string InstallArgos(string zip, string argosDir)
    {
        using var stage = new TempDir();
        ZipFile.ExtractToDirectory(zip, stage.Path);

        var am = Directory.EnumerateFiles(stage.Path, "*.argosmodel", SearchOption.AllDirectories).FirstOrDefault()
                 ?? throw new InvalidOperationException("压缩包内未找到 .argosmodel（不是 Argos 语言包）。");

        var stage2 = Path.Combine(stage.Path, "stage2");
        Directory.CreateDirectory(stage2);
        ZipFile.ExtractToDirectory(am, stage2);

        // .argosmodel 内部常带一层同名顶层目录，若如此则取该目录为包根
        var top = Directory.EnumerateFileSystemEntries(stage2).ToList();
        var pkgRoot = stage2;
        if (top.Count == 1 && Directory.Exists(top[0])) pkgRoot = top[0];

        var dirName = Path.GetFileName(pkgRoot.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(dirName) || pkgRoot == stage2)
            dirName = Path.GetFileNameWithoutExtension(am).Replace('.', '_');

        var dest = Path.Combine(argosDir, dirName);
        MergeCopy(pkgRoot, dest);

        if (!File.Exists(Path.Combine(dest, "metadata.json")))
            throw new InvalidOperationException("安装后缺少 metadata.json，语言包可能不完整。");
        return $"Argos 语言对 {dirName} → {dest}";
    }

    /// <summary>递归合并复制（目标已存在则覆盖合并），兼容 zip 重复安装/升级。</summary>
    private static void MergeCopy(string srcDir, string dstDir)
    {
        Directory.CreateDirectory(dstDir);
        foreach (var file in Directory.EnumerateFiles(srcDir))
        {
            var target = Path.Combine(dstDir, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
        }
        foreach (var dir in Directory.EnumerateDirectories(srcDir))
        {
            MergeCopy(dir, Path.Combine(dstDir, Path.GetFileName(dir)));
        }
    }

    /// <summary>临时目录助手：Dispose 时删除。</summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cadtrans_model_" + Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); } catch { /* ignore */ }
        }
    }
}

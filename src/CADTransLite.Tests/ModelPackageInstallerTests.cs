// ModelPackageInstallerTests.cs
// Functional tests for ModelPackageInstaller using synthetic packages.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using CADTransLite.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class ModelPackageInstallerTests
{
    [TestMethod]
    public void Classify_ByFileName_ReturnsExpectedKinds()
    {
        Assert.AreEqual(ModelPackageInstaller.PackageKind.Bergamot, ModelPackageInstaller.Classify("x/bergamot-models.zip"));
        Assert.AreEqual(ModelPackageInstaller.PackageKind.Nllb, ModelPackageInstaller.Classify("x/nllb-model.zip"));
        Assert.AreEqual(ModelPackageInstaller.PackageKind.ArgosPair, ModelPackageInstaller.Classify("x/translate-en_zh-1.9.zip"));
        Assert.AreEqual(ModelPackageInstaller.PackageKind.Unknown, ModelPackageInstaller.Classify("x/something.zip"));
    }

    [TestMethod]
    public async Task Install_AllThreePackages_InstallsIntoCustomRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "mpi_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zips = Path.Combine(root, "zips");
            Directory.CreateDirectory(zips);

            var berg = CreateZip(Path.Combine(zips, "bergamot-models.zip"), new Dictionary<string, string>
            {
                ["bergamot/en-zh/model.bin"] = "b",
                ["bergamot/en-zh/config.txt"] = "c",
            });

            var nllb = CreateZip(Path.Combine(zips, "nllb-model.zip"), new Dictionary<string, string>
            {
                ["models/nllb-200-distilled-600M/config.txt"] = "n",
            });

            // Argos 双层：zip -> .argosmodel（内含同名顶层目录）-> 包内容
            var inner = Path.Combine(zips, "inner.argosmodel");
            CreateZip(inner, new Dictionary<string, string>
            {
                ["translate-x_y-1_0/metadata.json"] = "{}",
                ["translate-x_y-1_0/model/model.bin"] = "m",
            });
            var argos = Path.Combine(zips, "translate-x_y-1.0.zip");
            CreateZip(argos, new Dictionary<string, byte[]>
            {
                ["translate-x_y-1.0.argosmodel"] = File.ReadAllBytes(inner),
            });

            var progress = new Progress<string>(_ => { });
            await ModelPackageInstaller.InstallAsync(new[] { berg, nllb, argos }, root, progress);

            Assert.IsTrue(File.Exists(Path.Combine(root, "bergamot", "en-zh", "model.bin")), "bergamot 未就位");
            Assert.IsTrue(File.Exists(Path.Combine(root, "bergamot", "en-zh", "config.txt")), "bergamot 多文件缺失");
            Assert.IsTrue(File.Exists(Path.Combine(root, "models", "nllb-200-distilled-600M", "config.txt")), "nllb 未就位");
            Assert.IsTrue(File.Exists(Path.Combine(root, "argos_packages", "translate-x_y-1_0", "metadata.json")), "argos metadata 缺失");
            Assert.IsTrue(File.Exists(Path.Combine(root, "argos_packages", "translate-x_y-1_0", "model", "model.bin")), "argos model.bin 缺失");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [TestMethod]
    public async Task Install_UnknownZip_IsSkippedWithoutFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "mpi_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var unknown = CreateZip(Path.Combine(root, "something.zip"), new Dictionary<string, string>
            {
                ["random/file.txt"] = "r",
            });

            var progress = new Progress<string>(_ => { });
            await ModelPackageInstaller.InstallAsync(new[] { unknown }, root, progress);

            Assert.IsFalse(File.Exists(Path.Combine(root, "random", "file.txt")), "未知包不应被安装");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void ResolveTargets_WithoutCustomRoot_UsesBundledToolsLayout()
    {
        var (bergamotDir, nllbBase, argosDir) = ModelPackageInstaller.ResolveTargets(null);
        StringAssert.Contains(bergamotDir, Path.Combine("tools", "bergamot"));
        StringAssert.Contains(argosDir, Path.Combine("tools", "py", "argos_packages"));
        StringAssert.Contains(nllbBase, Path.Combine("tools", "py"));
    }

    private static string CreateZip(string path, Dictionary<string, string> textEntries)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var kv in textEntries) AddEntry(zip, kv.Key, System.Text.Encoding.UTF8.GetBytes(kv.Value));
        return path;
    }

    private static string CreateZip(string path, Dictionary<string, byte[]> binaryEntries)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var kv in binaryEntries) AddEntry(zip, kv.Key, kv.Value);
        return path;
    }

    private static void AddEntry(ZipArchive zip, string entryName, byte[] bytes)
    {
        var entry = zip.CreateEntry(entryName);
        using var s = entry.Open();
        s.Write(bytes, 0, bytes.Length);
    }
}

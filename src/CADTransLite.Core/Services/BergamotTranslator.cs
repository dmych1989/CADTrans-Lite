// Services/BergamotTranslator.cs
// 纯 .NET 离线翻译引擎（基于 Mozilla Bergamot，通过 BergamotTranslatorSharp 进程内运行）。
// 无 Python 依赖：模型以原生 bergamot.dll 推理；模型文件放在 _modelsRoot 下，按方向分目录存放。
using System.Collections.Concurrent;
using System.Threading;
using BergamotTranslatorSharp;
using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Models;

namespace CADTransLite.Core.Services;

/// <summary>
/// 离线翻译实现，使用 <see cref="BergamotTranslatorSharp.BlockingService"/> 在进程内推理。
/// 相比原先依赖嵌入式 Python（LibreTranslate / NLLB）的本地引擎，本实现：
/// <list type="bullet">
///   <item>不依赖 Python 运行时，无需启动子进程 / HTTP 服务；</item>
///   <item>模型随应用发布目录下的 <c>tools/bergamot/&lt;方向&gt;/config.txt</c> 加载；</item>
///   <item>语言对通过「直接模型」或「经英文中转(pivot)」两种策略解析，覆盖任意语言互译。</item>
/// </list>
/// </summary>
public sealed class BergamotTranslator : ITranslationApi
{
    private sealed class Cached
    {
        public BlockingService Service = null!;
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    private sealed class Plan
    {
        public string Key { get; set; } = string.Empty;
        public string DirectConfigPath { get; set; } = string.Empty;
        public string? PivotConfigPath { get; set; }
    }

    private readonly string _modelsRoot;
    private readonly ConcurrentDictionary<string, Cached> _cache = new();
    private readonly object _buildLock = new();

    public BergamotTranslator(string modelsRoot)
    {
        _modelsRoot = modelsRoot;
    }

    public string Name => "Bergamot (本地)";

    public Task<string> TranslateAsync(string text, string sourceLang, string targetLang,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(string.Empty);

        var svc = GetOrCreateService(sourceLang, targetLang);
        string? result = null;
        try
        {
            svc.Gate.Wait(cancellationToken);
            result = svc.Service.Translate(text);
        }
        finally
        {
            svc.Gate.Release();
        }

        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException(
                "Bergamot 返回了空译文（对应语言对的模型可能未正确安装）。请运行 tools/setup_bergamot.ps1 安装模型。");
        return Task.FromResult(result);
    }

    public Task<List<string>> TranslateBatchAsync(List<string> texts, string sourceLang, string targetLang,
        CancellationToken cancellationToken = default)
    {
        var svc = GetOrCreateService(sourceLang, targetLang);
        var outp = new List<string>(texts.Count);
        try
        {
            svc.Gate.Wait(cancellationToken);
            foreach (var t in texts)
            {
                if (string.IsNullOrWhiteSpace(t))
                {
                    outp.Add(string.Empty);
                    continue;
                }

                var r = svc.Service.Translate(t);
                if (string.IsNullOrWhiteSpace(r))
                    throw new InvalidOperationException(
                        "Bergamot 返回了空译文（模型可能未正确安装）。请运行 tools/setup_bergamot.ps1 安装模型。");
                outp.Add(r);
            }
        }
        finally
        {
            svc.Gate.Release();
        }

        return Task.FromResult(outp);
    }

    /// <summary>
    /// 预加载某语言对的模型（供 UI「检查并加载模型」使用）。成功返回 <c>null</c>，失败返回错误信息。
    /// </summary>
    public string? WarmUp(string sourceLang, string targetLang)
    {
        try
        {
            var svc = GetOrCreateService(sourceLang, targetLang);
            svc.Gate.Wait();
            try
            {
                svc.Service.Translate("test");
            }
            finally
            {
                svc.Gate.Release();
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private Cached GetOrCreateService(string sourceLang, string targetLang)
    {
        var (s, t) = NormalizePair(sourceLang, targetLang);
        var plan = ResolvePlan(s, t);
        var cacheKey = plan.Key;

        // 双重检查锁定，避免并发重复加载（模型加载较慢）。
        if (_cache.TryGetValue(cacheKey, out var existing))
            return existing;

        lock (_buildLock)
        {
            if (_cache.TryGetValue(cacheKey, out var existing2))
                return existing2;

            var svc = plan.PivotConfigPath == null
                ? new BlockingService(plan.DirectConfigPath)
                : new BlockingService(plan.DirectConfigPath, plan.PivotConfigPath);

            var cached = new Cached { Service = svc };
            _cache[cacheKey] = cached;
            return cached;
        }
    }

    private (string Source, string Target) NormalizePair(string sourceLang, string targetLang)
    {
        var t = (targetLang ?? "en").Trim().ToLowerInvariant();
        var s = (sourceLang ?? "auto").Trim().ToLowerInvariant();

        // Bergamot 不支持自动检测：auto 时按目标语言推断来源（常见 CAD 场景：英→中 / 中→英）。
        if (s == "auto")
            s = t == "en" ? "zh" : "en";

        return (NormalizeCode(s), NormalizeCode(t));
    }

    private static string NormalizeCode(string code)
    {
        // 取主语言子标签：zh-Hans / zh-CN -> zh 等。
        var main = code.Split('-')[0];
        return main switch
        {
            "ja" or "jp" => "ja",
            "ko" => "ko",
            "fr" => "fr",
            "de" => "de",
            "es" => "es",
            "ru" => "ru",
            "pt" => "pt",
            "ar" => "ar",
            "zh" => "zh",
            "en" => "en",
            "uk" => "uk",
            _ => main
        };
    }

    private Plan ResolvePlan(string s, string t)
    {
        var direct = Path.Combine(_modelsRoot, $"{s}-{t}", "config.txt");
        if (File.Exists(direct))
            return new Plan { Key = $"d:{s}-{t}", DirectConfigPath = direct };

        // 经英文中转：需要 s-en 与 en-t 两个方向模型都已安装。
        if (s != "en" && t != "en")
        {
            var c1 = Path.Combine(_modelsRoot, $"{s}-en", "config.txt");
            var c2 = Path.Combine(_modelsRoot, $"en-{t}", "config.txt");
            if (File.Exists(c1) && File.Exists(c2))
                return new Plan { Key = $"p:{s}-en|en-{t}", DirectConfigPath = c1, PivotConfigPath = c2 };
        }

        throw new InvalidOperationException(BuildMissingModelMessage(s, t));
    }

    private string BuildMissingModelMessage(string s, string t)
    {
        var installed = ListInstalledDirections();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Bergamot 未安装「{s}→{t}」语言对模型。");
        sb.AppendLine("请运行 tools/setup_bergamot.ps1 安装模型（默认下载 en-* 与 *-en 方向，可覆盖任意语言对互译）。");
        sb.Append(installed.Count > 0
            ? $"当前已安装方向：{string.Join(", ", installed)}"
            : "当前 tools/bergamot 下没有任何模型。");
        return sb.ToString();
    }

    private List<string> ListInstalledDirections()
    {
        var list = new List<string>();
        try
        {
            if (!Directory.Exists(_modelsRoot))
                return list;
            foreach (var dir in Directory.EnumerateDirectories(_modelsRoot))
            {
                if (File.Exists(Path.Combine(dir, "config.txt")))
                    list.Add(Path.GetFileName(dir));
            }
        }
        catch
        {
            // 目录不可访问时忽略，仅返回空列表。
        }

        return list;
    }
}

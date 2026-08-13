// EngineFactory.cs
// Maps an engine display name + a config map to a concrete ITranslationApi, mirroring the
// provider selection logic in the main application's BuildTranslationApi().
using System.Collections.Generic;
using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Models;
using CADTransLite.Core.Services;

namespace CADTransLite.McpServer;

internal static class EngineFactory
{
    public static readonly List<string> EngineNames = new()
    {
        "Argos Translate (本地)",
        "LibreTranslate (本地)",
        "NLLB (本地)",
        "DeepLX",
        "自定义AI",
        "百度翻译",
        "腾讯翻译",
        "Microsoft Translator",
        "DeepL"
    };

    /// <summary>Default base URLs for the local HTTP engines.</summary>
    public static readonly Dictionary<string, string> LocalDefaults = new()
    {
        ["argos_url"] = "http://127.0.0.1:5001",
        ["libre_url"] = "http://127.0.0.1:5000",
        ["nllb_url"] = "http://127.0.0.1:5002",
        ["deeplx_url"] = "http://127.0.0.1:1188"
    };

    public static ITranslationApi Build(string engine, Dictionary<string, string> cfg)
    {
        switch (engine)
        {
            case "Argos Translate (本地)":
                return new ArgosTranslateTranslator(new TranslationApiConfig
                {
                    BaseUrl = Cfg(cfg, "argos_url", LocalDefaults["argos_url"])
                });
            case "LibreTranslate (本地)":
                return new LibreTranslateTranslator(new TranslationApiConfig
                {
                    BaseUrl = Cfg(cfg, "libre_url", LocalDefaults["libre_url"])
                });
            case "NLLB (本地)":
                return new NllbTranslator(new TranslationApiConfig
                {
                    BaseUrl = Cfg(cfg, "nllb_url", LocalDefaults["nllb_url"])
                });
            case "DeepLX":
                return new DeepLXTranslator(new TranslationApiConfig
                {
                    BaseUrl = Cfg(cfg, "deeplx_url", LocalDefaults["deeplx_url"])
                });
            case "自定义AI":
                return new CustomAiTranslator(new TranslationApiSettings
                {
                    EnableCustomAI = true,
                    ApiKey = Cfg(cfg, "api_key", ""),
                    BaseUrl = Cfg(cfg, "base_url", ""),
                    ModelName = Cfg(cfg, "model", "gpt-4o-mini")
                });
            case "百度翻译":
                return new BaiduTranslator(new TranslationApiConfig
                {
                    AppId = Cfg(cfg, "app_id", ""),
                    SecretKey = Cfg(cfg, "app_key", "")
                });
            case "腾讯翻译":
                return new TencentTranslator(new TranslationApiConfig
                {
                    AppId = Cfg(cfg, "secret_id", ""),
                    SecretKey = Cfg(cfg, "secret_key", "")
                });
            case "Microsoft Translator":
                return new MicrosoftTranslator(new TranslationApiConfig
                {
                    ApiKey = Cfg(cfg, "api_key", ""),
                    Region = Cfg(cfg, "region", "")
                });
            case "DeepL":
                return new DeepLTranslator(new TranslationApiConfig
                {
                    ApiKey = Cfg(cfg, "api_key", "")
                });
            default:
                throw new InvalidOperationException($"未知翻译引擎：{engine}");
        }
    }

    private static string Cfg(Dictionary<string, string> cfg, string key, string def) =>
        cfg.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : def;
}

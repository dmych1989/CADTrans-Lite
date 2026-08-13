// MainWindowViewModel.cs
// Full implementation of the main window view model.
// v2.8: settings persistence, real translation, extract/translate/writeback, ODA path management.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Models;
using CADTransLite.Core.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CADTransLite.UI
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        // ─────────────────────────────────────────────────────────────
        // Service fields
        // ─────────────────────────────────────────────────────────────
        private readonly OdaConverter _odaConverter;
        private readonly DwgExtractor _extractor;
        private readonly ExcelHandler _excelHandler;
        private readonly DwgWriter _dwgWriter;
        private readonly SettingsManager _settingsManager;
        private TranslationService? _translationService;

        // ─────────────────────────────────────────────────────────────
        // Backing fields – status / progress
        // ─────────────────────────────────────────────────────────────
        private string _statusText = "就绪";
        private double _progressValue;
        private bool _isProcessing;

        // ─────────────────────────────────────────────────────────────
        // Backing fields – file state
        // ─────────────────────────────────────────────────────────────
        private string? _currentFilePath;          // loaded CAD file (.dwg or .dxf)
        private string? _currentExcelPath;         // loaded Excel file
        private string? _lastExtractedDxfPath;     // DXF path from last extraction (cached for write-back)
        private List<TranslationItem>? _extractedItems;   // last extraction result (merged)

        // ─────────────────────────────────────────────────────────────
        // Backing fields – ODA
        // ─────────────────────────────────────────────────────────────
        private string _odaPath = OdaSettings.DefaultExecutablePath;
        private string _odaStatusText = "检测中...";
        private string _odaStatusColor = "Orange";
        private bool _isOdaAvailable;

        // ─────────────────────────────────────────────────────────────
        // Backing fields – translation engine
        // ─────────────────────────────────────────────────────────────
        private string _selectedProvider = "百度翻译";
        private string _sourceLangCode = "en";
        private string _targetLangCode = "zh";

        // Baidu
        private bool _enableBaiduTranslate;
        private string _baiduAppId = "";
        private string _baiduAppKey = "";

        // Tencent
        private bool _enableTencentTranslate;
        private string _tencentSecretId = "";
        private string _tencentSecretKey = "";

        // Microsoft
        private bool _enableMicrosoftTranslate;
        private string _microsoftApiKey = "";
        private string _microsoftRegion = "global";

        // DeepLX
        private bool _enableDeepLX;
        private string _deepLXUrl = "http://127.0.0.1:1188";

        // LibreTranslate (本地)
        private bool _enableLibreTranslate;
        private string _libreTranslateUrl = "http://127.0.0.1:5000";

        // Argos Translate (本地)
        private bool _enableArgos;
        private string _argosUrl = "http://127.0.0.1:5001";

        // NLLB (本地离线)
        private bool _enableNllb;
        private string _nllbUrl = "http://127.0.0.1:5002";

        // ─────────────────────────────────────────────────────────────
        // 本地服务（LibreTranslate / Argos / NLLB）实时运行状态
        // ─────────────────────────────────────────────────────────────
        private readonly LocalServiceStatus _libreTranslateService = new();
        private readonly LocalServiceStatus _argosService = new();
        private readonly LocalServiceStatus _nllbService = new();
        private readonly System.Timers.Timer _localServiceMonitor;

        // DeepL
        private bool _enableDeepL;
        private string _deepLApiKey = "";

        // Custom AI
        private bool _enableCustomAI;
        private string _apiKey = "";
        private string _baseUrl = "https://api.openai.com/v1";
        private string _modelName = "gpt-4o-mini";

        // ─────────────────────────────────────────────────────────────
        // ImportSettings backing
        // ─────────────────────────────────────────────────────────────
        private bool _importBlockAttributes = true;
        private bool _importMTextParagraph = false;
        private bool _importMTextWhole = true;
        private bool _importFrozenLayers = false;
        private bool _importLockedLayers = false;
        private bool _importOffLayers = false;

        // ─────────────────────────────────────────────────────────────
        // Phase 3 — Rich Excel format & cleaned dedup backing fields
        // ─────────────────────────────────────────────────────────────
        private bool _useRichExcelFormat = true;
        private bool _enableCleanedDedup = false;

        // ─────────────────────────────────────────────────────────────
        // Phase 4 — Layout adjust, Glossary, AI filter, DWG version
        // ─────────────────────────────────────────────────────────────
        private bool _enableLayoutAdjust = true;
        private bool _enableGlossary = false;
        private bool _enableAiFilter = false;
        private DwgOutputVersion? _selectedOutputVersion;

        // ─────────────────────────────────────────────────────────────
        // Constructor
        // ─────────────────────────────────────────────────────────────
        public MainWindowViewModel()
        {
            _odaConverter    = new OdaConverter();
            _extractor       = new DwgExtractor();
            _excelHandler    = new ExcelHandler();
            _dwgWriter       = new DwgWriter();
            _settingsManager = new SettingsManager();

            // Initialize default language selections (must happen before LoadSettings)
            _sourceLanguage = SupportedLanguages.ByCode("EN") ?? SupportedLanguages.All[1];
            _targetLanguage = SupportedLanguages.ByCode("ZH") ?? SupportedLanguages.All[0];

            // Commands
            ExtractAndExportCommand    = new AsyncRelayCommand(ExtractAndExportAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
            TranslateCommand           = new AsyncRelayCommand(TranslateAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
            ImportAndWriteBackCommand  = new AsyncRelayCommand(ImportAndWriteBackAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
            ExportTranslationOnlyCommand = new AsyncRelayCommand(ExportTranslationOnlyAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
            TestTranslationApiCommand  = new AsyncRelayCommand(TestTranslationApiAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
            StartLocalServiceCommand = new AsyncRelayCommand<string>(StartLocalServiceAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
            PauseLocalServiceCommand = new AsyncRelayCommand<string>(PauseLocalServiceAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
            FetchModelsCommand        = new AsyncRelayCommand(FetchModelsAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
            TranslateViaMcpCommand    = new AsyncRelayCommand(TranslateViaMcpAsync);
            StartMcpBridgeCommand     = new AsyncRelayCommand(StartMcpBridgeAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
            SaveSettingsCommand        = new RelayCommand(SaveSettings);
            BrowseOdaPathCommand       = new RelayCommand(BrowseOdaPath);
            OpenOdaDownloadCommand     = new RelayCommand(OpenOdaDownloadPage);
            SelectExcelCommand         = new RelayCommand(SelectExcelFile);
            BrowseTranslateExcelCommand = new RelayCommand(BrowseTranslateExcel);
            BrowseImportExcelCommand    = new RelayCommand(BrowseImportExcel);
            BrowseGlossaryPathCommand   = new RelayCommand(BrowseGlossaryPath);
            LoadGlossaryCommand         = new RelayCommand(LoadGlossary);
            AddGlossaryEntryCommand     = new RelayCommand(AddGlossaryEntry);
            RemoveGlossaryEntryCommand  = new RelayCommand(RemoveGlossaryEntry);
            SaveGlossaryCommand         = new RelayCommand(SaveGlossary);

            // Load persisted settings, then detect ODA
            LoadSettings();
            UpdateLanguageLists();
            CheckOdaStatus();

            // 后台轮询三个本地 Python 翻译服务的端口，自动更新运行状态显示
            _localServiceMonitor = new System.Timers.Timer(3000);
            _localServiceMonitor.Elapsed += (_, __) => RefreshLocalServiceStatuses();
            _localServiceMonitor.AutoReset = false; // 每次处理完再重新计时，避免重叠执行
            _localServiceMonitor.Start();
            _ = Task.Run(() => RefreshLocalServiceStatuses());
        }

        /// <summary>
        /// Rebuilds the source/target language lists so the dropdowns only show languages the
        /// currently selected engine supports, and keeps the current selection valid (falling back
        /// to a sensible default when the previously selected language is unsupported by the new engine).
        /// </summary>
        private void UpdateLanguageLists()
        {
            var list = SupportedLanguages.ForProvider(_selectedProvider);
            SourceLanguages = list;
            TargetLanguages = list;

            if (_sourceLanguage is null || !list.Contains(_sourceLanguage))
            {
                SourceLanguage = list.FirstOrDefault(l => l.Code == "EN") ?? list.First()!;
            }
            if (_targetLanguage is null || !list.Contains(_targetLanguage))
            {
                TargetLanguage = list.FirstOrDefault(l => l.Code == "ZH") ?? list.First()!;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Status / progress properties
        // ─────────────────────────────────────────────────────────────

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set { _isProcessing = value; OnPropertyChanged(); }
        }

        // ─────────────────────────────────────────────────────────────
        // File state properties
        // ─────────────────────────────────────────────────────────────

        public string? CurrentFilePath
        {
            get => _currentFilePath;
            set { _currentFilePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasFile)); }
        }

        public string? CurrentExcelPath
        {
            get => _currentExcelPath;
            set { _currentExcelPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasExcel)); }
        }

        /// <summary>True if a CAD file has been loaded.</summary>
        public bool HasFile  => !string.IsNullOrEmpty(_currentFilePath);
        /// <summary>True if an Excel file has been loaded.</summary>
        public bool HasExcel => !string.IsNullOrEmpty(_currentExcelPath);

        // Custom Excel path for one-click translate
        private string? _translateExcelPath;
        public string? TranslateExcelPath
        {
            get => _translateExcelPath;
            set { _translateExcelPath = value; OnPropertyChanged(); }
        }

        // Custom Excel path for import write-back
        private string? _importExcelPath;
        public string? ImportExcelPath
        {
            get => _importExcelPath;
            set { _importExcelPath = value; OnPropertyChanged(); }
        }

        // ─────────────────────────────────────────────────────────────
        // ODA properties
        // ─────────────────────────────────────────────────────────────

        public string OdaPath
        {
            get => _odaPath;
            set
            {
                if (_odaPath == value) return;
                _odaPath = value;
                _odaConverter.ExecutablePath = value;
                OnPropertyChanged();
                // Re-check whenever path changes
                CheckOdaStatus();
            }
        }

        public string OdaStatusText
        {
            get => _odaStatusText;
            set { _odaStatusText = value; OnPropertyChanged(); }
        }

        public string OdaStatusColor
        {
            get => _odaStatusColor;
            set { _odaStatusColor = value; OnPropertyChanged(); }
        }

        public bool IsOdaAvailable
        {
            get => _isOdaAvailable;
            set { _isOdaAvailable = value; OnPropertyChanged(); }
        }

        // ─────────────────────────────────────────────────────────────
        // Translation engine properties
        // ─────────────────────────────────────────────────────────────

        public string SelectedProvider
        {
            get => _selectedProvider;
            set
            {
                if (_selectedProvider == value) return;
                _selectedProvider = value;
                OnPropertyChanged();
                // Notify all visibility properties to show/hide engine panels
                OnPropertyChanged(nameof(BaiduSettingsVisibility));
                OnPropertyChanged(nameof(TencentSettingsVisibility));
                OnPropertyChanged(nameof(MicrosoftSettingsVisibility));
                OnPropertyChanged(nameof(DeepLSettingsVisibility));
                OnPropertyChanged(nameof(DeepLXSettingsVisibility));
                OnPropertyChanged(nameof(LibreTranslateSettingsVisibility));
                OnPropertyChanged(nameof(ArgosSettingsVisibility));
                OnPropertyChanged(nameof(NllbSettingsVisibility));
                OnPropertyChanged(nameof(CustomAISettingsVisibility));
                OnPropertyChanged(nameof(SupportedModelsText));
                UpdateLanguageLists();
            }
        }

        /// <summary>
        /// Hint describing the model/backend the currently selected engine uses, shown in the UI
        /// so the user knows what the channel supports (models / language packages).
        /// </summary>
        public string SupportedModelsText => _selectedProvider switch
        {
            "百度翻译" => "百度神经网络翻译，无需选择模型。",
            "腾讯翻译" => "腾讯神经网络翻译，无需选择模型。",
            "Microsoft Translator" => "Microsoft 神经网络翻译，无需选择模型。",
            "DeepL" => "DeepL 神经网络翻译，无需选择模型。",
            "DeepLX" => "DeepL 神经网络翻译（经 DeepLX 代理），无需选择模型。",
            "自定义AI" => "可配置大模型：在下方填写模型名或点击“获取模型”拉取（如 gpt-4o、deepseek-chat）。",
            "NLLB (本地)" => "本地模型：NLLB-200-Distilled-600M（已随软件打包，离线可用）。",
            "Argos Translate (本地)" => "本地引擎：基于 Argos 语言包 en/zh/ja/ko/fr/de/es/ru/pt/it，无独立模型选择。",
            "LibreTranslate (本地)" => "本地引擎：基于 Argos 语言包 en/zh/ja/ko/fr/de/es/ru/pt/it，无独立模型选择。",
            _ => string.Empty,
        };

        public string SourceLangCode
        {
            get => _sourceLangCode;
            set { _sourceLangCode = value; OnPropertyChanged(); }
        }

        public string TargetLangCode
        {
            get => _targetLangCode;
            set { _targetLangCode = value; OnPropertyChanged(); }
        }

        // Baidu
        public bool EnableBaiduTranslate
        {
            get => _enableBaiduTranslate;
            set { _enableBaiduTranslate = value; OnPropertyChanged(); }
        }
        public string BaiduAppId  { get => _baiduAppId;  set { _baiduAppId  = value; OnPropertyChanged(); } }
        public string BaiduAppKey { get => _baiduAppKey; set { _baiduAppKey = value; OnPropertyChanged(); } }

        // Tencent
        public bool EnableTencentTranslate
        {
            get => _enableTencentTranslate;
            set { _enableTencentTranslate = value; OnPropertyChanged(); }
        }
        public string TencentSecretId  { get => _tencentSecretId;  set { _tencentSecretId  = value; OnPropertyChanged(); } }
        public string TencentSecretKey { get => _tencentSecretKey; set { _tencentSecretKey = value; OnPropertyChanged(); } }

        // Microsoft
        public bool EnableMicrosoftTranslate
        {
            get => _enableMicrosoftTranslate;
            set { _enableMicrosoftTranslate = value; OnPropertyChanged(); }
        }
        public string MicrosoftApiKey { get => _microsoftApiKey; set { _microsoftApiKey = value; OnPropertyChanged(); } }
        public string MicrosoftRegion { get => _microsoftRegion; set { _microsoftRegion = value; OnPropertyChanged(); } }

        // DeepLX
        public bool EnableDeepLX { get => _enableDeepLX; set { _enableDeepLX = value; OnPropertyChanged(); } }
        public string DeepLXUrl  { get => _deepLXUrl;    set { _deepLXUrl    = value; OnPropertyChanged(); } }

        // LibreTranslate (本地)
        public bool EnableLibreTranslate { get => _enableLibreTranslate; set { _enableLibreTranslate = value; OnPropertyChanged(); } }
        public string LibreTranslateUrl  { get => _libreTranslateUrl;    set { _libreTranslateUrl    = value; OnPropertyChanged(); } }

        // Argos Translate (本地)
        public bool EnableArgos { get => _enableArgos; set { _enableArgos = value; OnPropertyChanged(); } }
        public string ArgosUrl  { get => _argosUrl;    set { _argosUrl    = value; OnPropertyChanged(); } }

        // NLLB (本地离线)
        public bool EnableNllb { get => _enableNllb; set { _enableNllb = value; OnPropertyChanged(); } }
        public string NllbUrl  { get => _nllbUrl;    set { _nllbUrl    = value; OnPropertyChanged(); } }

        // DeepL
        public bool EnableDeepL     { get => _enableDeepL;     set { _enableDeepL     = value; OnPropertyChanged(); } }
        public string DeepLApiKey   { get => _deepLApiKey;     set { _deepLApiKey     = value; OnPropertyChanged(); } }

        // Custom AI
        public bool EnableCustomAI  { get => _enableCustomAI;  set { _enableCustomAI  = value; OnPropertyChanged(); } }
        public string ApiKey        { get => _apiKey;          set { _apiKey          = value; OnPropertyChanged(); } }
        public string BaseUrl       { get => _baseUrl;         set { _baseUrl         = value; OnPropertyChanged(); } }
        public string ModelName     { get => _modelName;       set { _modelName       = value; OnPropertyChanged(); } }

        /// <summary>
        /// 自定义AI 服务商快速预设。选择后自动填充 Base URL（及该服务商的默认模型名称）。
        /// 注意：Base URL 为接口基地址，CustomAiTranslator 会自动拼接 "/chat/completions"。
        /// </summary>
        public sealed record CustomAiPreset(string Name, string BaseUrl, string DefaultModel = "");

        public IReadOnlyList<CustomAiPreset> CustomAiPresets { get; } = new CustomAiPreset[]
        {
            new("OpenAI",               "https://api.openai.com/v1",                                                  "gpt-4o-mini"),
            new("DeepSeek",             "https://api.deepseek.com/v1",                                               "deepseek-chat"),
            new("智谱 GLM",             "https://open.bigmodel.cn/api/paas/v4",                                      "glm-4-flash"),
            new("英伟达 NVIDIA",        "https://integrate.api.nvidia.com/v1",                                       "meta/llama-3.1-8b-instruct"),
            new("商汤 SenseNova",       "https://token.sensenova.cn/v1",                                             "SenseChat-5"),
            new("讯飞星火 Spark",       "https://maas-api.cn-huabei-1.xf-yun.com/v2",                                "generalv3.5"),
            new("阶跃 StepFun",         "https://api.stepfun.com/step_plan/v1",                                      "step-1-flash"),
            new("Cline",                "https://api.cline.bot/api/v1",                                              ""),
            new("Agnes AI",             "https://apihub.agnes-ai.com/v1",                                            ""),
            new("GitHub Models",        "https://models.github.ai/inference",                                       "gpt-4o"),
            new("通义千问 Qwen",        "https://llm-cj8mxh98n5vpkcqa.cn-beijing.maas.aliyuncs.com/compatible-mode/v1", "qwen-plus"),
            new("Cloudflare",           "https://api.cloudflare.com/client/v4/accounts/769125449574e80c5184124aecbbef93/ai/v1", "@cf/meta/llama-3-8b-instruct"),
            new("OpenRouter",           "https://openrouter.ai/api/v1",                                              "openai/gpt-4o-mini"),
            new("硅基流动 SiliconFlow",  "https://api.siliconflow.cn/v1",                                            "deepseek-ai/DeepSeek-V3"),
            new("推理时代 AIHubMix",     "https://aihubmix.com/v1",                                                   "deepseek-chat"),
        };

        private CustomAiPreset? _selectedCustomAiPreset;
        public CustomAiPreset? SelectedCustomAiPreset
        {
            get => _selectedCustomAiPreset;
            set
            {
                if (Equals(_selectedCustomAiPreset, value)) return;
                _selectedCustomAiPreset = value;
                if (value is not null)
                {
                    BaseUrl = value.BaseUrl;
                    if (!string.IsNullOrWhiteSpace(value.DefaultModel))
                    {
                        ModelName = value.DefaultModel;
                        if (!ModelNames.Contains(value.DefaultModel))
                            ModelNames.Insert(0, value.DefaultModel);
                    }
                }
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 模型名称下拉建议项。默认给出常用模型；点击「获取模型列表」后会被接口返回的模型覆盖。
        /// </summary>
        public ObservableCollection<string> ModelNames { get; } = new ObservableCollection<string>
        {
            "gpt-4o-mini", "gpt-4o", "deepseek-chat",
        };

        private bool _isFetchingModels;
        /// <summary>是否正在从接口拉取模型列表（用于按钮忙状态）。</summary>
        public bool IsFetchingModels
        {
            get => _isFetchingModels;
            set { _isFetchingModels = value; OnPropertyChanged(); }
        }

        // ─────────────────────────────────────────────────────────────
        // Translation engine visibility (bound from XAML)
        // Only the panel matching SelectedProvider is shown.
        // ─────────────────────────────────────────────────────────────

        public System.Windows.Visibility BaiduSettingsVisibility =>
            _selectedProvider == "百度翻译"
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

        public System.Windows.Visibility TencentSettingsVisibility =>
            _selectedProvider == "腾讯翻译"
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

        public System.Windows.Visibility MicrosoftSettingsVisibility =>
            _selectedProvider == "Microsoft Translator"
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

        public System.Windows.Visibility DeepLSettingsVisibility =>
            _selectedProvider == "DeepL"
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

        public System.Windows.Visibility DeepLXSettingsVisibility =>
            _selectedProvider == "DeepLX"
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

        public System.Windows.Visibility LibreTranslateSettingsVisibility =>
            _selectedProvider == "LibreTranslate (本地)"
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

        public System.Windows.Visibility ArgosSettingsVisibility =>
            _selectedProvider == "Argos Translate (本地)"
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

        public System.Windows.Visibility NllbSettingsVisibility =>
            _selectedProvider == "NLLB (本地)"
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

        public System.Windows.Visibility CustomAISettingsVisibility =>
            _selectedProvider == "自定义AI"
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

        // ─────────────────────────────────────────────────────────────
        // ImportSettings properties (bound from XAML)
        // ─────────────────────────────────────────────────────────────

        public bool ImportBlockAttributes
        {
            get => _importBlockAttributes;
            set { _importBlockAttributes = value; OnPropertyChanged(); }
        }
        public bool ImportMTextParagraph
        {
            get => _importMTextParagraph;
            set { _importMTextParagraph = value; OnPropertyChanged(); }
        }
        public bool ImportMTextWhole
        {
            get => _importMTextWhole;
            set { _importMTextWhole = value; OnPropertyChanged(); }
        }
        public bool ImportFrozenLayers
        {
            get => _importFrozenLayers;
            set { _importFrozenLayers = value; OnPropertyChanged(); }
        }
        public bool ImportLockedLayers
        {
            get => _importLockedLayers;
            set { _importLockedLayers = value; OnPropertyChanged(); }
        }
        public bool ImportOffLayers
        {
            get => _importOffLayers;
            set { _importOffLayers = value; OnPropertyChanged(); }
        }

        // ─────────────────────────────────────────────────────────────
        // Phase 3 — Rich Excel format & cleaned dedup properties
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 是否使用多列富元数据 Excel 格式导出（11 列）。
        /// false = 传统 2 列格式。
        /// </summary>
        public bool UseRichExcelFormat
        {
            get => _useRichExcelFormat;
            set { _useRichExcelFormat = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 是否启用清洗后去重。启用后，合并阶段会在第一步合并之后，
        /// 按 (EntityType, CleanedText) 进行二次去重。
        /// </summary>
        public bool EnableCleanedDedup
        {
            get => _enableCleanedDedup;
            set { _enableCleanedDedup = value; OnPropertyChanged(); }
        }

        // ─────────────────────────────────────────────────────────────
        // Phase 4 — Layout adjust, Glossary, AI filter, DWG version properties
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 是否启用布局自适应（翻译后文字过长时自动缩放字高）。
        /// </summary>
        public bool EnableLayoutAdjust
        {
            get => _enableLayoutAdjust;
            set { _enableLayoutAdjust = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 是否启用术语表替换（Phase 4 预留占位，暂禁用）。
        /// </summary>
        public bool EnableGlossary
        {
            get => _enableGlossary;
            set { _enableGlossary = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 是否启用 AI 智能过滤（Phase 4 预留占位，暂禁用）。
        /// </summary>
        public bool EnableAiFilter
        {
            get => _enableAiFilter;
            set { _enableAiFilter = value; OnPropertyChanged(); }
        }

        private string _aiFilterPrompt = string.Empty;
        /// <summary>AI 过滤自定义 prompt 模板。</summary>
        public string AiFilterPrompt
        {
            get => _aiFilterPrompt;
            set { _aiFilterPrompt = value; OnPropertyChanged(); }
        }

        private string _aiFilterModelName = string.Empty;
        /// <summary>AI 过滤使用的模型名称。空则复用翻译 API 的 ModelName。</summary>
        public string AiFilterModelName
        {
            get => _aiFilterModelName;
            set { _aiFilterModelName = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 选中的 DWG 输出版本。
        /// </summary>
        public DwgOutputVersion? SelectedOutputVersion
        {
            get => _selectedOutputVersion;
            set { _selectedOutputVersion = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 所有支持的 DWG 输出版本列表。
        /// </summary>
        public List<DwgOutputVersion> OutputVersions { get; } = DwgOutputVersion.GetAllVersions();

        // ─────────────────────────────────────────────────────────────
        // Phase 4 — Glossary properties
        // ─────────────────────────────────────────────────────────────

        private string _glossaryPath = string.Empty;
        /// <summary>术语表文件路径。</summary>
        public string GlossaryPath
        {
            get => _glossaryPath;
            set { _glossaryPath = value; OnPropertyChanged(); }
        }

        private ObservableCollection<GlossaryEntry> _glossaryEntries = new();
        /// <summary>术语条目列表（用于 UI 绑定）。</summary>
        public ObservableCollection<GlossaryEntry> GlossaryEntries
        {
            get => _glossaryEntries;
            set { _glossaryEntries = value; OnPropertyChanged(); }
        }

        // Stub properties for unused checkboxes in XAML (no-op, just prevent binding errors)
        public bool ImportProxyObjects  { get; set; } = true;
        public bool ImportDimensionText { get; set; } = false;

        // Export suffix property
        private string _exportSuffix = "_纯翻译";
        public string ExportSuffix
        {
            get => _exportSuffix;
            set { _exportSuffix = value; OnPropertyChanged(); }
        }

        // ─────────────────────────────────────────────────────────────
        // Language / Provider collections (bound from XAML ComboBoxes)
        // ─────────────────────────────────────────────────────────────

        /// <summary>All supported languages for the source/target ComboBoxes.</summary>
        private IReadOnlyList<LanguageInfo> _sourceLanguages = SupportedLanguages.All.ToList();
        private IReadOnlyList<LanguageInfo> _targetLanguages = SupportedLanguages.All.ToList();

        public IReadOnlyList<LanguageInfo> SourceLanguages
        {
            get => _sourceLanguages;
            private set { _sourceLanguages = value; OnPropertyChanged(); }
        }

        public IReadOnlyList<LanguageInfo> TargetLanguages
        {
            get => _targetLanguages;
            private set { _targetLanguages = value; OnPropertyChanged(); }
        }

        private LanguageInfo _sourceLanguage;
        private LanguageInfo _targetLanguage;

        public LanguageInfo SourceLanguage
        {
            get => _sourceLanguage;
            set
            {
                if (_sourceLanguage == value) return;
                _sourceLanguage = value;
                _sourceLangCode = value?.Code?.ToLowerInvariant() ?? "en";
                OnPropertyChanged();
            }
        }

        public LanguageInfo TargetLanguage
        {
            get => _targetLanguage;
            set
            {
                if (_targetLanguage == value) return;
                _targetLanguage = value;
                _targetLangCode = value?.Code?.ToLowerInvariant() ?? "zh";
                OnPropertyChanged();
            }
        }

        /// <summary>Available translation providers for the Provider ComboBox.</summary>
        public IReadOnlyList<string> TranslationProviders { get; } = new[]
        {
            "百度翻译", "腾讯翻译", "Microsoft Translator", "DeepL", "DeepLX",
            "LibreTranslate (本地)", "Argos Translate (本地)", "NLLB (本地)", "自定义AI",
        };

        // ─────────────────────────────────────────────────────────────
        // Commands
        // ─────────────────────────────────────────────────────────────

        public IAsyncRelayCommand ExtractAndExportCommand   { get; }
        public IAsyncRelayCommand TranslateCommand          { get; }
        public IAsyncRelayCommand ImportAndWriteBackCommand { get; }
        public IAsyncRelayCommand ExportTranslationOnlyCommand { get; }
        public IAsyncRelayCommand TestTranslationApiCommand { get; }
        public IAsyncRelayCommand StartLocalServiceCommand { get; }
        public IAsyncRelayCommand PauseLocalServiceCommand { get; }
        public IAsyncRelayCommand FetchModelsCommand     { get; }
        public IAsyncRelayCommand TranslateViaMcpCommand { get; }
        public IAsyncRelayCommand StartMcpBridgeCommand  { get; }

        // 本地翻译服务实时运行状态（供设置面板绑定显示）
        public LocalServiceStatus LibreTranslateService => _libreTranslateService;
        public LocalServiceStatus ArgosService           => _argosService;
        public LocalServiceStatus NllbService            => _nllbService;
        public IRelayCommand      SaveSettingsCommand       { get; }
        public IRelayCommand      BrowseOdaPathCommand      { get; }
        public IRelayCommand      OpenOdaDownloadCommand    { get; }
        public IRelayCommand      SelectExcelCommand        { get; }
        public IRelayCommand      BrowseTranslateExcelCommand { get; }
        public IRelayCommand      BrowseImportExcelCommand    { get; }
        public IRelayCommand      BrowseGlossaryPathCommand   { get; }
        public IRelayCommand      LoadGlossaryCommand         { get; }
        public IRelayCommand      AddGlossaryEntryCommand     { get; }
        public IRelayCommand      RemoveGlossaryEntryCommand  { get; }
        public IRelayCommand      SaveGlossaryCommand         { get; }

        /// <summary>Alias: clicking the drop zone triggers the same as "select DWG file".</summary>
        public ICommand SelectDwgCommand => ExtractAndExportCommand;

        // ─────────────────────────────────────────────────────────────
        // ODA helpers
        // ─────────────────────────────────────────────────────────────

        public void CheckOdaStatus()
        {
            bool found = File.Exists(_odaPath);
            if (found)
            {
                OdaStatusText  = "✅ ODA File Converter 已安装";
                OdaStatusColor = "Green";
                IsOdaAvailable = true;
                _odaConverter.ExecutablePath = _odaPath;
            }
            else
            {
                OdaStatusText  = "⚠️ 未检测到 ODA File Converter";
                OdaStatusColor = "OrangeRed";
                IsOdaAvailable = false;
            }
        }

        private void BrowseOdaPath()
        {
            var dlg = new OpenFileDialog
            {
                Title  = "选择 ODA File Converter 可执行文件",
                Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                FileName = "ODAFileConverter.exe",
            };
            if (!string.IsNullOrEmpty(_odaPath) && File.Exists(_odaPath))
                dlg.InitialDirectory = Path.GetDirectoryName(_odaPath);

            if (dlg.ShowDialog() == true)
                OdaPath = dlg.FileName;   // setter triggers CheckOdaStatus()
        }

        private static void OpenOdaDownloadPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.opendesign.com/guestfiles/oda_file_converter",
                    UseShellExecute = true,
                });
            }
            catch { /* silently ignore */ }
        }

        // ─────────────────────────────────────────────────────────────
        // File loading (drag-drop or button)
        // ─────────────────────────────────────────────────────────────

        /// <summary>Called from code-behind after drag-drop or file-open dialog.</summary>
        public void LoadDwgFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                StatusText = $"❌ 文件不存在：{filePath}";
                return;
            }
            CurrentFilePath = filePath;
            _extractedItems = null;
            _lastExtractedDxfPath = null;
            StatusText = $"✅ 已加载：{Path.GetFileName(filePath)}";
            ProgressValue = 0;
        }

        // ─────────────────────────────────────────────────────────────
        // 一键通过 MCP 桥接翻译当前图纸
        // ─────────────────────────────────────────────────────────────

        private const string McpBridgeHost = "127.0.0.1";
        private const int McpBridgePort = 8090;

        private async Task TranslateViaMcpAsync()
        {
            if (_isProcessing) return;

            if (string.IsNullOrEmpty(_currentFilePath) || !File.Exists(_currentFilePath))
            {
                StatusText = "❌ 请先加载一个 CAD 图纸（.dwg / .dxf）";
                return;
            }

            // 集成启动：若 MCP 桥接未运行，自动拉起后再继续（无需手动启动 CADTransLite.McpBridge.exe）。
            if (!await EnsureMcpBridgeAsync())
                return;

            var filePath = _currentFilePath!;
            var src = _sourceLanguage?.GetProviderCode(_selectedProvider) ?? "en";
            var tgt = _targetLanguage?.GetProviderCode(_selectedProvider) ?? "zh";
            var engine = _selectedProvider;
            var import = BuildImportSettings();

            var parameters = new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["source"] = src,
                ["target"] = tgt,
                ["engine"] = engine,
                ["enable_layout_adjust"] = EnableLayoutAdjust,
                ["oda_path"] = _odaPath ?? "",
                ["import_block_attributes"] = import.ImportBlockAttributes,
                ["import_mtext_paragraph"] = import.ImportMTextParagraph,
                ["import_mtext_whole"] = import.ImportMTextWhole,
                ["import_frozen_layers"] = import.ImportFrozenLayers,
                ["import_locked_layers"] = import.ImportLockedLayers,
                ["import_off_layers"] = import.ImportOffLayers,
                ["use_rich_excel_format"] = import.UseRichExcelFormat,
                ["enable_cleaned_dedup"] = import.EnableCleanedDedup,
                ["enable_ai_filter"] = import.EnableAiFilter,
                ["enable_glossary"] = import.EnableGlossary,
            };

            IsProcessing = true;
            ProgressValue = 10;
            StatusText = $"正在通过 MCP 桥接翻译（{engine}）：{Path.GetFileName(filePath)} ...";

            try
            {
                using var client = new McpBridgeClient(McpBridgeHost, McpBridgePort);
                var result = await client.SendAsync("translate_drawing", parameters, timeoutMs: 600000);
                if (!result.Success)
                {
                    StatusText = $"❌ MCP 翻译失败：{result.Error}";
                    return;
                }

                var data = result.Data!.Value;
                var outPath = data.TryGetProperty("output_path", out var op) && !string.IsNullOrEmpty(op.GetString())
                    ? op.GetString()!
                    : filePath;
                var total = data.TryGetProperty("entities_total", out var t) ? t.GetInt32() : 0;
                var translated = data.TryGetProperty("translated_count", out var tr) ? tr.GetInt32() : 0;

                ProgressValue = 100;
                LoadDwgFile(outPath);
                StatusText = $"✅ MCP 翻译完成：共 {total} 项，已译 {translated} 项 → 已载入 {Path.GetFileName(outPath)}";
            }
            catch (Exception ex)
            {
                StatusText = $"❌ 无法连接 MCP 桥接（{McpBridgeHost}:{McpBridgePort}），请先启动 CADTransLite.McpBridge.exe：{ex.Message}";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // MCP 桥接：集成启动（无需手动运行 CADTransLite.McpBridge.exe）
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 确保 MCP 桥接服务正在运行：若端口已监听则直接返回；否则自动定位并启动
        /// CADTransLite.McpBridge.exe，再轮询端口直至就绪（最多 15s）。
        /// </summary>
        /// <returns>true 表示桥接可用，false 表示启动失败（已写入 StatusText）。</returns>
        private async Task<bool> EnsureMcpBridgeAsync()
        {
            if (LocalServerHelper.IsPortOpen(McpBridgeHost, McpBridgePort))
                return true;

            StatusText = $"🔌 未检测到 MCP 桥接（{McpBridgeHost}:{McpBridgePort}），正在自动启动 CADTransLite.McpBridge.exe …";

            var proc = LocalServerHelper.TryStartMcpBridge(McpBridgePort);
            if (proc is null)
            {
                StatusText = "❌ 无法自动启动 MCP 桥接：未找到 CADTransLite.McpBridge.exe。" +
                             "请确认它与本程序在同一目录；或手动运行 CADTransLite.McpBridge.exe --port=8090。";
                return false;
            }

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(15))
            {
                await Task.Delay(500).ConfigureAwait(false);
                if (LocalServerHelper.IsPortOpen(McpBridgeHost, McpBridgePort))
                    return true;
                if (proc.HasExited)
                    break;
            }

            if (proc.HasExited)
            {
                var reason = LocalServerHelper.GetLastError(proc.Id);
                StatusText = $"❌ MCP 桥接进程启动后退出（退出码 {proc.ExitCode}）" +
                             (string.IsNullOrWhiteSpace(reason) ? "" : $"：{reason}");
                return false;
            }

            StatusText = $"⚠️ MCP 桥接已启动，但 {McpBridgePort} 端口 15s 内未就绪，请查看 log 目录下的日志。";
            return false;
        }

        /// <summary>
        /// 手动「启动桥接」按钮：主动拉起 MCP 桥接并报告状态。
        /// </summary>
        private async Task StartMcpBridgeAsync()
        {
            if (LocalServerHelper.IsPortOpen(McpBridgeHost, McpBridgePort))
            {
                StatusText = $"✅ MCP 桥接已在运行（{McpBridgeHost}:{McpBridgePort}）。";
                return;
            }

            var proc = LocalServerHelper.TryStartMcpBridge(McpBridgePort);
            if (proc is null)
            {
                StatusText = "❌ 无法自动启动 MCP 桥接：未找到 CADTransLite.McpBridge.exe。" +
                             "请确认它与本程序在同一目录；或手动运行 CADTransLite.McpBridge.exe --port=8090。";
                return;
            }

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(15))
            {
                await Task.Delay(500).ConfigureAwait(false);
                if (LocalServerHelper.IsPortOpen(McpBridgeHost, McpBridgePort))
                {
                    StatusText = $"✅ MCP 桥接已启动（{McpBridgeHost}:{McpBridgePort}），现在可以点「⚡ MCP 一键翻译」。";
                    return;
                }
                if (proc.HasExited)
                    break;
            }

            if (proc.HasExited)
            {
                var reason = LocalServerHelper.GetLastError(proc.Id);
                StatusText = $"❌ MCP 桥接进程启动后退出（退出码 {proc.ExitCode}）" +
                             (string.IsNullOrWhiteSpace(reason) ? "" : $"：{reason}");
            }
            else
            {
                StatusText = $"⚠️ MCP 桥接已启动，但 {McpBridgePort} 端口 15s 内未就绪，请查看 log。";
            }
        }

        private void SelectExcelFile()
        {
            var dlg = new OpenFileDialog
            {
                Title  = "选择翻译 Excel 文件",
                Filter = "Excel 文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                CurrentExcelPath = dlg.FileName;
                StatusText = $"✅ 已加载 Excel：{Path.GetFileName(dlg.FileName)}";
            }
        }

        private void BrowseTranslateExcel()
        {
            var dlg = new OpenFileDialog
            {
                Title  = "选择待翻译的 Excel 文件",
                Filter = "Excel 文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                TranslateExcelPath = dlg.FileName;
                StatusText = $"✅ 已选择翻译 Excel：{Path.GetFileName(dlg.FileName)}";
            }
        }

        private void BrowseImportExcel()
        {
            var dlg = new OpenFileDialog
            {
                Title  = "选择已翻译的 Excel 文件",
                Filter = "Excel 文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                ImportExcelPath = dlg.FileName;
                StatusText = $"✅ 已选择导入 Excel：{Path.GetFileName(dlg.FileName)}";
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Glossary commands
        // ─────────────────────────────────────────────────────────────

        private void BrowseGlossaryPath()
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择术语表 JSON 文件",
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                CheckFileExists = false,  // allow creating new
            };
            if (!string.IsNullOrEmpty(_glossaryPath))
                dlg.InitialDirectory = Path.GetDirectoryName(_glossaryPath);

            if (dlg.ShowDialog() == true)
            {
                GlossaryPath = dlg.FileName;
                LoadGlossary();
            }
        }

        private void LoadGlossary()
        {
            try
            {
                if (string.IsNullOrEmpty(_glossaryPath) || !File.Exists(_glossaryPath))
                {
                    GlossaryEntries = new ObservableCollection<GlossaryEntry>();
                    return;
                }
                var entries = GlossaryManager.LoadGlossary(_glossaryPath);
                GlossaryEntries = new ObservableCollection<GlossaryEntry>(entries);
                StatusText = $"已加载 {entries.Count} 条术语";
            }
            catch (Exception ex)
            {
                StatusText = $"⚠️ 加载术语表失败：{ex.Message}";
            }
        }

        private void AddGlossaryEntry()
        {
            GlossaryEntries.Add(new GlossaryEntry
            {
                SourceTerm = "新术语",
                TargetTerm = "目标术语",
                SourceLang = _sourceLanguage?.Code ?? "EN",
                TargetLang = _targetLanguage?.Code ?? "ZH",
            });
        }

        private void RemoveGlossaryEntry()
        {
            // Remove the last entry if any.
            // User can also delete rows directly in the DataGrid via Delete key.
            if (_glossaryEntries.Count > 0)
                _glossaryEntries.RemoveAt(_glossaryEntries.Count - 1);
        }

        private void SaveGlossary()
        {
            try
            {
                string path = _glossaryPath;
                if (string.IsNullOrEmpty(path))
                    path = GlossaryManager.GetDefaultGlossaryPath();

                GlossaryManager.SaveGlossary(_glossaryEntries.ToList(), path);
                GlossaryPath = path;
                StatusText = $"✅ 已保存 {_glossaryEntries.Count} 条术语到 {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                StatusText = $"⚠️ 保存术语表失败：{ex.Message}";
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Step 1 – Extract & Export
        // ─────────────────────────────────────────────────────────────

        private async Task ExtractAndExportAsync(CancellationToken cancellationToken)
        {
            if (_isProcessing) return;
            // If no file loaded, show open dialog
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                var dlg = new OpenFileDialog
                {
                    Title  = "选择 CAD 文件",
                    Filter = "CAD 文件 (*.dwg;*.dxf)|*.dwg;*.dxf|DWG 文件 (*.dwg)|*.dwg|DXF 文件 (*.dxf)|*.dxf",
                };
                if (dlg.ShowDialog() != true)
                    return;
                LoadDwgFile(dlg.FileName);
            }

            IsProcessing  = true;
            ProgressValue = 0;
            _extractedItems = null;

            try
            {
                string cadFile = _currentFilePath!;
                string dxfFile = cadFile;

                // ── Step A: DWG → DXF if needed ──────────────────────
                if (Path.GetExtension(cadFile).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsOdaAvailable)
                    {
                        StatusText = "❌ 处理 DWG 文件需要 ODA File Converter，请先在设置中配置路径。";
                        return;
                    }
                    StatusText    = "正在将 DWG 转换为 DXF...";
                    ProgressValue = 10;

                    string outputDir = Path.GetDirectoryName(cadFile)!;
                    var progress = MakeProgress(10, 40, "转换中");
                    dxfFile = await _odaConverter.DwgToDxfAsync(cadFile, outputDir, cancellationToken);
                    StatusText = $"✅ DWG 已转换：{Path.GetFileName(dxfFile)}";
                }

                // ── Step B: Extract text ──────────────────────────────
                StatusText    = "正在提取文本...";
                ProgressValue = 40;

                var extractProgress = MakeProgress(40, 80, "提取中");
                var importSettings = BuildImportSettings();

                (List<TranslationItem> mergedItems, int rawCount, string? loadWarning) = await Task.Run(
                    () => _extractor.ExtractAndMerge(dxfFile, importSettings, extractProgress),
                    cancellationToken);

                _extractedItems = mergedItems;
                _lastExtractedDxfPath = dxfFile;  // Cache for write-back

                if (mergedItems.Count == 0)
                {
                    // 诊断：用原始解析器统计文件中实际存在的文字实体（不依赖图层/导入设置过滤），
                    // 以区分"文件确实无文字"与"文字被导入设置或图层过滤掉"。
                    int rawTextCount = 0, rawMTextCount = 0;
                    try
                    {
                        rawTextCount  = CADTransLite.Core.Services.DxfRawParser.ParseTextEntities(dxfFile).Count;
                        rawMTextCount = CADTransLite.Core.Services.DxfRawParser.ParseMTextEntities(dxfFile).Count;
                    }
                    catch { /* 解析失败时按"无文字"处理 */ }

                    var diag = new System.Text.StringBuilder();
                    diag.Append("⚠️ 未提取到任何文本实体：");
                    if (rawTextCount + rawMTextCount == 0)
                    {
                        diag.Append("原始解析也未在该 DXF 中发现 TEXT / MTEXT 文字实体，文件可能确实没有可翻译的文字内容，或文件格式异常。");
                    }
                    else
                    {
                        diag.Append($"DXF 中实际存在 {rawTextCount + rawMTextCount} 个文字实体（TEXT {rawTextCount} / MTEXT {rawMTextCount}），但全部被当前「导入设置」过滤掉了（例如未勾选对应文字类型，或文字位于已关闭/冻结/锁定的图层）。请打开导入设置检查文字类型与图层选项。");
                    }
                    // 若 netDxf 加载失败/超时，附上真实原因，避免误导用户以为文件无文字。
                    if (!string.IsNullOrEmpty(loadWarning))
                        diag.Append(" ［诊断信息］" + loadWarning);

                    // 调试信息：揭示 UI 实际尝试读取的文件，便于区分".dxf 与 .dwg 混淆"
                    // 或"打开的不是工作区那份文件"等情况。
                    diag.Append($" ［调试］实际路径={dxfFile}；存在={System.IO.File.Exists(dxfFile)}；");
                    try
                    {
                        var fi = new System.IO.FileInfo(dxfFile);
                        diag.Append($"大小={fi.Length}字节；扩展名={fi.Extension}；");
                    }
                    catch (System.Exception ex) { diag.Append($"文件信息获取失败：{ex.Message}；"); }

                    StatusText = diag.ToString();
                    ProgressValue = 100;
                    return;
                }

                // ── Step C: Export to Excel ───────────────────────────
                StatusText    = "正在导出 Excel...";
                ProgressValue = 80;

                string excelPath = BuildExcelOutputPath(dxfFile, "_纯翻译");
                // Preserve any translations already present in a previous version of this Excel so a
                // re-extraction never overwrites the user's translated work.
                int restored = MergeExistingTranslations(excelPath, mergedItems);
                await Task.Run(
                    () => _excelHandler.Export(mergedItems, excelPath, importSettings),
                    cancellationToken);

                CurrentExcelPath = excelPath;
                ProgressValue    = 100;
                StatusText = restored > 0
                    ? $"✅ 已提取 {rawCount} 项（合并为 {mergedItems.Count} 行），已从原 Excel 保留 {restored} 条译文（未覆盖），Excel 已保存：{Path.GetFileName(excelPath)}"
                    : $"✅ 已提取 {rawCount} 项（合并为 {mergedItems.Count} 行），Excel 已保存：{Path.GetFileName(excelPath)}";
            }
            catch (OperationCanceledException)
            {
                StatusText = "⚠️ 操作已取消";
            }
            catch (Exception ex)
            {
                StatusText = $"❌ 提取失败：{ex.Message}";
                ProgressValue = 0;
            }
            finally
            {
                IsProcessing = false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Step 2 – Translate
        // ─────────────────────────────────────────────────────────────

        private async Task TranslateAsync(CancellationToken cancellationToken)
        {
            if (_isProcessing) return;
            // Check if a custom Excel path is provided
            bool useCustomExcel = !string.IsNullOrEmpty(_translateExcelPath) && File.Exists(_translateExcelPath);

            // Determine items to translate
            List<TranslationItem> itemsToTranslate;

            if (useCustomExcel)
            {
                // Load from custom Excel path (standalone — don't override _extractedItems)
                try
                {
                    StatusText = "正在从自定义 Excel 加载翻译条目...";
                    var (items, importErr) = await Task.Run(
                        () => _excelHandler.Import(_translateExcelPath!, _extractedItems ?? new List<TranslationItem>()),
                        cancellationToken);
                    if (importErr != null)
                    {
                        StatusText = $"❌ 无法读取 Excel：{importErr}";
                        return;
                    }
                    itemsToTranslate = items ?? new List<TranslationItem>();
                }
                catch (Exception ex)
                {
                    StatusText = $"❌ 无法读取 Excel：{ex.Message}";
                    return;
                }
            }
            else if (_extractedItems != null && _extractedItems.Count > 0)
            {
                // Use in-memory extracted items (with entity info)
                itemsToTranslate = _extractedItems;
            }
            else if (!string.IsNullOrEmpty(_currentExcelPath) && File.Exists(_currentExcelPath))
            {
                // Fall back to current Excel path
                try
                {
                    StatusText = "正在从 Excel 加载翻译条目...";
                    var (items, importErr) = await Task.Run(
                        () => _excelHandler.Import(_currentExcelPath, _extractedItems ?? new List<TranslationItem>()),
                        cancellationToken);
                    if (importErr != null)
                    {
                        StatusText = $"❌ 无法读取 Excel：{importErr}";
                        return;
                    }
                    itemsToTranslate = items ?? new List<TranslationItem>();
                }
                catch (Exception ex)
                {
                    StatusText = $"❌ 无法读取 Excel：{ex.Message}";
                    return;
                }
            }
            else
            {
                StatusText = "⚠️ 请先执行「提取并导出」，或指定要翻译的 Excel 文件。";
                return;
            }

            if (itemsToTranslate.Count == 0)
            {
                StatusText = "⚠️ 没有可翻译的条目。";
                return;
            }

            // Build translation API
            ITranslationApi? api;
            try
            {
                api = BuildTranslationApi();
            }
            catch (Exception ex)
            {
                StatusText = $"❌ 翻译引擎配置错误：{ex.Message}";
                return;
            }

            if (api == null)
            {
                StatusText = "⚠️ 请在设置中启用并配置至少一个翻译引擎。";
                return;
            }

            _translationService = new TranslationService(api);
            IsProcessing  = true;
            ProgressValue = 0;

            try
            {
                // Compute provider-specific language codes
                string srcLang = _sourceLanguage?.GetProviderCode(_selectedProvider) ?? _sourceLangCode;
                string tgtLang = _targetLanguage?.GetProviderCode(_selectedProvider) ?? _targetLangCode;

                // ── Phase 4: AI smart filter before translation ──
                if (EnableAiFilter && itemsToTranslate.Count > 0)
                {
                    try
                    {
                        // Reuse Custom AI API settings for filtering
                        string filterApiKey = _apiKey;
                        string filterBaseUrl = _baseUrl;
                        string filterModelName = string.IsNullOrWhiteSpace(_aiFilterModelName)
                            ? (string.IsNullOrWhiteSpace(_modelName) ? "gpt-4o-mini" : _modelName)
                            : _aiFilterModelName;

                        if (string.IsNullOrWhiteSpace(filterApiKey) || string.IsNullOrWhiteSpace(filterBaseUrl))
                        {
                            StatusText = "⚠️ AI 过滤需要自定义AI API 配置（API Key 和 Base URL），请在设置中配置。";
                        }
                        else
                        {
                            StatusText = $"正在通过 AI 过滤文本（{itemsToTranslate.Count} 条）...";
                            var filter = new AiTextFilter(filterApiKey, filterBaseUrl, filterModelName, _aiFilterPrompt);
                            string filterSrcLang = _sourceLanguage?.Code ?? _sourceLangCode;
                            string filterTgtLang = _targetLanguage?.Code ?? _targetLangCode;
                            int skippedCount = await filter.FilterAsync(
                                        itemsToTranslate, filterSrcLang, filterTgtLang,
                                        protectTableHeaders: true,
                                        progress: MakeProgress(0, 10, "AI过滤"),
                                        cancellationToken: cancellationToken);

                            // Mark SKIP items so they won't be translated
                            // Set TranslatedText to OriginalText so TranslationService skips them
                            // (TranslationService only translates items with empty TranslatedText)
                            foreach (var item in itemsToTranslate)
                            {
                                if (item.AiFilterDecision == "SKIP")
                                {
                                    item.Status = "skipped";
                                    item.TranslatedText = item.OriginalText ?? string.Empty;
                                }
                            }

                            if (skippedCount > 0)
                                StatusText = $"AI 过滤完成：{skippedCount} 条被跳过，{itemsToTranslate.Count - skippedCount} 条待翻译";
                        }
                    }
                    catch (Exception ex)
                    {
                        // AI filter failure should not block translation
                        StatusText = $"⚠️ AI 过滤失败（将继续翻译全部文本）：{ex.Message}";
                    }
                }

                // Filter items that need translation
                var toTranslate = itemsToTranslate.Where(i => string.IsNullOrWhiteSpace(i.TranslatedText)).ToList();
                StatusText = $"正在通过 {api.Name} 翻译 {toTranslate.Count} 条文本...";

                var progress = MakeProgress(10, 95, "翻译中");
                await _translationService.TranslateItemsAsync(
                    itemsToTranslate,
                    srcLang,
                    tgtLang,
                    progress,
                    cancellationToken);

                // ── Phase 4: Apply glossary after translation ──
                if (EnableGlossary && _glossaryEntries.Count > 0)
                {
                    StatusText = $"正在应用术语表（{_glossaryEntries.Count} 条术语）...";
                    string glossarySrcLang = _sourceLanguage?.Code ?? _sourceLangCode;
                    string glossaryTgtLang = _targetLanguage?.Code ?? _targetLangCode;
                    int replacedCount = GlossaryManager.ApplyGlossary(
                        itemsToTranslate, _glossaryEntries.ToList(), glossarySrcLang, glossaryTgtLang);
                    if (replacedCount > 0)
                        StatusText = $"术语表已应用：{replacedCount} 处替换";
                }

                // Auto-export translated Excel
                ProgressValue = 95;
                StatusText    = "正在导出翻译结果...";

                string excelPath;
                if (useCustomExcel)
                    excelPath = _translateExcelPath!;
                else if (!string.IsNullOrEmpty(_currentExcelPath))
                    excelPath = _currentExcelPath;
                else
                    excelPath = BuildExcelOutputPath(_currentFilePath ?? "translated", "_纯翻译");

                // 如果路径来自原始文件，改名为纯翻译版本
                if (excelPath.EndsWith("_纯翻译.xlsx", StringComparison.OrdinalIgnoreCase) == false
                    && excelPath.EndsWith("_待翻译.xlsx", StringComparison.OrdinalIgnoreCase))
                    excelPath = excelPath.Replace("_待翻译.xlsx", "_纯翻译.xlsx");

                // Preserve any translations already present in a previous version of this Excel
                // (e.g. manually edited rows the engine failed to translate) instead of dropping them.
                MergeExistingTranslations(excelPath, itemsToTranslate);
                await Task.Run(
                    () => _excelHandler.Export(itemsToTranslate, excelPath, BuildImportSettings()),
                    cancellationToken);

                CurrentExcelPath = excelPath;
                ProgressValue    = 100;
                // Only count items whose translation actually differs from the original — a text that
                // stayed identical (engine returned empty / original) must NOT be reported as translated.
                int trulyTranslated = itemsToTranslate.Count(i =>
                    !string.IsNullOrWhiteSpace(i.TranslatedText) && i.TranslatedText != i.OriginalText);
                int unchanged = itemsToTranslate.Count - trulyTranslated;
                StatusText = unchanged == 0
                    ? $"✅ 翻译完成：{trulyTranslated}/{itemsToTranslate.Count} 条，Excel 已保存：{Path.GetFileName(excelPath)}"
                    : $"⚠️ 翻译部分完成：{trulyTranslated}/{itemsToTranslate.Count} 条已翻译，{unchanged} 条未翻译（保留原文）。Excel 已保存：{Path.GetFileName(excelPath)}";
            }
            catch (OperationCanceledException)
            {
                StatusText = "⚠️ 翻译已取消";
            }
            catch (Exception ex)
            {
                StatusText    = $"❌ 翻译失败：{ex.Message}";
                ProgressValue = 0;
            }
            finally
            {
                IsProcessing = false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Step 3 – Import & Write Back
        // ─────────────────────────────────────────────────────────────

        private async Task ImportAndWriteBackAsync(CancellationToken cancellationToken)
        {
            if (_isProcessing) return;
            // Determine Excel file to use
            string? excelToImport = !string.IsNullOrEmpty(_importExcelPath) && File.Exists(_importExcelPath)
                ? _importExcelPath
                : _currentExcelPath;

            // Ensure we have an Excel file with translations
            if (string.IsNullOrEmpty(excelToImport) || !File.Exists(excelToImport))
            {
                var dlgExcel = new OpenFileDialog
                {
                    Title  = "选择翻译 Excel 文件",
                    Filter = "Excel 文件 (*.xlsx)|*.xlsx",
                };
                if (dlgExcel.ShowDialog() != true)
                    return;
                excelToImport = dlgExcel.FileName;
                CurrentExcelPath = excelToImport;
            }

            // Ensure we have a source CAD file
            if (string.IsNullOrEmpty(_currentFilePath) || !File.Exists(_currentFilePath))
            {
                var dlgCad = new OpenFileDialog
                {
                    Title  = "选择原始 CAD 文件",
                    Filter = "CAD 文件 (*.dwg;*.dxf)|*.dwg;*.dxf",
                };
                if (dlgCad.ShowDialog() != true)
                    return;
                LoadDwgFile(dlgCad.FileName);
            }

            IsProcessing  = true;
            ProgressValue = 0;

            try
            {
                string cadFile  = _currentFilePath!;
                string dxfFile;
                bool   convertedDxf = false;

                // ── Step A: Get or create DXF ──────────────────────
                if (Path.GetExtension(cadFile).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
                {
                    // Reuse cached DXF from extraction if available (handles match)
                    if (!string.IsNullOrEmpty(_lastExtractedDxfPath) && File.Exists(_lastExtractedDxfPath))
                    {
                        dxfFile = _lastExtractedDxfPath;
                        StatusText = $"使用已提取的 DXF 文件：{Path.GetFileName(dxfFile)}";
                    }
                    else
                    {
                        if (!IsOdaAvailable)
                        {
                            StatusText = "❌ 处理 DWG 文件需要 ODA File Converter，请先在设置中配置路径。";
                            return;
                        }
                        StatusText    = "正在将 DWG 转换为 DXF...";
                        ProgressValue = 10;
                        string outputDir = Path.GetDirectoryName(cadFile)!;
                        dxfFile      = await _odaConverter.DwgToDxfAsync(cadFile, outputDir, cancellationToken);
                        convertedDxf = true;
                    }
                }
                else
                {
                    dxfFile = cadFile;
                }

                // ── Step B: Read Excel translations ────────────────
                StatusText    = "正在读取 Excel 翻译数据...";
                ProgressValue = 25;

                var (importedItems, importError) = await Task.Run(
                    () => _excelHandler.Import(excelToImport!, _extractedItems ?? new List<TranslationItem>()),
                    cancellationToken);

                if (importError != null)
                {
                    StatusText    = $"❌ Excel 读取失败：{importError}";
                    ProgressValue = 0;
                    return;
                }

                var importList = importedItems ?? new List<TranslationItem>();
                if (importList.Count == 0)
                {
                    if (importError == null)
                    {
                        StatusText    = "⚠️ Excel 文件中没有读取到翻译条目。可能原因：Excel格式与当前设置不匹配（例如从11列格式切换到2列格式），或Excel文件为空。请检查Excel文件内容和格式设置。";
                    }
                    else
                    {
                        StatusText    = $"⚠️ Excel 文件中没有读取到翻译条目：{importError}";
                    }
                    ProgressValue = 100;
                    return;
                }

                // ── Merge: use _extractedItems as base, apply translations by row ──
                List<TranslationItem> items;
                if (_extractedItems != null && _extractedItems.Count > 0)
                {
                    // Use the in-memory extracted items (with handles, entity types, format placeholders)
                    // Apply translations from Excel by row index
                    int count = Math.Min(_extractedItems.Count, importList.Count);
                    for (int i = 0; i < count; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(importList[i].TranslatedText))
                        {
                            _extractedItems[i].TranslatedText = importList[i].TranslatedText;
                        }
                    }
                    items = _extractedItems;
                    StatusText = $"已从 Excel 加载 {importList.Count} 条，匹配到 {count} 条翻译";
                }
                else
                {
                    // No in-memory items — use standalone imported items directly
                    // (write-back may have limited handle matching without entity info)
                    items = importList;
                    StatusText = $"已从 Excel 加载 {importList.Count} 条（独立模式）";
                }

                // ── Phase 4: Apply glossary before write-back ──
                if (EnableGlossary && _glossaryEntries.Count > 0)
                {
                    string glossarySrcLang = _sourceLanguage?.Code ?? _sourceLangCode;
                    string glossaryTgtLang = _targetLanguage?.Code ?? _targetLangCode;
                    int replacedCount = GlossaryManager.ApplyGlossary(
                        items, _glossaryEntries.ToList(), glossarySrcLang, glossaryTgtLang);
                    if (replacedCount > 0)
                        StatusText = $"术语表已应用：{replacedCount} 处替换";
                }

                // ── Step C: Write back into DXF ───────────────────
                StatusText    = "正在回填译文到 DXF...";
                ProgressValue = 50;

                var writeProgress = MakeProgress(50, 85, "回填中");
                (string translatedDxf, List<string> log) = await Task.Run(
                    () => _dwgWriter.WriteBack(dxfFile, items, writeProgress, enableLayoutAdjust: EnableLayoutAdjust),
                    cancellationToken);

                // ── Step D: DXF → DWG if original was DWG ─────────
                string finalOutputFile = translatedDxf;
                if (convertedDxf)
                {
                    StatusText    = "正在将已翻译 DXF 转换回 DWG...";
                    ProgressValue = 88;
                    string outputDir = Path.GetDirectoryName(translatedDxf)!;
                    string? versionCode = SelectedOutputVersion?.VersionCode;
                    finalOutputFile  = await _odaConverter.DxfToDwgAsync(translatedDxf, outputDir, versionCode, cancellationToken);
                }

                ProgressValue = 100;
                int updated = log.Count(l => l.StartsWith("[OK]"));
                int skipped = log.Count(l => l.StartsWith("[SKIP]"));
                int notFound = log.Count(l => l.StartsWith("[WARN]"));
                StatusText = $"✅ 回填完成：{updated} 项已翻译回填，{skipped} 项未翻译（跳过），{notFound} 项未找到。已保存：{Path.GetFileName(finalOutputFile)}";
            }
            catch (OperationCanceledException)
            {
                StatusText = "⚠️ 操作已取消";
            }
            catch (Exception ex)
            {
                StatusText    = $"❌ 回填失败：{ex.Message}";
                ProgressValue = 0;
            }
            finally
            {
                IsProcessing = false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Test Translation API
        // ─────────────────────────────────────────────────────────────

        private async Task TestTranslationApiAsync(CancellationToken cancellationToken)
        {
            ITranslationApi? api;
            try
            {
                api = BuildTranslationApi();
            }
            catch (Exception ex)
            {
                StatusText = $"❌ 配置错误：{ex.Message}";
                return;
            }

            if (api == null)
            {
                StatusText = "⚠️ 请先启用并配置一个翻译引擎。";
                return;
            }

            IsProcessing  = true;
            ProgressValue = 0;
            StatusText    = $"正在测试 {api.Name} 连接...";

            try
            {
                ProgressValue = 50;
                string testText   = "Hello";
                string srcLang = _sourceLanguage?.GetProviderCode(_selectedProvider) ?? _sourceLangCode;
                string tgtLang = _targetLanguage?.GetProviderCode(_selectedProvider) ?? _targetLangCode;

                // For local servers, probe the port first so we can tell "service not started /
                // wrong address" apart from "service is up but still loading its language model".
                bool? portOpen = null;
                int probedPort = 0;
                if (api.Name.Contains("本地"))
                {
                    string probeUrl = _selectedProvider switch
                    {
                        "LibreTranslate (本地)" => _libreTranslateUrl,
                        "Argos Translate (本地)" => _argosUrl,
                        "NLLB (本地)" => _nllbUrl,
                        _ => _deepLXUrl,
                    };
                    if (LocalServerHelper.TryParseHostPort(probeUrl, out var probeHost, out var probePort))
                    {
                        probedPort = probePort;
                        portOpen = LocalServerHelper.IsPortOpen(probeHost, probePort);
                    }
                }

                // 各引擎首次响应耗时差异极大：
                //   · NLLB          —— 首次请求要把 600M 模型载入内存，最长 180s；
                //   · Argos / LibreTranslate —— 首次请求要加载 Argos 语言包 + MiniSBD 分句模型，
                //                        实测冷启动可达 1–2 分钟，之后每次翻译 <1s，故给 120s；
                //   · DeepLX        —— 受 DeepL 免费上游 429 限流并指数退避重试，给 30s；
                //   · 远程接口       —— 20s。
                bool isNllb  = api.Name.Contains("NLLB");
                bool isDeepLX = api.Name.Contains("DeepLX");
                bool isArgosLike = api.Name.Contains("Argos") || api.Name.Contains("LibreTranslate");
                int testTimeoutMs = isNllb ? 180000
                                   : isArgosLike ? 120000
                                   : isDeepLX ? 30000
                                   : 20000;

                var translateTask = api.TranslateAsync(testText, srcLang, tgtLang, cancellationToken);
                var timeoutTask   = Task.Delay(testTimeoutMs, cancellationToken);
                var completed     = await Task.WhenAny(translateTask, timeoutTask);

                if (completed != translateTask)
                {
                    // 超时后依然要「观察」翻译任务，否则它稍后失败会变成 UnobservedTaskException；
                    // 同时把真实错误写进日志，方便定位（UI 只提示超时不足以排查）。
                    var pendingName = api.Name;
                    _ = translateTask.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            ErrorLogger.Instance.Warn("TestApi",
                                $"{pendingName} 测试请求在超时后失败：{t.Exception?.GetBaseException().Message}");
                        else if (t.IsCompletedSuccessfully)
                            ErrorLogger.Instance.Info("TestApi",
                                $"{pendingName} 测试请求在超时后才返回：\"{t.Result}\"");
                    }, TaskScheduler.Default);

                    int sec = testTimeoutMs / 1000;
                    if (portOpen == true && isDeepLX)
                        StatusText = $"⚠️ {api.Name} 测试超时：服务已启动且端口已监听，但请求未在 {sec}s 内返回。" +
                                     "通常是因为 DeepL 免费接口对该 IP 限流（429）。请等待几分钟配额恢复，或改用自定义 AI / 官方 DeepL API。";
                    else if (portOpen == true)
                        StatusText = $"⚠️ {api.Name} 测试超时：服务已在监听端口 {probedPort}，但 {sec}s 内没有返回结果。" +
                                     "本地引擎首次翻译需要把语言模型载入内存（约 1–3 分钟，仅第一次慢）。" +
                                     "请稍候片刻再点一次「测试」，成功后每次翻译通常 1 秒内完成；若一直超时，请查看 log 目录下的日志。";
                    else if (api.Name.Contains("本地"))
                        StatusText = $"⚠️ {api.Name} 测试超时：本地服务未在监听端口 {probedPort}。" +
                                     "请先点击该引擎旁的「启动服务」按钮，待状态变为「● 运行中」后再测试。";
                    else
                        StatusText = $"⚠️ {api.Name} 测试超时：远程接口未在 {sec}s 内返回。" +
                                     "请检查 Base URL / 模型名称是否正确、网络是否可达，或该模型响应较慢（可改用更快的模型）。";
                    ProgressValue = 0;
                    return;
                }

                string result     = await translateTask;
                ProgressValue     = 100;
                StatusText        = $"✅ {api.Name} 连接正常！测试翻译：\"{testText}\" → \"{result}\"";
            }
            catch (OperationCanceledException)
            {
                StatusText = "⚠️ 测试已取消";
            }
            catch (Exception ex)
            {
                StatusText    = $"❌ {api.Name} 连接失败：{ex.Message}";
                ProgressValue = 0;
            }
            finally
            {
                IsProcessing = false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 本地服务（LibreTranslate / Argos / NLLB）启动与状态检测
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 启动指定本地翻译服务对应的嵌入式 Python 进程（tools/py 下的脚本），并轮询端口直至就绪。
        /// <paramref name="provider"/> 取值为 "LibreTranslate (本地)" / "Argos Translate (本地)" / "NLLB (本地)"。
        /// <para>
        /// 注意：这里刻意不接收 <c>CancellationToken</c>。三个「启动服务」按钮共用同一个
        /// <see cref="AsyncRelayCommand{T}"/> 实例，而 CommunityToolkit 在启动新一次执行时会取消上一次的
        /// token；若沿用该 token（例如传给 Task.Delay），用户点第二个按钮就会让第一个启动流程抛出
        /// TaskCanceledException 并冒泡成「未处理的异常：A task was canceled.」对话框。
        /// </para>
        /// </summary>
        private async Task StartLocalServiceAsync(string? provider)
        {
            LocalServiceStatus? status = provider switch
            {
                "LibreTranslate (本地)" => _libreTranslateService,
                "Argos Translate (本地)" => _argosService,
                "NLLB (本地)" => _nllbService,
                _ => null,
            };
            if (status is null || provider is null) return;

            var url = provider switch
            {
                "LibreTranslate (本地)" => _libreTranslateUrl,
                "Argos Translate (本地)" => _argosUrl,
                "NLLB (本地)" => _nllbUrl,
                _ => string.Empty,
            };
            if (!LocalServerHelper.TryParseHostPort(url, out var host, out var port))
            {
                StatusText = $"⚠️ {provider} 地址无法解析：{url}";
                return;
            }

            if (status.IsBusy)
            {
                // 重复点击时给出明确反馈，而不是静默无响应（用户会以为按钮失灵而反复点击）。
                StatusText = $"ℹ️ {provider} 正在启动中，请耐心等待（首次启动要加载语言模型，可能需要 1–3 分钟）…";
                return;
            }

            // 端口已监听：可能是本会话服务，也可能是之前残留/不健康的旧实例
            // （例如离线环境下 Argos 加载失败却仍占着端口）。先尝试停止占用该端口的进程，
            // 再重新拉起一个干净的健康实例，避免“端口被占却连不上/翻译失败”的怪象。
            if (LocalServerHelper.IsPortOpen(host, port))
            {
                LocalServerHelper.StopServerOnPort(host, port);
                await Task.Delay(800).ConfigureAwait(false);
                if (LocalServerHelper.IsPortOpen(host, port))
                {
                    status.StatusText = "● 运行中（外部进程占用，无法重启）";
                    status.StatusBrush = Brushes.Orange;
                    StatusText = $"ℹ️ {provider} 端口被其他进程占用且无法停止，将沿用现有服务。";
                    return;
                }
                // 端口已释放，继续往下走启动逻辑即可（等效于重启）。
            }

            var script = provider switch
            {
                "LibreTranslate (本地)" => "libretranslate_server.py",
                "Argos Translate (本地)" => "argos_server.py",
                "NLLB (本地)" => "nllb_server.py",
                _ => string.Empty,
            };

            status.IsBusy = true;
            status.StatusText = "◌ 启动中…";
            status.StatusBrush = Brushes.Orange;
            StatusText = $"正在启动 {provider} 本地服务…（首次加载语言模型较慢，请耐心等待）";

            try
            {
                var args = new List<string> { script, "--port", port.ToString() };
                if (provider == "LibreTranslate (本地)")
                    args.AddRange(new[] { "--host", host });

                // 复用与翻译器相同的启动逻辑（解析 tools/py/python.exe 并以隐藏窗口拉起）
                var proc = LocalServerHelper.TryStartBundledServer("python.exe", args.ToArray(), "tools/py");
                if (proc is null)
                {
                    status.StatusText = "○ 启动失败";
                    status.StatusBrush = Brushes.Red;
                    StatusText = $"❌ {provider} 启动失败：未找到嵌入式 Python（tools/py/python.exe）或进程创建被拒绝，" +
                                 "请确认安装目录完整（详见 log 目录下的日志）。";
                    return;
                }

                // 轮询端口直至就绪。LibreTranslate 启动时会加载全部 Argos 语言包，
                // NLLB 要载入 600M 模型，实测均可能超过 2 分钟，因此最长等待 240s，
                // 并在等待期间刷新耗时提示，让用户知道程序没有卡死。
                var deadline = TimeSpan.FromSeconds(240);
                var sw = Stopwatch.StartNew();
                bool started = false;
                int lastReport = -1;

                while (sw.Elapsed < deadline)
                {
                    await Task.Delay(1000).ConfigureAwait(false);

                    if (LocalServerHelper.IsPortOpen(host, port)) { started = true; break; }

                    if (proc.HasExited) break; // 进程已退出，不必再等

                    int elapsed = (int)sw.Elapsed.TotalSeconds;
                    if (elapsed / 5 != lastReport)
                    {
                        lastReport = elapsed / 5;
                        StatusText = $"正在启动 {provider} 本地服务…已等待 {elapsed}s" +
                                     "（首次启动需加载语言模型，请勿重复点击）";
                    }
                }

                if (started)
                {
                    status.StatusText = "● 运行中";
                    status.StatusBrush = Brushes.LimeGreen;

                    // 端口打开 ≠ 可以翻译：Argos/LibreTranslate/NLLB 都是在「第一次翻译请求」时才
                    // 加载语言包 / 模型（用户遇到的「端口已监听但测试超时」正是这一段）。
                    // 这里主动发一次极小的预热请求把模型载进内存，之后再点「测试」就是秒回。
                    int portOpenSec = (int)sw.Elapsed.TotalSeconds;
                    StatusText = $"{provider} 已监听端口（{portOpenSec}s），正在预热语言模型…（仅首次，需要 1–3 分钟）";
                    var (warmOk, warmSec, warmErr) = await WarmUpLocalServiceAsync(url).ConfigureAwait(false);

                    if (warmOk)
                        StatusText = $"✅ {provider} 已就绪（启动 {portOpenSec}s + 预热 {warmSec}s），现在可以直接翻译或点「测试」。";
                    else
                        StatusText = $"✅ {provider} 服务已启动（{portOpenSec}s），但预热请求未成功（{warmErr}）。" +
                                     "首次翻译可能较慢，可稍后再点「测试」。";
                }
                else if (proc.HasExited)
                {
                    // 进程直接退出：多半是缺依赖/脚本报错，把 Python 的最后一条 stderr 直接显示出来。
                    var reason = LocalServerHelper.GetLastError(proc.Id);
                    status.StatusText = "○ 启动失败";
                    status.StatusBrush = Brushes.Red;
                    StatusText = $"❌ {provider} 启动失败：Python 进程已退出（退出码 {proc.ExitCode}）。" +
                                 (string.IsNullOrWhiteSpace(reason) ? "" : $"错误：{reason} ") +
                                 $"可先手动运行 tools/py/{script} 查看详情，或执行 tools/py/setup_engines.ps1 安装依赖。";
                }
                else
                {
                    status.StatusText = "◌ 启动中…";
                    status.StatusBrush = Brushes.Orange;
                    StatusText = $"⚠️ {provider} 已启动进程，但 240s 内端口 {port} 仍未监听。" +
                                 "服务可能仍在加载模型（状态圆点会在就绪后自动变绿），也可能端口被占用，请查看 log 目录下的日志。";
                }
            }
            catch (OperationCanceledException)
            {
                // 用户切换/重复点击导致的取消：静默处理，绝不弹「未处理的异常」。
                StatusText = $"⚠️ {provider} 启动流程已取消。";
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Error("LocalServer", $"{provider} 启动失败：{ex}");
                status.StatusText = "○ 启动失败";
                status.StatusBrush = Brushes.Red;
                StatusText = $"❌ {provider} 启动失败：{ex.Message}";
            }
            finally
            {
                // 无论成功、失败还是异常，都必须解除 IsBusy，否则状态轮询会被永久卡在「启动中…」。
                status.IsBusy = false;
            }
        }

        private async Task PauseLocalServiceAsync(string? provider)
        {
            LocalServiceStatus? status = provider switch
            {
                "LibreTranslate (本地)" => _libreTranslateService,
                "Argos Translate (本地)" => _argosService,
                "NLLB (本地)" => _nllbService,
                _ => null,
            };
            if (status is null || provider is null) return;

            var url = provider switch
            {
                "LibreTranslate (本地)" => _libreTranslateUrl,
                "Argos Translate (本地)" => _argosUrl,
                "NLLB (本地)" => _nllbUrl,
                _ => string.Empty,
            };
            if (!LocalServerHelper.TryParseHostPort(url, out var host, out var port))
            {
                StatusText = $"⚠️ {provider} 地址无法解析：{url}";
                return;
            }

            status.IsBusy = true;
            status.StatusText = "◌ 停止中…";
            status.StatusBrush = Brushes.Orange;
            StatusText = $"正在停止 {provider} 本地服务…";

            // 结束任何占用该端口的进程（不限于本会话启动的），并等待其真正退出。
            LocalServerHelper.StopServerOnPort(host, port);
            await Task.Delay(800).ConfigureAwait(false);

            if (!LocalServerHelper.IsPortOpen(host, port))
            {
                status.StatusText = "○ 已停止";
                status.StatusBrush = Brushes.Gray;
                StatusText = $"✅ {provider} 已暂停（端口 {port} 已释放）。";
            }
            else
            {
                status.StatusText = "○ 停止失败";
                status.StatusBrush = Brushes.Red;
                StatusText = $"⚠️ {provider} 停止失败：端口 {port} 仍被占用，可能被其他程序使用。";
            }
            status.IsBusy = false;
        }

        /// <summary>
        /// 向本地服务发送一次极小的翻译请求，把语言包/模型提前载入内存。
        /// 三个本地引擎（LibreTranslate / Argos / NLLB）使用相同的接口约定：
        /// POST /translate { q, source, target, format } → { translatedText }。
        /// </summary>
        /// <returns>(是否成功, 耗时秒数, 失败原因)</returns>
        private static async Task<(bool ok, int seconds, string error)> WarmUpLocalServiceAsync(string baseUrl)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
                var body = new StringContent(
                    "{\"q\":\"hi\",\"source\":\"en\",\"target\":\"zh\",\"format\":\"text\"}",
                    Encoding.UTF8, "application/json");
                using var resp = await http.PostAsync($"{baseUrl.TrimEnd('/')}/translate", body).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return (false, (int)sw.Elapsed.TotalSeconds, $"HTTP {(int)resp.StatusCode}");
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                ErrorLogger.Instance.Info("LocalServer",
                    $"预热 {baseUrl} 完成，耗时 {(int)sw.Elapsed.TotalSeconds}s，响应：{json}");
                return (true, (int)sw.Elapsed.TotalSeconds, string.Empty);
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Warn("LocalServer", $"预热 {baseUrl} 失败：{ex.Message}");
                return (false, (int)sw.Elapsed.TotalSeconds, ex.Message);
            }
        }

        /// <summary>
        /// 后台定时器回调：逐一探测三个本地服务的端口并更新状态显示。
        /// 采用 AutoReset=false，每次处理完毕后在 finally 中重新计时，避免回调重叠。
        /// </summary>
        private void RefreshLocalServiceStatuses()
        {
            try
            {
                ProbeService(_libreTranslateUrl, _libreTranslateService);
                ProbeService(_argosUrl, _argosService);
                ProbeService(_nllbUrl, _nllbService);
            }
            finally
            {
                _localServiceMonitor?.Start();
            }
        }

        /// <summary>
        /// 探测单个本地服务端口是否监听，并通过 Dispatcher 将状态写回绑定对象（保证在 UI 线程更新）。
        /// </summary>
        private static void ProbeService(string url, LocalServiceStatus status)
        {
            if (status.IsBusy) return; // 启动中，不覆盖「启动中…」显示

            bool running = LocalServerHelper.TryParseHostPort(url, out var host, out var port)
                           && LocalServerHelper.IsPortOpen(host, port);

            var brush = running ? Brushes.LimeGreen : Brushes.Gray;
            var text = running ? "● 运行中" : "○ 未运行";

            if (Application.Current?.Dispatcher != null)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    status.StatusBrush = brush;
                    status.StatusText = text;
                });
            else
            {
                status.StatusBrush = brush;
                status.StatusText = text;
            }
        }

        /// <summary>
        /// 从自定义 AI 的 /models 接口拉取可用模型，并填充到「模型名称」下拉列表。
        /// </summary>
        private async Task FetchModelsAsync(CancellationToken cancellationToken)
        {
            if (!_enableCustomAI)
            {
                StatusText = "⚠️ 请先在翻译引擎中选择并启用「自定义AI」。";
                return;
            }
            if (string.IsNullOrWhiteSpace(_baseUrl))
            {
                StatusText = "⚠️ 请先填写 Base URL。";
                return;
            }

            IsFetchingModels = true;
            StatusText       = $"正在从 {_baseUrl.TrimEnd('/')}/models 获取模型列表...";

            try
            {
                var models = await CustomAiTranslator.ListModelsAsync(_apiKey, _baseUrl, cancellationToken);

                if (models.Count == 0)
                {
                    StatusText = "⚠️ 接口未返回任何模型（或返回格式不兼容）。请手动填写模型名称。";
                }
                else
                {
                    ModelNames.Clear();
                    foreach (var m in models)
                        ModelNames.Add(m);

                    if (string.IsNullOrWhiteSpace(_modelName))
                        ModelName = models[0];

                    StatusText = $"✅ 已获取 {models.Count} 个模型，可在「模型名称」下拉中选择。";
                }
            }
            catch (OperationCanceledException)
            {
                StatusText = "⚠️ 获取模型列表已取消";
            }
            catch (Exception ex)
            {
                StatusText = $"❌ 获取模型列表失败：{ex.Message}";
            }
            finally
            {
                IsFetchingModels = false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Export Translation Only (DocuTranslate-style 2-column export)
        // ─────────────────────────────────────────────────────────────

        private async Task ExportTranslationOnlyAsync(CancellationToken cancellationToken)
        {
            if (_isProcessing) return;

            if (_extractedItems == null || _extractedItems.Count == 0)
            {
                StatusText = "⚠️ 请先执行「提取导出」，获取翻译条目后再使用此功能。";
                return;
            }

            int translatedCount = _extractedItems.Count(i => !string.IsNullOrWhiteSpace(i.TranslatedText));
            if (translatedCount == 0)
            {
                StatusText = "⚠️ 当前没有已翻译的条目。请先翻译（②一键翻译 或 手动编辑Excel），再导出纯翻译对照表。";
                return;
            }

            IsProcessing  = true;
            ProgressValue = 0;

            try
            {
                string cadFile = _currentFilePath ?? "translation";
                string excelPath = BuildExcelOutputPath(cadFile, "_纯翻译对照");

                StatusText    = "正在导出纯翻译对照表...";
                ProgressValue = 30;

                // Preserve translations already present in a previous version of this file.
                MergeExistingTranslations(excelPath, _extractedItems);
                await Task.Run(
                    () => _excelHandler.ExportTranslationOnly(_extractedItems, excelPath),
                    cancellationToken);

                ProgressValue = 100;

                // Count deduped items for status
                var seen = new HashSet<string>();
                int dedupedCount = 0;
                foreach (var item in _extractedItems)
                {
                    if (string.IsNullOrWhiteSpace(item.TranslatedText)) continue;
                    string key = (item.CleanedText ?? item.OriginalText ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(key) && seen.Add(key))
                        dedupedCount++;
                }

                StatusText = $"✅ 纯翻译对照表已导出：{translatedCount} 条已翻译（去重后 {dedupedCount} 条），保存至：{Path.GetFileName(excelPath)}";
            }
            catch (OperationCanceledException)
            {
                StatusText = "⚠️ 操作已取消";
            }
            catch (ArgumentException ex)
            {
                StatusText = $"⚠️ {ex.Message}";
            }
            catch (Exception ex)
            {
                StatusText = $"❌ 导出失败：{ex.Message}";
                ProgressValue = 0;
            }
            finally
            {
                IsProcessing = false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Settings – Save / Load
        // ─────────────────────────────────────────────────────────────

        public void SaveSettings()
        {
            try
            {
                var settings = BuildUserSettings();
                _settingsManager.Save(settings);
                StatusText    = "✅ 设置已保存";
                ProgressValue = 100;
            }
            catch (Exception ex)
            {
                StatusText = $"❌ 设置保存失败：{ex.Message}";
            }
        }

        private void LoadSettings()
        {
            try
            {
                var settings = _settingsManager.Load();
                ApplyUserSettings(settings);
            }
            catch
            {
                // Silently fall back to defaults
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────

        /// <summary>Builds a UserSettings snapshot from current ViewModel state.</summary>
        private UserSettings BuildUserSettings() => new()
        {
            OdaPath            = _odaPath,
            SourceLanguageCode = _sourceLanguage?.Code ?? _sourceLangCode.ToUpperInvariant(),
            TargetLanguageCode = _targetLanguage?.Code ?? _targetLangCode.ToUpperInvariant(),
            SelectedProvider   = _selectedProvider,
            EnableLayoutAdjust = _enableLayoutAdjust,
            EnableGlossary     = _enableGlossary,
            EnableAiFilter     = _enableAiFilter,
            AiFilterPrompt     = _aiFilterPrompt,
            AiFilterModelName  = _aiFilterModelName,
            GlossaryPath       = _glossaryPath,
            OutputDwgVersion   = _selectedOutputVersion?.VersionCode ?? "ACAD2018",
            Import = new ImportSettings
            {
                ImportBlockAttributes = _importBlockAttributes,
                ImportMTextParagraph  = _importMTextParagraph,
                ImportMTextWhole      = _importMTextWhole,
                ImportFrozenLayers    = _importFrozenLayers,
                ImportLockedLayers    = _importLockedLayers,
                ImportOffLayers       = _importOffLayers,
                UseRichExcelFormat    = _useRichExcelFormat,
                EnableCleanedDedup    = _enableCleanedDedup,
            },
            TranslationApi = new TranslationApiSettings
            {
                EnableBaiduTranslate    = _enableBaiduTranslate,
                BaiduAppId              = _baiduAppId,
                BaiduAppKey             = _baiduAppKey,
                EnableTencentTranslate  = _enableTencentTranslate,
                TencentSecretId         = _tencentSecretId,
                TencentSecretKey        = _tencentSecretKey,
                EnableMicrosoftTranslate= _enableMicrosoftTranslate,
                MicrosoftApiKey         = _microsoftApiKey,
                MicrosoftRegion         = _microsoftRegion,
                EnableDeepLX            = _enableDeepLX,
                DeepLXUrl               = _deepLXUrl,
                EnableLibreTranslate    = _enableLibreTranslate,
                LibreTranslateUrl       = _libreTranslateUrl,
                EnableArgos             = _enableArgos,
                ArgosUrl                = _argosUrl,
                EnableNllb              = _enableNllb,
                NllbUrl                 = _nllbUrl,
                EnableDeepL             = _enableDeepL,
                DeepLApiKey             = _deepLApiKey,
                EnableCustomAI          = _enableCustomAI,
                ApiKey                  = _apiKey,
                BaseUrl                 = _baseUrl,
                ModelName               = _modelName,
                AiFilterPrompt          = _aiFilterPrompt,
                AiFilterModelName       = _aiFilterModelName,
            },
        };

        /// <summary>Applies a loaded UserSettings to ViewModel properties.</summary>
        private void ApplyUserSettings(UserSettings s)
        {
            _odaPath           = s.OdaPath;
            _sourceLangCode    = s.SourceLanguageCode;
            _targetLangCode    = s.TargetLanguageCode;
            _selectedProvider  = s.SelectedProvider;

            // Restore language combo selections
            _sourceLanguage = SupportedLanguages.ByCode(s.SourceLanguageCode) ?? _sourceLanguage;
            _targetLanguage = SupportedLanguages.ByCode(s.TargetLanguageCode) ?? _targetLanguage;

            var imp = s.Import;
            _importBlockAttributes = imp.ImportBlockAttributes;
            _importMTextParagraph  = imp.ImportMTextParagraph;
            _importMTextWhole      = imp.ImportMTextWhole;
            _importFrozenLayers    = imp.ImportFrozenLayers;
            _importLockedLayers    = imp.ImportLockedLayers;
            _importOffLayers       = imp.ImportOffLayers;
            _useRichExcelFormat    = imp.UseRichExcelFormat;
            _enableCleanedDedup    = imp.EnableCleanedDedup;

            var api = s.TranslationApi;
            _enableBaiduTranslate     = api.EnableBaiduTranslate;
            _baiduAppId               = api.BaiduAppId;
            _baiduAppKey              = api.BaiduAppKey;
            _enableTencentTranslate   = api.EnableTencentTranslate;
            _tencentSecretId          = api.TencentSecretId;
            _tencentSecretKey         = api.TencentSecretKey;
            _enableMicrosoftTranslate = api.EnableMicrosoftTranslate;
            _microsoftApiKey          = api.MicrosoftApiKey;
            _microsoftRegion          = api.MicrosoftRegion;
                _enableDeepLX             = api.EnableDeepLX;
                _deepLXUrl                = api.DeepLXUrl;
                _enableLibreTranslate     = api.EnableLibreTranslate;
                _libreTranslateUrl        = api.LibreTranslateUrl;
                _enableArgos              = api.EnableArgos;
                _argosUrl                 = api.ArgosUrl;
                _enableNllb               = api.EnableNllb;
                _nllbUrl                  = api.NllbUrl;
            _enableDeepL              = api.EnableDeepL;
            _deepLApiKey              = api.DeepLApiKey;
            _enableCustomAI           = api.EnableCustomAI;
            _apiKey                   = api.ApiKey;
            _baseUrl                  = api.BaseUrl;
            _modelName                = api.ModelName;

            // Phase 4 settings
            _enableLayoutAdjust  = s.EnableLayoutAdjust;
            _enableGlossary      = s.EnableGlossary;
            _enableAiFilter      = s.EnableAiFilter;
            _aiFilterPrompt      = s.AiFilterPrompt ?? string.Empty;
            _aiFilterModelName   = s.AiFilterModelName ?? string.Empty;
            _glossaryPath        = s.GlossaryPath ?? string.Empty;
            _selectedOutputVersion = OutputVersions.FirstOrDefault(v => v.VersionCode == s.OutputDwgVersion) ?? OutputVersions[0];

            _odaConverter.ExecutablePath = _odaPath;

            // Notify AI filter properties
            OnPropertyChanged(nameof(AiFilterPrompt));
            OnPropertyChanged(nameof(AiFilterModelName));

            // Auto-load glossary if path exists
            if (!string.IsNullOrEmpty(_glossaryPath) && File.Exists(_glossaryPath))
            {
                var entries = GlossaryManager.LoadGlossary(_glossaryPath);
                _glossaryEntries = new ObservableCollection<GlossaryEntry>(entries);
                OnPropertyChanged(nameof(GlossaryEntries));
            }
        }

        /// <summary>Builds an ImportSettings from current ViewModel values.</summary>
        private ImportSettings BuildImportSettings() => new()
        {
            ImportBlockAttributes = _importBlockAttributes,
            ImportMTextParagraph  = _importMTextParagraph,
            ImportMTextWhole      = _importMTextWhole,
            ImportFrozenLayers    = _importFrozenLayers,
            ImportLockedLayers    = _importLockedLayers,
            ImportOffLayers       = _importOffLayers,
            UseRichExcelFormat    = _useRichExcelFormat,
            EnableCleanedDedup    = _enableCleanedDedup,
            EnableLayoutAdjust    = _enableLayoutAdjust,
            EnableAiFilter        = _enableAiFilter,
            EnableGlossary        = _enableGlossary,
        };

        /// <summary>
        /// Builds the active <see cref="ITranslationApi"/> based on <see cref="SelectedProvider"/>.
        /// Returns null if the selected provider's credentials are not configured.
        /// </summary>
        private ITranslationApi? BuildTranslationApi()
        {
            switch (_selectedProvider)
            {
                case "自定义AI":
                    if (string.IsNullOrWhiteSpace(_apiKey))
                        throw new InvalidOperationException("自定义AI的 API Key 不能为空。");
                    if (string.IsNullOrWhiteSpace(_baseUrl))
                        throw new InvalidOperationException("自定义AI的 Base URL 不能为空。");
                    var aiSettings = new TranslationApiSettings
                    {
                        EnableCustomAI = true,
                        ApiKey         = _apiKey,
                        BaseUrl        = _baseUrl,
                        ModelName      = string.IsNullOrWhiteSpace(_modelName) ? "gpt-4o-mini" : _modelName,
                    };
                    return new CustomAiTranslator(aiSettings);

                case "百度翻译":
                    if (string.IsNullOrWhiteSpace(_baiduAppId) || string.IsNullOrWhiteSpace(_baiduAppKey))
                        throw new InvalidOperationException("百度翻译 AppId / AppKey 不能为空。");
                    return new BaiduTranslator(new TranslationApiConfig
                    {
                        AppId     = _baiduAppId,
                        SecretKey = _baiduAppKey,
                    });

                case "腾讯翻译":
                    if (string.IsNullOrWhiteSpace(_tencentSecretId) || string.IsNullOrWhiteSpace(_tencentSecretKey))
                        throw new InvalidOperationException("腾讯翻译 SecretId / SecretKey 不能为空。");
                    return new TencentTranslator(new TranslationApiConfig
                    {
                        AppId     = _tencentSecretId,
                        SecretKey = _tencentSecretKey,
                    });

                case "Microsoft Translator":
                    if (string.IsNullOrWhiteSpace(_microsoftApiKey))
                        throw new InvalidOperationException("Microsoft Translator API Key 不能为空。");
                    return new MicrosoftTranslator(new TranslationApiConfig
                    {
                        ApiKey = _microsoftApiKey,
                        Region = _microsoftRegion,
                    });

                case "DeepL":
                    if (string.IsNullOrWhiteSpace(_deepLApiKey))
                        throw new InvalidOperationException("DeepL API Key 不能为空。");
                    return new DeepLTranslator(new TranslationApiConfig { ApiKey = _deepLApiKey });

                case "DeepLX":
                    if (string.IsNullOrWhiteSpace(_deepLXUrl))
                        throw new InvalidOperationException("DeepLX URL 不能为空。");
                    return new DeepLXTranslator(new TranslationApiConfig { BaseUrl = _deepLXUrl });

                case "LibreTranslate (本地)":
                    if (string.IsNullOrWhiteSpace(_libreTranslateUrl))
                        throw new InvalidOperationException("LibreTranslate URL 不能为空。");
                    return new LibreTranslateTranslator(new TranslationApiConfig { BaseUrl = _libreTranslateUrl });

                case "Argos Translate (本地)":
                    if (string.IsNullOrWhiteSpace(_argosUrl))
                        throw new InvalidOperationException("Argos Translate URL 不能为空。");
                    return new ArgosTranslateTranslator(new TranslationApiConfig { BaseUrl = _argosUrl });

                case "NLLB (本地)":
                    if (string.IsNullOrWhiteSpace(_nllbUrl))
                        throw new InvalidOperationException("NLLB URL 不能为空。");
                    return new NllbTranslator(new TranslationApiConfig { BaseUrl = _nllbUrl });

                default:
                    return null;
            }
        }

        /// <summary>Constructs an Excel output path next to the DXF file.</summary>
        private static string BuildExcelOutputPath(string cadOrDxfPath, string suffix)
        {
            string dir  = Path.GetDirectoryName(cadOrDxfPath) ?? ".";
            string name = Path.GetFileNameWithoutExtension(cadOrDxfPath);
            return Path.Combine(dir, $"{name}{suffix}.xlsx");
        }

        /// <summary>
        /// Before (re-)exporting an Excel, carry over any existing translations from a previous
        /// version of the same file, so that re-extraction never silently destroys the user's work.
        /// The new source text is always refreshed from the CAD, but a translation that already
        /// exists for a given original text is preserved. Matching is by original text
        /// (trimmed, case-insensitive). Returns the number of translations restored.
        /// </summary>
        private int MergeExistingTranslations(string excelPath, List<TranslationItem> newItems)
        {
            if (string.IsNullOrEmpty(excelPath) || !System.IO.File.Exists(excelPath))
                return 0;
            int restored = 0;
            try
            {
                var (existing, _) = _excelHandler.Import(excelPath, new List<TranslationItem>());
                if (existing == null)
                    return 0;
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in existing)
                {
                    if (string.IsNullOrWhiteSpace(e.OriginalText) || string.IsNullOrWhiteSpace(e.TranslatedText))
                        continue;
                    // Skip entries where the "translation" is just the original text (the old
                    // fall-back that masqueraded a failed translation as a successful one).
                    if (e.TranslatedText.Trim() == e.OriginalText.Trim())
                        continue;
                    map[e.OriginalText.Trim()] = e.TranslatedText;
                }
                foreach (var n in newItems)
                {
                    if (!string.IsNullOrWhiteSpace(n.TranslatedText))
                        continue;
                    if (n.OriginalText != null && map.TryGetValue(n.OriginalText.Trim(), out var t))
                    {
                        n.TranslatedText = t;
                        restored++;
                    }
                }
            }
            catch
            {
                // If the previous file is unreadable, just proceed with the fresh export.
            }
            return restored;
        }

        /// <summary>Creates an IProgress that maps [0..100] onto [from..to] of ProgressValue.</summary>
        private IProgress<(int current, int total, string message)> MakeProgress(
            double from, double to, string prefix)
        {
            return new Progress<(int current, int total, string message)>(t =>
            {
                double fraction = t.total > 0 ? (double)t.current / t.total : 0;
                ProgressValue = from + fraction * (to - from);
                if (!string.IsNullOrEmpty(t.message))
                    StatusText = $"{prefix}：{t.message}";
            });
        }

        // ─────────────────────────────────────────────────────────────
        // INotifyPropertyChanged
        // ─────────────────────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

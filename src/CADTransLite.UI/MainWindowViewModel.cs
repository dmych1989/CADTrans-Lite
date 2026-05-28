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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
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
            CheckOdaStatus();
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
                OnPropertyChanged(nameof(CustomAISettingsVisibility));
            }
        }

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

        // DeepL
        public bool EnableDeepL     { get => _enableDeepL;     set { _enableDeepL     = value; OnPropertyChanged(); } }
        public string DeepLApiKey   { get => _deepLApiKey;     set { _deepLApiKey     = value; OnPropertyChanged(); } }

        // Custom AI
        public bool EnableCustomAI  { get => _enableCustomAI;  set { _enableCustomAI  = value; OnPropertyChanged(); } }
        public string ApiKey        { get => _apiKey;          set { _apiKey          = value; OnPropertyChanged(); } }
        public string BaseUrl       { get => _baseUrl;         set { _baseUrl         = value; OnPropertyChanged(); } }
        public string ModelName     { get => _modelName;       set { _modelName       = value; OnPropertyChanged(); } }

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
        public IReadOnlyList<LanguageInfo> SourceLanguages { get; } = SupportedLanguages.All.ToList();
        public IReadOnlyList<LanguageInfo> TargetLanguages { get; } = SupportedLanguages.All.ToList();

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
            "百度翻译", "腾讯翻译", "Microsoft Translator", "DeepL", "DeepLX", "自定义AI",
        };

        // ─────────────────────────────────────────────────────────────
        // Commands
        // ─────────────────────────────────────────────────────────────

        public IAsyncRelayCommand ExtractAndExportCommand   { get; }
        public IAsyncRelayCommand TranslateCommand          { get; }
        public IAsyncRelayCommand ImportAndWriteBackCommand { get; }
        public IAsyncRelayCommand ExportTranslationOnlyCommand { get; }
        public IAsyncRelayCommand TestTranslationApiCommand { get; }
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

                (List<TranslationItem> mergedItems, int rawCount) = await Task.Run(
                    () => _extractor.ExtractAndMerge(dxfFile, importSettings, extractProgress),
                    cancellationToken);

                _extractedItems = mergedItems;
                _lastExtractedDxfPath = dxfFile;  // Cache for write-back

                if (mergedItems.Count == 0)
                {
                    StatusText    = "⚠️ 未提取到任何文本实体，文件可能没有文字内容。";
                    ProgressValue = 100;
                    return;
                }

                // ── Step C: Export to Excel ───────────────────────────
                StatusText    = "正在导出 Excel...";
                ProgressValue = 80;

                string excelPath = BuildExcelOutputPath(dxfFile, "_纯翻译");
                await Task.Run(
                    () => _excelHandler.Export(mergedItems, excelPath, importSettings),
                    cancellationToken);

                CurrentExcelPath = excelPath;
                ProgressValue    = 100;
                StatusText = $"✅ 已提取 {rawCount} 项（合并为 {mergedItems.Count} 行），Excel 已保存：{Path.GetFileName(excelPath)}";
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

                var progress = MakeProgress(0, 95, "翻译中");
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

                await Task.Run(
                    () => _excelHandler.Export(itemsToTranslate, excelPath, BuildImportSettings()),
                    cancellationToken);

                CurrentExcelPath = excelPath;
                ProgressValue    = 100;
                int translated = itemsToTranslate.Count(i => !string.IsNullOrWhiteSpace(i.TranslatedText));
                StatusText = $"✅ 翻译完成：{translated}/{itemsToTranslate.Count} 条，Excel 已保存：{Path.GetFileName(excelPath)}";
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
                int notFound = log.Count(l => l.StartsWith("[WARN]"));
                StatusText = $"✅ 回填完成：{updated} 项已更新，{notFound} 项未找到。已保存：{Path.GetFileName(finalOutputFile)}";
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
                string result     = await api.TranslateAsync(testText, srcLang, tgtLang, cancellationToken);
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

// Controls/LanguageSelector.xaml.cs
// Code-behind for LanguageSelector.xaml.
// Provides dependency properties for SourceLanguage, TargetLanguage, and Languages.

using System.Windows;
using System.Windows.Controls;
using CADTransLite.Core.Models;

namespace CADTransLite.UI.Controls;

/// <summary>
/// A reusable control for selecting source and target languages.
/// Supports RTL languages (e.g., Arabic) by setting FlowDirection.
/// </summary>
public partial class LanguageSelector : UserControl
{
    // -----------------------------------------------------------------------
    // Dependency Properties
    // -----------------------------------------------------------------------

    /// <summary>List of available languages.</summary>
    public static readonly DependencyProperty LanguagesProperty =
        DependencyProperty.Register(
            nameof(Languages),
            typeof(System.Collections.Generic.List<LanguageInfo>),
            typeof(LanguageSelector),
            new PropertyMetadata(null));

    /// <summary>Selected source language.</summary>
    public static readonly DependencyProperty SourceLanguageProperty =
        DependencyProperty.Register(
            nameof(SourceLanguage),
            typeof(LanguageInfo),
            typeof(LanguageSelector),
            new PropertyMetadata(null));

    /// <summary>Selected target language.</summary>
    public static readonly DependencyProperty TargetLanguageProperty =
        DependencyProperty.Register(
            nameof(TargetLanguage),
            typeof(LanguageInfo),
            typeof(LanguageSelector),
            new PropertyMetadata(null));

    // -----------------------------------------------------------------------
    // Properties
    // -----------------------------------------------------------------------

    /// <summary>Gets or sets the list of available languages.</summary>
    public System.Collections.Generic.List<LanguageInfo> Languages
    {
        get => (System.Collections.Generic.List<LanguageInfo>)GetValue(LanguagesProperty);
        set => SetValue(LanguagesProperty, value);
    }

    /// <summary>Gets or sets the selected source language.</summary>
    public LanguageInfo SourceLanguage
    {
        get => (LanguageInfo)GetValue(SourceLanguageProperty);
        set => SetValue(SourceLanguageProperty, value);
    }

    /// <summary>Gets or sets the selected target language.</summary>
    public LanguageInfo TargetLanguage
    {
        get => (LanguageInfo)GetValue(TargetLanguageProperty);
        set => SetValue(TargetLanguageProperty, value);
    }

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    /// <summary>Initializes a new instance of the LanguageSelector control.</summary>
    public LanguageSelector()
    {
        InitializeComponent();
        DataContext = this;

        // 默认加载所有支持的语言
        if (Languages == null)
        {
            Languages = SupportedLanguages.All.ToList();
        }

        // 默认选择：源语言=英语，目标语言=中文
        if (SourceLanguage == null)
        {
            SourceLanguage = SupportedLanguages.ByCode("EN")!;
        }

        if (TargetLanguage == null)
        {
            TargetLanguage = SupportedLanguages.ByCode("ZH")!;
        }
    }
}

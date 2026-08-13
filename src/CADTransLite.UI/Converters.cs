// Converters.cs
// Value converters used in MainWindow.xaml.

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CADTransLite.Core.Models;

namespace CADTransLite.UI;

/// <summary>
/// Converts a non-empty string to <see cref="Visibility.Visible"/>;
/// empty/null string to <see cref="Visibility.Collapsed"/>.
/// </summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class StringToVisibilityConverter : IValueConverter
{
    /// <summary>Singleton instance for use in XAML.</summary>
    public static readonly StringToVisibilityConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts a non-empty string to <see langword="true"/>; empty/null to <see langword="false"/>.
/// </summary>
[ValueConversion(typeof(string), typeof(bool))]
public sealed class StringToBoolConverter : IValueConverter
{
    /// <summary>Singleton instance for use in XAML.</summary>
    public static readonly StringToBoolConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrEmpty(value as string);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts <see langword="true"/> to <see cref="Visibility.Visible"/>;
/// <see langword="false"/> to <see cref="Visibility.Collapsed"/>.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>Singleton instance for use in XAML.</summary>
    public static readonly BoolToVisibilityConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts a <see cref="bool"/> to its inverse. Used for binding
/// <c>IsEnabled</c> to <c>IsProcessing</c> (inverted).
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBoolConverter : IValueConverter
{
    /// <summary>Singleton instance for use in XAML.</summary>
    public static readonly InverseBoolConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;
}

/// <summary>
/// Converts a <see cref="LanguageInfo"/> to <see cref="FlowDirection"/>.
/// RTL languages (e.g., Arabic, Hebrew) → RightToLeft;
/// LTR languages (e.g., Chinese, English) → LeftToRight.
/// </summary>
[ValueConversion(typeof(LanguageInfo), typeof(FlowDirection))]
public sealed class LanguageInfoToFlowDirectionConverter : IValueConverter
{
    /// <summary>Singleton instance for use in XAML.</summary>
    public static readonly LanguageInfoToFlowDirectionConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LanguageInfo lang)
        {
            // RTL 语言：阿拉伯语、希伯来语
            return lang.Code switch
            {
                "ar" => FlowDirection.RightToLeft,  // 阿拉伯语
                "he" => FlowDirection.RightToLeft,  // 希伯来语
                "fa" => FlowDirection.RightToLeft,  // 波斯语
                "ur" => FlowDirection.RightToLeft,  // 乌尔都语
                _ => FlowDirection.LeftToRight
            };
        }

        return FlowDirection.LeftToRight;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts a color hex string (e.g., "#4CAF50") to <see cref="System.Windows.Media.SolidColorBrush"/>.
/// </summary>
[ValueConversion(typeof(string), typeof(System.Windows.Media.SolidColorBrush))]
public sealed class StringToBrushConverter : IValueConverter
{
    /// <summary>Singleton instance for use in XAML.</summary>
    public static readonly StringToBrushConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorHex && !string.IsNullOrEmpty(colorHex))
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
                return new System.Windows.Media.SolidColorBrush(color);
            }
            catch
            {
                // Fallback to default color
                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
            }
        }
        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts a boolean to visibility, inverting the logic.
/// <see langword="true"/> → <see cref="Visibility.Collapsed"/>;
/// <see langword="false"/> → <see cref="Visibility.Visible"/>.
/// Used to show "Download" button only when ODA is NOT available.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    /// <summary>Singleton instance for use in XAML.</summary>
    public static readonly InverseBoolToVisibilityConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

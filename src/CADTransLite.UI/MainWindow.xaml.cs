// MainWindow.xaml.cs
// Code-behind for MainWindow.xaml.
// Handles drag-and-drop (.dwg and .dxf) and click events that delegate to the ViewModel.

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace CADTransLite.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// </summary>
public partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    /// <summary>Initializes the window.</summary>
    public MainWindow()
    {
        InitializeComponent();
        
        // Check ODA File Converter status on startup
        Loaded += (s, e) => ViewModel.CheckOdaStatus();

        // Save settings when the window closes
        Closed += (s, e) => ViewModel.SaveSettings();
    }

    // -----------------------------------------------------------------------
    // Drag-and-Drop handlers
    // -----------------------------------------------------------------------

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (IsCadDrop(e))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!IsCadDrop(e))
            return;

        string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files is null || files.Length == 0)
            return;

        // Take only the first dropped file.
        string filePath = files[0];
        if (!File.Exists(filePath))
            return;

        string ext = Path.GetExtension(filePath).ToUpperInvariant();
        if (ext != ".DXF" && ext != ".DWG")
        {
            MessageBox.Show(
                "请拖入 .dwg 或 .dxf 格式的文件。",
                "文件类型不支持",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ViewModel.LoadDwgFile(filePath);
    }

    // -----------------------------------------------------------------------
    // Drop zone click (acts as "Select File" button)
    // -----------------------------------------------------------------------

    private void DropZone_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel.SelectDwgCommand.Execute(null);
    }

    /// <summary>
    /// Handles the RequestNavigate event for Hyperlink elements.
    /// Opens URLs in the default browser.
    /// </summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
            // Silently ignore navigation errors
        }
        e.Handled = true;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static bool IsCadDrop(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop);
}

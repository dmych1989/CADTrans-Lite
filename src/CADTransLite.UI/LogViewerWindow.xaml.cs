using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CADTransLite.Core.Services;

namespace CADTransLite.UI;

/// <summary>
/// 日志浏览窗口：列出 log 目录下的每日运行日志，可查看并一键复制内容。
/// </summary>
public partial class LogViewerWindow : Window
{
    public LogViewerWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshFiles();
    }

    private void RefreshFiles()
    {
        var dir = ErrorLogger.Instance?.LogDir;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            TxtInfo.Text = "日志目录不存在：" + dir;
            CmbFiles.ItemsSource = null;
            TxtLog.Text = string.Empty;
            return;
        }

        var files = Directory.GetFiles(dir, "run_*.log")
                             .OrderByDescending(f => f)
                             .ToList();
        CmbFiles.ItemsSource = files;

        if (files.Count > 0)
        {
            CmbFiles.SelectedIndex = 0;
            LoadFile(files[0]);
        }
        else
        {
            TxtLog.Text = "（没有找到日志文件）";
        }

        TxtInfo.Text = $"共 {files.Count} 个日志文件";
    }

    private void LoadFile(string path)
    {
        try
        {
            TxtLog.Text = File.ReadAllText(path);
            TxtLog.ScrollToHome();
        }
        catch (Exception ex)
        {
            TxtLog.Text = "读取失败：" + ex.Message;
        }
    }

    private void CmbFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbFiles.SelectedItem is string f && File.Exists(f))
            LoadFile(f);
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshFiles();

    private void BtnCopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TxtLog.Text)) return;
        try
        {
            Clipboard.SetText(TxtLog.Text);
            TxtInfo.Text = "已复制全部日志到剪贴板（" + TxtLog.Text.Length + " 字符）";
        }
        catch (Exception ex)
        {
            MessageBox.Show("复制失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnCopySel_Click(object sender, RoutedEventArgs e)
    {
        var text = TxtLog.SelectedText;
        if (string.IsNullOrEmpty(text))
        {
            TxtInfo.Text = "未选中任何文本，已复制全部";
            text = TxtLog.Text;
        }
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            TxtInfo.Text = "已复制选中日志（" + text.Length + " 字符）";
        }
        catch (Exception ex)
        {
            MessageBox.Show("复制失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

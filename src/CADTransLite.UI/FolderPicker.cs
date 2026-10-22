// FolderPicker.cs
// Minimal Win32 folder-browser dialog wrapper so the WPF app can pick a directory
// without taking a dependency on Windows Forms.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CADTransLite.UI;

/// <summary>
/// 弹出一个系统“选择文件夹”对话框（SHBrowseForFolder），返回选中目录；取消则返回 null。
/// 不依赖 System.Windows.Forms，避免与 WPF 工程产生额外的 SDK 引用。
/// </summary>
public static class FolderPicker
{
    private const uint BIF_RETURNONLYFSDIRS = 0x0001;
    private const uint BIF_NEWDIALOGSTYLE   = 0x0040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public IntPtr iImage;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// 显示文件夹选择对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="initialPath">初始目录（可为 null）。</param>
    /// <returns>选中目录；取消或失败返回 null。</returns>
    public static string? PickFolder(string title, string? initialPath = null)
    {
        var bi = new BROWSEINFO
        {
            hwndOwner = GetForegroundWindow(),
            lpszTitle = title,
            ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE,
        };

        IntPtr pidl = IntPtr.Zero;
        try
        {
            pidl = SHBrowseForFolder(ref bi);
            if (pidl == IntPtr.Zero)
                return null;

            var sb = new StringBuilder(260);
            return SHGetPathFromIDList(pidl, sb) ? sb.ToString() : null;
        }
        finally
        {
            if (pidl != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pidl);
        }
    }
}

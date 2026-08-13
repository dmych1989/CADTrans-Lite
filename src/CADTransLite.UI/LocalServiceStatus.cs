// LocalServiceStatus.cs
// 本地翻译服务（LibreTranslate / Argos / NLLB）的运行状态模型，供 WPF 绑定显示。
using System.ComponentModel;
using System.Windows.Media;

namespace CADTransLite.UI;

/// <summary>
/// 表示一个本地 Python 翻译服务的实时状态，供设置面板中的圆点 + 文字绑定使用。
/// 所有属性变更均通过 <see cref="INotifyPropertyChanged"/> 通知 WPF 绑定。
/// </summary>
public sealed class LocalServiceStatus : INotifyPropertyChanged
{
    private string _statusText = "○ 未运行";
    private Brush _statusBrush = Brushes.Gray;
    private bool _isBusy;

    /// <summary>状态文字，例如「● 运行中」「○ 未运行」「◌ 启动中…」「○ 启动失败」。</summary>
    public string StatusText
    {
        get => _statusText;
        set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
    }

    /// <summary>状态圆点颜色（Brush）。</summary>
    public Brush StatusBrush
    {
        get => _statusBrush;
        set { if (!Equals(_statusBrush, value)) { _statusBrush = value; OnPropertyChanged(); } }
    }

    /// <summary>是否正在执行启动操作（用于避免状态轮询覆盖「启动中」显示）。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

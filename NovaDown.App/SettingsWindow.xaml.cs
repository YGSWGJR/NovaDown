using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NovaDown.Core;

namespace NovaDown.App;

public partial class SettingsWindow : Window
{
    private readonly AppServices _services;

    /// <summary>保存成功后触发（主窗口用于刷新状态栏）。</summary>
    public event Action? Saved;

    public SettingsWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        Owner = Application.Current.MainWindow;
        LoadValues();
    }

    private void LoadValues()
    {
        var s = _services.Settings;
        DirBox.Text = s.DownloadDirectory;
        SegmentsBox.Text = s.MaxSegmentsPerTask.ToString();
        AutoStartCheck.IsChecked = s.AutoStartTasks;
        PortBox.Text = s.ApiPort.ToString();
        TokenBox.Text = s.ApiToken;
        TakeoverCheck.IsChecked = s.TakeoverEnabled;
        MinSizeBox.Text = (s.TakeoverMinSizeBytes / (1024.0 * 1024)).ToString("0.##");
        ExtensionsBox.Text = s.TakeoverExtensions;
        FallbackCheck.IsChecked = s.FallbackOnError;
        SpeedCombo.SelectedIndex = IndexForSpeed(s.GlobalMaxSpeedBytesPerSecond);
    }

    private static int IndexForSpeed(long bps) => bps switch
    {
        <= 0 => 0,
        <= 512 * 1024 => 1,
        <= 1024 * 1024 => 2,
        <= 2 * 1024 * 1024 => 3,
        <= 5 * 1024 * 1024 => 4,
        _ => 5,
    };

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择下载目录" };
        if (dlg.ShowDialog(this) == true) DirBox.Text = dlg.FolderName;
    }

    private void CopyToken_Click(object sender, RoutedEventArgs e)
        => Clipboard.SetText(TokenBox.Text);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _services.Settings;
        if (string.IsNullOrWhiteSpace(DirBox.Text)) { Warn("下载目录不能为空。"); return; }
        if (!int.TryParse(SegmentsBox.Text, out var segs) || segs is < 1 or > 32)
        { Warn("分段数需为 1–32。"); return; }
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1024 or > 65535)
        { Warn("端口需为 1024–65535。"); return; }
        if (!double.TryParse(MinSizeBox.Text, out var mb) || mb < 0)
        { Warn("最小接管大小需为非负数字。"); return; }

        var oldPort = s.ApiPort;
        s.DownloadDirectory = DirBox.Text.Trim();
        s.GlobalMaxSpeedBytesPerSecond = SpeedTag();
        s.MaxSegmentsPerTask = segs;
        s.AutoStartTasks = AutoStartCheck.IsChecked == true;
        s.ApiPort = port;
        s.TakeoverEnabled = TakeoverCheck.IsChecked == true;
        s.TakeoverMinSizeBytes = (long)(mb * 1024 * 1024);
        s.TakeoverExtensions = ExtensionsBox.Text.Trim();
        s.FallbackOnError = FallbackCheck.IsChecked == true;
        s.Save();

        try
        {
            _services.ApplySettings();
        }
        catch (Exception ex)
        {
            s.ApiPort = oldPort; // 回滚，避免设置与实际监听端口不一致
            s.Save();
            _services.ApplySettings();
            Warn($"API 端口切换失败（可能被占用）：{ex.Message}");
            return;
        }

        Saved?.Invoke();
        DialogResult = true;
    }

    private long SpeedTag() =>
        long.TryParse((SpeedCombo.SelectedItem as ComboBoxItem)?.Tag as string, out var v) ? v : 0;

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void Warn(string text)
        => MessageBox.Show(this, text, "NovaDown 设置", MessageBoxButton.OK, MessageBoxImage.Warning);
}

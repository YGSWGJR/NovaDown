using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NovaDown.Core;

namespace NovaDown.App;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly ObservableCollection<TaskItem> _tasks = new();
    private readonly DispatcherTimer _timer;

    public MainWindow(AppServices services)
    {
        InitializeComponent();

        _services = services;
        Tasks = _tasks;
        DataContext = this;
        Directory.CreateDirectory(services.Settings.DownloadDirectory);

        // 来自任何入口（界面 / 浏览器扩展 / Agent 插件）的任务都会显示在这里
        _services.Manager.JobAdded += job => Dispatcher.BeginInvoke(() =>
        {
            _tasks.Add(new TaskItem(job));
            UpdateButtons();
        });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            foreach (var t in _tasks) t.Tick();
            UpdateButtons();
        };
        _timer.Start();

        RefreshApiStatus();
    }

    public ObservableCollection<TaskItem> Tasks { get; }

    private TaskItem? SelectedTask => TaskGrid.SelectedItem as TaskItem;

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (url.Length == 0) return;

        AddButton.IsEnabled = false;
        try
        {
            await _services.Manager.AddAsync(url);
            UrlBox.Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"添加任务失败：{ex.Message}", "NovaDown",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            AddButton.IsEnabled = UrlBox.Text.Trim().Length > 0;
            UpdateButtons();
        }
    }

    private void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
        => AddButton.IsEnabled = UrlBox.Text.Trim().Length > 0;

    private void TaskGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateButtons();

    private void UpdateButtons()
    {
        var t = SelectedTask;
        PauseButton.IsEnabled = t?.Job.State == TaskState.Downloading;
        ResumeButton.IsEnabled = t is not null && t.Job.State is TaskState.Paused or TaskState.Error;
        OpenFolderButton.IsEnabled = t is not null;
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
        => SelectedTask?.Job.Pause();

    private void ResumeButton_Click(object sender, RoutedEventArgs e)
        => SelectedTask?.Job.Start();

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var t = SelectedTask;
        if (t is null) return;
        Process.Start("explorer.exe", $"/select,\"{t.Job.FilePath}\"");
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_services);
        win.Saved += RefreshApiStatus;
        win.ShowDialog();
    }

    private void RefreshApiStatus()
        => ApiStatusText.Text = _services.Api.IsRunning
            ? $"浏览器接管 API: http://localhost:{_services.Settings.ApiPort} 运行中 · 令牌见设置"
            : "浏览器接管 API: 未运行";
}

/// <summary>任务行视图：包装 DownloadJob，把事件转发到 UI 线程并暴露绑定属性。</summary>
public sealed class TaskItem : INotifyPropertyChanged
{
    private readonly DownloadJob _job;
    private long _lastDone;
    private long _lastTick = Environment.TickCount64;
    private double _speed;

    public TaskItem(DownloadJob job)
    {
        _job = job;
        _job.StatusChanged += OnJobChanged;
        _job.ProgressChanged += OnJobChanged;
        _job.DetailChanged += OnJobChanged;
    }

    public DownloadJob Job => _job;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>由界面计时器每秒调用一次，计算瞬时速度。</summary>
    public void Tick()
    {
        var now = Environment.TickCount64;
        var elapsedMs = now - _lastTick;
        if (elapsedMs <= 0) return;
        var done = _job.Done;
        _speed = (done - _lastDone) * 1000.0 / elapsedMs;
        _lastDone = done;
        _lastTick = now;
        OnPropertyChanged(nameof(SpeedText));
    }

    public string FileName => _job.FileName;
    public double ProgressPercent => _job.Progress * 100;
    public string ProgressText => $"{ProgressPercent:0.0}%";
    public string SizeText => $"{ByteSize.Format(_job.Done)} / {ByteSize.Format(_job.Total)}";
    public string SpeedText => _job.State == TaskState.Downloading && _speed > 0
        ? $"{ByteSize.Format((long)_speed)}/s" : "—";
    public string StateText => _job.State switch
    {
        TaskState.Preparing => "准备中",
        TaskState.Downloading => _job.Detail is null ? "下载中" : "重试中",
        TaskState.Paused => "已暂停",
        TaskState.Completed => "已完成",
        TaskState.Error => "出错",
        _ => _job.State.ToString(),
    };

    private void OnJobChanged(DownloadJob _)
        => Application.Current?.Dispatcher.BeginInvoke(RefreshAll);

    private void RefreshAll()
    {
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(SpeedText));
        OnPropertyChanged(nameof(StateText));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

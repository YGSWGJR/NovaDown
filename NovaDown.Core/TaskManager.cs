namespace NovaDown.Core;

/// <summary>
/// 任务中枢：统一管理任务的创建、查询、暂停/继续。
/// WPF 界面、控制 API（浏览器/Agent 插件）共用同一个 TaskManager 实例。
/// </summary>
public sealed class TaskManager
{
    private readonly DownloadEngine _engine;
    private readonly Dictionary<string, DownloadJob> _jobs = new();
    private readonly List<string> _order = new();
    private readonly object _lock = new();

    public TaskManager(DownloadEngine engine, string downloadDirectory, bool autoStart = true)
    {
        _engine = engine;
        DownloadDirectory = downloadDirectory;
        AutoStart = autoStart;
        Directory.CreateDirectory(downloadDirectory);
    }

    public DownloadEngine Engine => _engine;

    public string DownloadDirectory { get; private set; }

    public bool AutoStart { get; set; }

    /// <summary>新任务加入时触发（含来自 API/插件的），UI 应切线程后处理。</summary>
    public event Action<DownloadJob>? JobAdded;

    public async Task<DownloadJob> AddAsync(
        string url, IReadOnlyDictionary<string, string>? extraHeaders = null,
        bool? autoStart = null, CancellationToken ct = default)
    {
        var job = await _engine.CreateTaskAsync(url, DownloadDirectory, extraHeaders, ct).ConfigureAwait(false);

        lock (_lock)
        {
            if (_jobs.TryAdd(job.Id, job)) _order.Add(job.Id);
        }

        JobAdded?.Invoke(job);

        if (autoStart ?? AutoStart) job.Start();
        return job;
    }

    public DownloadJob? Get(string id)
    {
        lock (_lock) return _jobs.GetValueOrDefault(id);
    }

    /// <summary>按加入顺序返回全部任务。</summary>
    public IReadOnlyList<DownloadJob> Snapshot()
    {
        lock (_lock) return _order.Select(id => _jobs[id]).ToList();
    }

    public bool Pause(string id)
    {
        var job = Get(id);
        if (job is null || job.State != TaskState.Downloading) return false;
        job.Pause();
        return true;
    }

    public bool Resume(string id)
    {
        var job = Get(id);
        if (job is null || job.State is not (TaskState.Paused or TaskState.Error)) return false;
        job.Start();
        return true;
    }

    public void SetDownloadDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || directory == DownloadDirectory) return;
        Directory.CreateDirectory(directory);
        DownloadDirectory = directory;
    }
}

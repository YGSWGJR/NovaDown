using System.Net.Http;

namespace NovaDown.Core;

/// <summary>创建下载任务；内部持有共享 HttpClient，供多个任务复用连接池。</summary>
public sealed class DownloadEngine
{
    private readonly HttpClient _http;
    private readonly DownloadOptions _options;
    private readonly TaskStateStore _store;

    public DownloadEngine(DownloadOptions? options = null, TaskStateStore? store = null)
    {
        _options = options ?? new DownloadOptions();
        _store = store ?? new TaskStateStore(Path.Combine(Path.GetTempPath(), "NovaDown"));
        _http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AllowAutoRedirect = false, // 重定向由 GuardedHttp 手动跟随并逐跳校验
        })
        {
            Timeout = Timeout.InfiniteTimeSpan, // 长下载不能被整体超时掐断
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaDown/0.1");
    }

    /// <summary>全局限速器（跨任务共享），限速为 0 表示不限；可运行中调整。</summary>
    public SpeedLimiter GlobalLimiter { get; } = new();

    public DownloadOptions Options => _options;

    public TaskStateStore Store => _store;

    /// <summary>为 url 建立下载任务，保存到 directory；同名已有进度的任务会自动续传。</summary>
    public Task<DownloadJob> CreateTaskAsync(
        string url, string directory, IReadOnlyDictionary<string, string>? extraHeaders = null,
        CancellationToken ct = default)
        => DownloadJob.CreateAsync(_http, url, directory, _options, _store, GlobalLimiter, extraHeaders, ct);
}

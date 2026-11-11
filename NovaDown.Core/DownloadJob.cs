using System.Net;
using System.Net.Http.Headers;

namespace NovaDown.Core;

/// <summary>
/// 一个文件下载任务：探测 → 分段并发下载（限速 + 退避重试）→ 断点续传。
/// 用法：CreateAsync 建立任务；Start 开始；Pause 暂停；之后可再 Start 继续。
/// 所有事件都在后台线程触发，订阅方自行调度到 UI 线程。
/// </summary>
public sealed class DownloadJob
{
    private readonly HttpClient _http;
    private readonly DownloadOptions _options;
    private readonly TaskStateStore _store;
    private readonly TaskRecord _record;
    private readonly SpeedLimiter? _globalLimiter;
    private readonly SpeedLimiter _taskLimiter = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _runLoop;
    private long _done;

    /// <summary>状态变化（含一次性完成/出错）时触发。</summary>
    public event Action<DownloadJob>? StatusChanged;

    /// <summary>进度推进时触发（落盘间隔级别，非每字节）。</summary>
    public event Action<DownloadJob>? ProgressChanged;

    /// <summary>重试等运行细节变化时触发。</summary>
    public event Action<DownloadJob>? DetailChanged;

    private DownloadJob(
        HttpClient http, DownloadOptions options, TaskStateStore store,
        TaskRecord record, SpeedLimiter? globalLimiter)
    {
        _http = http;
        _options = options;
        _store = store;
        _record = record;
        _globalLimiter = globalLimiter;
        _taskLimiter.BytesPerSecond = options.MaxSpeedBytesPerSecond;
        _done = record.Segments.Sum(s => s.Downloaded);
    }

    public string Id => _record.Id;
    public string Url => _record.Url;
    public string FilePath => _record.FilePath;
    public string FileName => Path.GetFileName(_record.FilePath);
    public long Total => _record.TotalBytes;
    public long Done => Interlocked.Read(ref _done);
    public TaskState State { get; private set; } = TaskState.Preparing;
    public string? ErrorMessage { get; private set; }

    /// <summary>运行细节（如"重试 2/5，4s 后重试"），空闲时为 null。</summary>
    public string? Detail { get; private set; }

    public double Progress => Total > 0 ? Math.Min(1.0, (double)Done / Total) : 0;

    /// <summary>探测资源并建立任务；若同路径存在进度记录且服务器文件未变，则直接续传。</summary>
    public static async Task<DownloadJob> CreateAsync(
        HttpClient http, string url, string directory, DownloadOptions options, TaskStateStore store,
        SpeedLimiter? globalLimiter, IReadOnlyDictionary<string, string>? extraHeaders = null,
        CancellationToken ct = default)
    {
        var headers = extraHeaders is { Count: > 0 }
            ? new Dictionary<string, string>(extraHeaders)
            : null;

        var (total, supportsRange, etag, fileName) =
            await ProbeAsync(http, url, headers, options, ct).ConfigureAwait(false);
        if (total is null)
            throw new NotSupportedException("服务器未返回文件大小，本版本暂不支持。");

        var filePath = Path.Combine(directory, fileName);
        var existing = store.Load(filePath);
        if (existing is not null
            && existing.Url == url
            && existing.TotalBytes == total
            && (etag is null || existing.ETag is null || existing.ETag == etag))
        {
            if (headers is not null) existing.Headers = headers;
            return new DownloadJob(http, options, store, existing, globalLimiter);
        }

        var record = BuildRecord(url, filePath, total.Value, supportsRange, etag, options, headers);
        store.Save(record);
        return new DownloadJob(http, options, store, record, globalLimiter);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_runLoop is not null && !_runLoop.IsCompleted) return;
            if (State == TaskState.Completed) return;

            ErrorMessage = null;
            SetDetail(null);
            SetState(TaskState.Downloading);
            _cts = new CancellationTokenSource();
            _runLoop = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (State != TaskState.Downloading) return;
            SetState(TaskState.Paused);
            _cts?.Cancel(); // RunAsync 捕获取消后会把当前进度落盘
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            if (!_record.SupportsRange)
            {
                // 服务器不支持 Range，续传无从谈起，只能整体重来
                foreach (var s in _record.Segments) s.Downloaded = 0;
                Interlocked.Exchange(ref _done, 0);
            }

            var workers = _record.Segments
                .Where(s => s.Start + s.Downloaded <= s.End)
                .Select(s => DownloadSegmentAsync(s, ct));

            await Task.WhenAll(workers).ConfigureAwait(false);

            var totalDone = _record.Segments.Sum(s => s.Downloaded);
            if (totalDone != _record.TotalBytes)
                throw new IOException($"完整性校验失败：已下载 {totalDone} 字节，应为 {_record.TotalBytes} 字节。");

            SetDetail(null);
            SetState(TaskState.Completed);
            SaveRecord();
            ProgressChanged?.Invoke(this);
        }
        catch (OperationCanceledException)
        {
            SaveRecord(); // 保留进度，等待下一次 Start
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            SetState(TaskState.Error);
            SaveRecord();
        }
    }

    /// <summary>外层重试循环：可重试错误按退避策略再来，进度从落盘点继续。</summary>
    private async Task DownloadSegmentAsync(SegmentState seg, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                await DownloadSegmentOnceAsync(seg, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < _options.MaxRetries)
            {
                attempt++;
                var delay = GetBackoff(ex, attempt);
                SetDetail($"重试 {attempt}/{_options.MaxRetries}（{Short(ex.Message)}），{delay.TotalSeconds:0}s 后重试");
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task DownloadSegmentOnceAsync(SegmentState seg, CancellationToken ct)
    {
        var position = seg.Start + seg.Downloaded;
        if (position > seg.End) return;

        using var req = BuildRequest(position, seg.End);
        using var resp = await GuardedHttp.SendAsync(_http, req, ct).ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            // 请求了中段 Range 却拿到 200 全量响应：要么服务器不支持 Range，要么文件已变，续传不可信
            if (_record.SupportsRange && position > 0)
                throw new PermanentDownloadException("服务器返回完整文件而非分段数据（文件可能已更新），请删除任务后重新添加。");
        }
        else if (IsTransientStatus(resp.StatusCode))
        {
            throw new SegmentStatusException(resp.StatusCode, GetRetryAfter(resp));
        }
        else
        {
            resp.EnsureSuccessStatusCode();
        }

        await using var network = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = new FileStream(_record.FilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write);
        file.Seek(position, SeekOrigin.Begin);

        var buffer = new byte[_options.ChunkSize];
        var sinceSave = 0L;
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        int read;
        while (true)
        {
            // 只有无数据流动超时才判死；数据持续流动则长下载不受影响
            idleCts.CancelAfter(TimeSpan.FromSeconds(_options.IdleTimeoutSeconds));
            try
            {
                read = await network.ReadAsync(buffer.AsMemory(), idleCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IOException($"连接 {_options.IdleTimeoutSeconds} 秒无数据，读取超时。");
            }
            idleCts.CancelAfter(Timeout.InfiniteTimeSpan);

            if (read == 0) break;

            // 限速：先扣预算再落盘（全局限速 + 单任务限速串联）
            if (_globalLimiter is not null)
                await _globalLimiter.AcquireAsync(read, ct).ConfigureAwait(false);
            await _taskLimiter.AcquireAsync(read, ct).ConfigureAwait(false);

            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            seg.Downloaded += read;
            Interlocked.Add(ref _done, read);
            sinceSave += read;

            if (sinceSave >= _options.StateSaveIntervalBytes)
            {
                // 只把已刷盘的数据计为进度，保证断电后续传不缺字节
                await file.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                SaveRecord();
                sinceSave = 0;
                ProgressChanged?.Invoke(this);
            }
        }

        file.Flush(true); // 段完成前确保全部落盘
        SaveRecord();
        ProgressChanged?.Invoke(this);
    }

    private HttpRequestMessage BuildRequest(long position, long end)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, _record.Url);
        if (_record.SupportsRange)
        {
            req.Headers.Range = new RangeHeaderValue(position, end);
            if (_record.ETag is not null)
                req.Headers.IfRange = new RangeConditionHeaderValue(_record.ETag);
        }

        if (_record.Headers is { Count: > 0 })
        {
            foreach (var (key, value) in _record.Headers)
                req.Headers.TryAddWithoutValidation(key, value);
        }
        return req;
    }

    private static bool IsTransientStatus(HttpStatusCode status) => status
        is HttpStatusCode.RequestTimeout          // 408
        or HttpStatusCode.TooManyRequests         // 429
        or HttpStatusCode.InternalServerError     // 500
        or HttpStatusCode.BadGateway              // 502
        or HttpStatusCode.ServiceUnavailable      // 503
        or HttpStatusCode.GatewayTimeout;         // 504

    private static TimeSpan? GetRetryAfter(HttpResponseMessage resp)
    {
        var ra = resp.Headers.RetryAfter;
        if (ra is null) return null;
        if (ra.Delta is { } delta) return delta;
        if (ra.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            return until > TimeSpan.Zero ? until : null;
        }
        return null;
    }

    private static bool IsRetryable(Exception ex) => ex switch
    {
        PermanentDownloadException or UrlBlockedException => false,
        SegmentStatusException => true,
        HttpRequestException hre => hre.StatusCode is null
            || hre.StatusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout,
        IOException => true, // 读取超时、连接重置等
        _ => false,
    };

    private static TimeSpan GetBackoff(Exception ex, int attempt)
    {
        if (ex is SegmentStatusException { RetryAfter: { } ra } && ra > TimeSpan.Zero)
            return ra > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : ra;

        var seconds = Math.Min(60, Math.Pow(2, attempt)); // 2, 4, 8, 16, 32, 60…
        return TimeSpan.FromSeconds(seconds);
    }

    private static string Short(string text)
        => text.Length <= 60 ? text : text[..60] + "…";

    private static async Task<(long? Total, bool SupportsRange, string? ETag, string FileName)> ProbeAsync(
        HttpClient http, string url, Dictionary<string, string>? extraHeaders, DownloadOptions options,
        CancellationToken ct)
    {
        long? total = null;
        var supportsRange = false;
        string? etag = null;
        var probeTimeout = TimeSpan.FromSeconds(options.ProbeTimeoutSeconds);

        // 首选 Range: 0-0 的 GET 探测：一次拿到大小/分段支持/ETag，
        // 也最可靠（不少 CDN 会拒收甚至挂起 HEAD）
        using (var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            probeCts.CancelAfter(probeTimeout);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(0, 0);
            if (extraHeaders is not null)
                foreach (var (key, value) in extraHeaders)
                    req.Headers.TryAddWithoutValidation(key, value);

            try
            {
                using var resp = await GuardedHttp.SendAsync(http, req, probeCts.Token).ConfigureAwait(false);
                if (resp.StatusCode == HttpStatusCode.PartialContent)
                {
                    supportsRange = true;
                    total = resp.Content.Headers.ContentRange?.Length;
                    etag = resp.Headers.ETag?.Tag;
                }
                else if (resp.IsSuccessStatusCode)
                {
                    total = resp.Content.Headers.ContentLength;
                    etag = resp.Headers.ETag?.Tag;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // GET 探测超时，继续走 HEAD 兜底
            }
        }

        // GET 被拒或信息不全时，用 HEAD 兜底
        if (total is null)
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(probeTimeout);
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            if (extraHeaders is not null)
                foreach (var (key, value) in extraHeaders)
                    req.Headers.TryAddWithoutValidation(key, value);

            try
            {
                using var resp = await GuardedHttp.SendAsync(http, req, probeCts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    total = resp.Content.Headers.ContentLength;
                    supportsRange = resp.Headers.AcceptRanges.Contains("bytes");
                    etag = resp.Headers.ETag?.Tag;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IOException("探测服务器超时：无法获取文件信息，请检查网络或 URL。");
            }
        }

        return (total, supportsRange, etag, DeriveFileName(url));
    }

    private static string DeriveFileName(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = Uri.UnescapeDataString(uri.AbsolutePath);
            name = Path.GetFileName(name);
            if (!string.IsNullOrWhiteSpace(name)) return SanitizeFileName(name);
        }
        catch (UriFormatException)
        {
        }
        return "download.bin";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "download.bin" : clean.Trim();
    }

    private static TaskRecord BuildRecord(
        string url, string filePath, long total, bool supportsRange, string? etag,
        DownloadOptions options, Dictionary<string, string>? headers)
    {
        var segCount = supportsRange
            ? (int)Math.Min(options.MaxSegments, Math.Max(1, total / options.MinSegmentSize))
            : 1;

        var segSize = total / segCount;
        var segments = new List<SegmentState>(segCount);
        for (var i = 0; i < segCount; i++)
        {
            var start = i * segSize;
            var end = i == segCount - 1 ? total - 1 : start + segSize - 1;
            segments.Add(new SegmentState { Index = i, Start = start, End = end });
        }

        return new TaskRecord
        {
            Url = url,
            FilePath = filePath,
            TotalBytes = total,
            SupportsRange = supportsRange,
            ETag = etag,
            Headers = headers,
            SegmentCount = segCount,
            Segments = segments,
        };
    }

    private void SetState(TaskState state)
    {
        State = state;
        StatusChanged?.Invoke(this);
    }

    private void SetDetail(string? detail)
    {
        if (Detail == detail) return;
        Detail = detail;
        DetailChanged?.Invoke(this);
    }

    private void SaveRecord()
    {
        _record.State = State;
        try
        {
            _store.Save(_record);
        }
        catch (IOException)
        {
            // 状态文件写入失败不影响主流程
        }
    }
}

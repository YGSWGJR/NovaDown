namespace NovaDown.Core;

public sealed class DownloadOptions
{
    /// <summary>最大分段数。</summary>
    public int MaxSegments { get; init; } = 8;

    /// <summary>单段最小字节数，文件太小不拆分。</summary>
    public long MinSegmentSize { get; init; } = 1024 * 1024;

    /// <summary>每次网络读取 / 磁盘写入的缓冲大小。</summary>
    public int ChunkSize { get; init; } = 64 * 1024;

    /// <summary>每段累计写入多少字节后把进度落盘一次。</summary>
    public int StateSaveIntervalBytes { get; init; } = 512 * 1024;

    /// <summary>探测请求超时（秒）。</summary>
    public int ProbeTimeoutSeconds { get; init; } = 15;

    /// <summary>下载中连接空闲多久判定为失活（秒）。</summary>
    public int IdleTimeoutSeconds { get; init; } = 30;

    /// <summary>单段最大重试次数（429/5xx/网络错误退避重试）。</summary>
    public int MaxRetries { get; init; } = 5;

    /// <summary>单任务限速（字节/秒，0 = 不限）。</summary>
    public long MaxSpeedBytesPerSecond { get; init; } = 0;
}

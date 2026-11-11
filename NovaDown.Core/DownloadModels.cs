namespace NovaDown.Core;

public enum TaskState
{
    Preparing,
    Downloading,
    Paused,
    Completed,
    Error,
}

/// <summary>一个分段：负责文件 [Start, End] 字节区间（含两端）。</summary>
public sealed class SegmentState
{
    public int Index { get; set; }

    public long Start { get; set; }

    public long End { get; set; }

    /// <summary>本段已写入磁盘的字节数。</summary>
    public long Downloaded { get; set; }

    public long Remaining => End - Start + 1 - Downloaded;
}

/// <summary>任务的持久化记录（JSON 落盘，用于断点续传）。</summary>
public sealed class TaskRecord
{
    /// <summary>任务 ID（跨进程重启保持不变，供 API/插件追踪）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Url { get; set; } = "";

    public string FilePath { get; set; } = "";

    public long TotalBytes { get; set; }

    /// <summary>服务器是否支持 Range 分段请求。</summary>
    public bool SupportsRange { get; set; }

    /// <summary>探测时服务器返回的 ETag，续传前用于校验文件一致性。</summary>
    public string? ETag { get; set; }

    /// <summary>随任务保存的附加请求头（如 Referer，防盗链站点需要）。</summary>
    public Dictionary<string, string>? Headers { get; set; }

    public int SegmentCount { get; set; }

    public TaskState State { get; set; } = TaskState.Preparing;

    public List<SegmentState> Segments { get; set; } = new();
}

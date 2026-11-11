using System.Net;

namespace NovaDown.Core;

/// <summary>URL 未通过安全校验（协议/私网地址），重试没有意义。</summary>
public sealed class UrlBlockedException(string message) : Exception(message);

/// <summary>确定性失败（如服务器文件已更新），不应重试。</summary>
public sealed class PermanentDownloadException(string message) : Exception(message);

/// <summary>服务器返回可重试状态码（429/5xx 等），可携带 Retry-After。</summary>
public sealed class SegmentStatusException(HttpStatusCode statusCode, TimeSpan? retryAfter)
    : Exception($"服务器返回 {(int)statusCode}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}

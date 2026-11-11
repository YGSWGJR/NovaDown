using System.Net;

namespace NovaDown.Core;

/// <summary>
/// 带安全校验的请求发送：禁用 HttpClient 自动重定向，
/// 改为手动跟随并对每一跳重新做 UrlGuard 校验，防止重定向绕过内网拦截。
/// </summary>
internal static class GuardedHttp
{
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http, HttpRequestMessage initial, CancellationToken ct)
    {
        var current = initial;
        var hops = 0;

        while (true)
        {
            await UrlGuard.ValidateAsync(current.RequestUri, ct).ConfigureAwait(false);

            if (++hops > 6)
            {
                current.Dispose();
                throw new PermanentDownloadException("重定向次数过多，已中止。");
            }

            var resp = await http
                .SendAsync(current, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (IsRedirect(resp.StatusCode) && resp.Headers.Location is { } location)
            {
                var baseUri = resp.RequestMessage?.RequestUri ?? current.RequestUri!;
                var next = new Uri(baseUri, location);
                var method = resp.StatusCode == HttpStatusCode.SeeOther && current.Method == HttpMethod.Head
                    ? HttpMethod.Get
                    : current.Method;

                resp.Dispose();
                current.Dispose();
                current = CloneForRedirect(method, next, current);
                continue;
            }

            current.Dispose(); // 连接由连接池管理，请求对象可安全释放
            return resp;
        }
    }

    private static bool IsRedirect(HttpStatusCode status) => status
        is HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    private static HttpRequestMessage CloneForRedirect(HttpMethod method, Uri next, HttpRequestMessage from)
    {
        var clone = new HttpRequestMessage(method, next);
        if (from.Headers.Range is { } range) clone.Headers.Range = range;
        if (from.Headers.IfRange is { } ifRange) clone.Headers.IfRange = ifRange;

        foreach (var header in from.Headers)
        {
            if (header.Key is "Range" or "If-Range" or "Host") continue;
            foreach (var value in header.Value)
                clone.Headers.TryAddWithoutValidation(header.Key, value);
        }
        return clone;
    }
}

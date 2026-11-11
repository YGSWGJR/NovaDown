using System.Net;
using System.Net.Sockets;

namespace NovaDown.Core;

/// <summary>
/// 下载 URL 的安全校验：仅允许 http/https；拒绝 localhost、环回、
/// 私有和保留地址（含 DNS 解析结果，防止域名指向内网）。
/// </summary>
internal static class UrlGuard
{
    public static async Task ValidateAsync(Uri? uri, CancellationToken ct)
    {
        if (uri is null)
            throw new UrlBlockedException("URL 为空。");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new UrlBlockedException($"不支持的协议：{uri.Scheme}（仅允许 http/https）");

        var host = uri.IdnHost;
        if (string.IsNullOrEmpty(host))
            throw new UrlBlockedException("URL 缺少主机名。");

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            throw new UrlBlockedException("出于安全考虑，禁止下载本机（localhost）地址。");

        if (IPAddress.TryParse(host, out var literal))
        {
            if (!IsPublic(literal))
                throw new UrlBlockedException($"禁止访问私有/保留地址：{host}");
            return;
        }

        IPAddress[] resolved;
        try
        {
            resolved = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new UrlBlockedException($"域名解析失败：{host}（{ex.Message}）");
        }

        if (resolved.Length == 0)
            throw new UrlBlockedException($"域名解析失败：{host}");

        foreach (var ip in resolved)
        {
            if (!IsPublic(ip))
                throw new UrlBlockedException($"域名 {host} 解析到私有/保留地址（{ip}），已拦截。");
        }
    }

    private static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 0                          // 0.0.0.0/8
                || b[0] == 10                           // 10/8
                || b[0] == 127                          // 环回
                || (b[0] == 100 && (b[1] & 0xC0) == 64) // 100.64/10 CGNAT
                || (b[0] == 169 && b[1] == 254)         // 链路本地
                || (b[0] == 172 && (b[1] & 0xF0) == 16) // 172.16/12
                || (b[0] == 192 && b[1] == 168)         // 192.168/16
                || (b[0] == 192 && b[1] == 0 && b[2] == 2)   // TEST-NET-1
                || (b[0] == 198 && (b[1] & 0xFE) == 18)      // 198.18/15 基准测试
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)// TEST-NET-2
                || (b[0] == 203 && b[1] == 0 && b[2] == 113) // TEST-NET-3
                || b[0] >= 224);                        // 组播 + 保留
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return false;
            if (IPAddress.IPv6Loopback.Equals(ip) || IPAddress.IPv6Any.Equals(ip)) return false;

            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;          // fc00::/7 ULA
            if (b[0] == 0xff) return false;                   // 组播
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) return false; // 文档段
            if (b.All(x => x == 0)) return false;             // ::
            return true;
        }

        return false;
    }
}

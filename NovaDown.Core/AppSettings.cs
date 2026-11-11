using System.Text.Json;

namespace NovaDown.Core;

/// <summary>
/// 应用设置，JSON 持久化于 %APPDATA%\NovaDown\settings.json。
/// 其中接管相关配置会通过 /api/extension-config 下发给浏览器扩展，
/// 实现"在桌面端软件里对插件进行设置"。
/// </summary>
public sealed class AppSettings
{
    public string DownloadDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "NovaDown");

    public int MaxSegmentsPerTask { get; set; } = 8;

    /// <summary>全局限速（字节/秒，0 = 不限）。</summary>
    public long GlobalMaxSpeedBytesPerSecond { get; set; } = 0;

    public bool AutoStartTasks { get; set; } = true;

    public int ApiPort { get; set; } = 28123;

    /// <summary>本机 API 访问令牌，首次创建设置时自动生成。</summary>
    public string ApiToken { get; set; } = Guid.NewGuid().ToString("N");

    // ── 以下为浏览器扩展下发配置 ──

    public bool TakeoverEnabled { get; set; } = true;

    /// <summary>小于该字节数的文件不接管（0 = 不限）。</summary>
    public long TakeoverMinSizeBytes { get; set; } = 1024 * 1024;

    /// <summary>逗号分隔的扩展名列表，命中即接管（无论大小）。</summary>
    public string TakeoverExtensions { get; set; } = ".exe,.zip,.rar,.7z,.iso,.msi,.dmg,.apk,.xz,.gz,.mp4,.mkv,.torrent";

    /// <summary>NovaDown 下载出错时，扩展回退为浏览器普通下载。</summary>
    public bool FallbackOnError { get; set; } = true;

    private static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NovaDown");

    public static string FilePath => Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using var fs = File.OpenRead(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(fs) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // 配置损坏时回退默认值
        }
        var fresh = new AppSettings();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        using var fs = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(fs, this, new JsonSerializerOptions { WriteIndented = true });
    }

    public DownloadOptions ToOptions() => new() { MaxSegments = Math.Clamp(MaxSegmentsPerTask, 1, 32) };

    /// <summary>解析扩展名过滤列表（小写、去空、带点）。</summary>
    public string[] ExtensionList() => TakeoverExtensions
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
        .Where(e => e.Length > 1)
        .ToArray();
}

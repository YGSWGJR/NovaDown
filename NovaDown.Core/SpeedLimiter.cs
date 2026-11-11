namespace NovaDown.Core;

/// <summary>
/// 令牌桶限速器。BytesPerSecond 为 0 表示不限速；
/// 速率可在运行中随时调整（设置界面实时生效）。
/// </summary>
public sealed class SpeedLimiter
{
    private long _rate;
    private long _tokens;
    private long _lastRefillTicks;
    private readonly object _lock = new();

    public long BytesPerSecond
    {
        get => Interlocked.Read(ref _rate);
        set => Interlocked.Exchange(ref _rate, Math.Max(0, value));
    }

    /// <summary>获取 bytes 字节的发送预算；预算不足时异步等待补充。</summary>
    public async ValueTask AcquireAsync(int bytes, CancellationToken ct)
    {
        var rate = BytesPerSecond;
        if (rate <= 0 || bytes <= 0) return;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            long waitMs;
            lock (_lock)
            {
                RefillLocked(rate);
                if (_tokens >= bytes)
                {
                    _tokens -= bytes;
                    return;
                }
                waitMs = Math.Max(1, (bytes - _tokens) * 1000 / rate);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(waitMs, 1000)), ct).ConfigureAwait(false);
        }
    }

    private void RefillLocked(long rate)
    {
        var now = Environment.TickCount64;
        var elapsedMs = _lastRefillTicks == 0 ? 0 : now - _lastRefillTicks;
        _lastRefillTicks = now;
        if (elapsedMs > 0)
            _tokens = Math.Min(rate, _tokens + rate * elapsedMs / 1000);
        if (_tokens < 0) _tokens = 0; // 限速调小时防止出现负余额
    }
}

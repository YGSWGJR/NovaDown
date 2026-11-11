using NovaDown.Core;

// serve 模式：无界面运行控制 API（浏览器扩展 / Agent 插件的后端）
if (args.Length >= 1 && args[0] == "serve")
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    var settings = AppSettings.Load();
    var engine = new DownloadEngine(settings.ToOptions());
    engine.GlobalLimiter.BytesPerSecond = settings.GlobalMaxSpeedBytesPerSecond;
    var manager = new TaskManager(engine, settings.DownloadDirectory, settings.AutoStartTasks);
    var api = new ApiServer(manager, settings);
    try
    {
        api.Start();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"API 启动失败：{ex.Message}");
        return 1;
    }
    Console.WriteLine("NovaDown 无界面服务已启动");
    Console.WriteLine($"API:    http://localhost:{settings.ApiPort}/api");
    Console.WriteLine($"令牌:   {settings.ApiToken}");
    Console.WriteLine($"下载到: {settings.DownloadDirectory}");
    Console.WriteLine("Ctrl+C 退出。");
    await Task.Delay(Timeout.Infinite);
    return 0;
}

if (args.Length < 1)
{
    Console.WriteLine("用法:");
    Console.WriteLine("  NovaDown.Cli serve   无界面运行控制 API（供浏览器/Agent 插件调用）");
    Console.WriteLine("  NovaDown.Cli <url> [目录] [分段数] [--limit 字节/秒]");
    return 1;
}

Console.OutputEncoding = System.Text.Encoding.UTF8;

// 解析可选参数 --limit（单任务限速，字节/秒）
long? limit = null;
var rest = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--limit" && i + 1 < args.Length && long.TryParse(args[++i], out var l))
        limit = l;
    else
        rest.Add(args[i]);
}

var url = rest[0];
var dir = rest.Count > 1 ? rest[1] : Directory.GetCurrentDirectory();
var segments = rest.Count > 2 && int.TryParse(rest[2], out var segs) && segs > 0 ? segs : 8;
var options = new DownloadOptions
{
    MaxSegments = segments,
    MaxSpeedBytesPerSecond = limit ?? 0,
};

try
{
    var engine = new DownloadEngine(options);
    var job = await engine.CreateTaskAsync(url, dir);

    Console.WriteLine($"文件: {job.FileName}");
    Console.WriteLine($"大小: {ByteSize.Format(job.Total)}  分段并发: {options.MaxSegments}  限速: {(limit is > 0 ? ByteSize.Format(limit.Value) + "/s" : "不限")}");
    Console.WriteLine("中断后重跑同一命令即可断点续传。");
    Console.WriteLine();

    var lastTick = Environment.TickCount64;
    long lastDone = 0;
    job.ProgressChanged += _ =>
    {
        var now = Environment.TickCount64;
        var elapsed = now - Interlocked.Read(ref lastTick);
        if (elapsed < 500) return;
        var speed = (job.Done - Interlocked.Read(ref lastDone)) * 1000.0 / elapsed;
        Interlocked.Exchange(ref lastDone, job.Done);
        Interlocked.Exchange(ref lastTick, now);
        Render(job, speed);
    };

    job.Start();

    var finished = new TaskCompletionSource();
    job.StatusChanged += j =>
    {
        if (j.State is TaskState.Completed or TaskState.Error or TaskState.Paused)
            finished.TrySetResult();
    };
    await finished.Task;

    Render(job, 0);
    Console.WriteLine();
    Console.WriteLine(job.State switch
    {
        TaskState.Completed => "下载完成。",
        TaskState.Paused => "已暂停，重跑同一命令可继续。",
        _ => $"失败：{job.ErrorMessage}",
    });
    return job.State == TaskState.Completed ? 0 : 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"出错：{ex.Message}");
    return 1;
}

static void Render(DownloadJob job, double speed)
{
    const int width = 28;
    var filled = (int)(job.Progress * width);
    var bar = new string('█', filled) + new string('░', width - filled);
    var detail = job.Detail is null ? "" : $"  [{job.Detail}]";
    Console.Write($"\r[{bar}] {job.Progress,6:P1}  {ByteSize.Format(job.Done)}/{ByteSize.Format(job.Total)}  {ByteSize.Format((long)speed)}/s{detail}   ");
}

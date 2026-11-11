using NovaDown.Core;

namespace NovaDown.App;

/// <summary>应用级服务容器：设置 / 引擎 / 任务中枢 / 控制 API。</summary>
public sealed class AppServices : IDisposable
{
    private AppServices(AppSettings settings, DownloadEngine engine, TaskManager manager, ApiServer api)
    {
        Settings = settings;
        Engine = engine;
        Manager = manager;
        Api = api;
    }

    public AppSettings Settings { get; }

    public DownloadEngine Engine { get; }

    public TaskManager Manager { get; }

    public ApiServer Api { get; }

    public static AppServices Create()
    {
        var settings = AppSettings.Load();
        var engine = new DownloadEngine(settings.ToOptions());
        engine.GlobalLimiter.BytesPerSecond = settings.GlobalMaxSpeedBytesPerSecond;
        var manager = new TaskManager(engine, settings.DownloadDirectory, settings.AutoStartTasks);
        var api = new ApiServer(manager, settings);
        return new AppServices(settings, engine, manager, api);
    }

    /// <summary>设置修改后调用：限速与下载目录即时生效，端口变化时重启 API。</summary>
    public void ApplySettings()
    {
        Engine.GlobalLimiter.BytesPerSecond = Settings.GlobalMaxSpeedBytesPerSecond;
        Manager.SetDownloadDirectory(Settings.DownloadDirectory);
        Manager.AutoStart = Settings.AutoStartTasks;

        if (Api.IsRunning && Api.BoundPort != Settings.ApiPort)
        {
            Api.Stop();
            Api.Start(); // 重新按新端口建立监听
        }
    }

    public void Dispose() => Api.Stop();
}

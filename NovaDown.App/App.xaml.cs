using System.Windows;
using NovaDown.Core;

namespace NovaDown.App;

public partial class App : Application
{
    public AppServices? Services { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Services = AppServices.Create();
        try
        {
            Services.Api.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"浏览器接管 API 启动失败（端口被占用？可在设置中更换）：{ex.Message}",
                "NovaDown", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        new MainWindow(Services).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services?.Dispose();
        base.OnExit(e);
    }
}

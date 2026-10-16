using Microsoft.UI.Xaml;

namespace TicketSplitter.App;

public partial class App : Application
{
    /// <summary>主窗口引用（步骤控件里选文件夹需要窗口句柄）。</summary>
    public static Window? Host { get; private set; }

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        Host = window;
        window.Activate();
    }
}

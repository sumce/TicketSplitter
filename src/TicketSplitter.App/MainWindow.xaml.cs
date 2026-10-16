using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TicketSplitter.App.ViewModels;
using Windows.Graphics;

namespace TicketSplitter.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        // 必须先建 ViewModel，x:Bind 在 InitializeComponent 期间就会读取它
        ViewModel = new MainViewModel(this);

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ApplyBackdrop();

        // 尺寸必须在窗口真正激活之后再设，否则会被 WinUI 的创建流程覆盖
        Activated += OnFirstActivated;

        Closed += (_, _) => ViewModel.Shutdown();
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        try { AppWindow.Resize(new SizeInt32(1500, 960)); }
        catch (Exception) { }
    }

    private void ApplyBackdrop()
    {
        // Mica 只有 Win11 支持；不支持时退回亚克力，再不行就保持默认
        try { SystemBackdrop = new MicaBackdrop(); return; }
        catch (Exception) { }

        try { SystemBackdrop = new DesktopAcrylicBackdrop(); }
        catch (Exception) { }
    }
}

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
        // ViewModel 必须先建：x:Bind 在 InitializeComponent 期间就会读取它
        ViewModel = new MainViewModel(this);

        InitializeComponent();

        // 各步骤是 UserControl，用经典 {Binding}，靠 DataContext 继承拿到 ViewModel
        RootGrid.DataContext = ViewModel;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyBackdrop();

        Activated += OnFirstActivated;
        Closed += (_, _) => ViewModel.Shutdown();
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        try { AppWindow.Resize(new SizeInt32(1520, 980)); }
        catch (Exception) { }
    }

    private void ApplyBackdrop()
    {
        try { SystemBackdrop = new MicaBackdrop(); return; } catch (Exception) { }
        try { SystemBackdrop = new DesktopAcrylicBackdrop(); } catch (Exception) { }
    }
}

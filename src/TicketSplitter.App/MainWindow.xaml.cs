using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TicketSplitter.App.Services;
using TicketSplitter.App.ViewModels;
using Windows.Graphics;

namespace TicketSplitter.App;

public sealed partial class MainWindow : Window
{
    private readonly nint _hwnd;
    private bool _syncingSelection;

    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        // ViewModel 必须先建：x:Bind 在 InitializeComponent 期间就会读取它
        ViewModel = new MainViewModel(this);
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyBackdrop();

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
        try { SystemBackdrop = new MicaBackdrop(); return; } catch (Exception) { }
        try { SystemBackdrop = new DesktopAcrylicBackdrop(); } catch (Exception) { }
    }

    // ================= 页面 <-> 票 双向联动 =================

    private void OnTicketsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || ViewModel.SelectedTicket is not { } ticket) return;

        _syncingSelection = true;
        try
        {
            var page = ViewModel.Pages.FirstOrDefault(p => p.PageIndex == ticket.StartPageIndex);
            if (page is not null)
            {
                ViewModel.SelectedPage = page;
                PagesList.ScrollIntoView(page);
            }
        }
        finally { _syncingSelection = false; }
    }

    private void OnPagesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || ViewModel.SelectedPage is not { } page) return;

        _syncingSelection = true;
        try
        {
            var ticket = ViewModel.Tickets.FirstOrDefault(
                t => page.PageIndex >= t.StartPageIndex && page.PageIndex < t.EndPageIndex);
            if (ticket is not null)
            {
                ViewModel.SelectedTicket = ticket;
                TicketsList.ScrollIntoView(ticket);
            }
        }
        finally { _syncingSelection = false; }
    }

    // ================= 对话框 =================

    private async void OnTokenClick(object sender, RoutedEventArgs e)
    {
        var box = new PasswordBox
        {
            Header = "MinerU Token",
            Password = ViewModel.Token,
            PlaceholderText = "粘贴 Token，或使用环境变量 MINERU_TOKEN",
        };
        var remember = new CheckBox
        {
            Content = "记住 Token（DPAPI 加密保存在本机）",
            IsChecked = ViewModel.RememberToken,
        };
        var reuse = new CheckBox
        {
            Content = "复用已有解析结果（不联网，跳过 MinerU 调用）",
            IsChecked = ViewModel.ReuseExistingParse,
            IsEnabled = ViewModel.CanReuseExisting,
        };
        var reuseHint = new TextBlock
        {
            Text = ViewModel.CanReuseExisting
                ? "本机已有该文件的解析结果，可直接复用。"
                : "本机还没有该文件的解析结果，需联网调用 MinerU。",
            FontSize = 12,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
        };
        var panel = new StackPanel { Spacing = 12, Width = 440 };
        panel.Children.Add(box);
        panel.Children.Add(remember);
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
            Margin = new Thickness(0, 4, 0, 4),
        });
        panel.Children.Add(reuseHint);
        panel.Children.Add(reuse);

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "解析设置",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        ViewModel.Token = box.Password;
        ViewModel.RememberToken = remember.IsChecked == true;
        ViewModel.ReuseExistingParse = reuse.IsChecked == true;
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasTickets) return;

        var summary = new TextBlock
        {
            Text = ViewModel.TicketSummary,
            Opacity = 0.75,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };

        var dirBox = new TextBox { Text = ViewModel.OutputDir, Header = "输出目录" };
        var browse = new Button { Content = "浏览…", VerticalAlignment = VerticalAlignment.Bottom };
        browse.Click += async (_, _) =>
        {
            var picked = await PickerHelper.PickFolderAsync(_hwnd);
            if (picked is not null) dirBox.Text = picked;
        };

        var dirRow = new Grid { ColumnSpacing = 8 };
        dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(dirBox, 0);
        Grid.SetColumn(browse, 1);
        dirRow.Children.Add(dirBox);
        dirRow.Children.Add(browse);

        var clean = new CheckBox
        {
            Content = "导出前清空上次的票类型目录",
            IsChecked = ViewModel.CleanOutput,
        };

        var panel = new StackPanel { Spacing = 12, Width = 480 };
        panel.Children.Add(summary);
        panel.Children.Add(dirRow);
        panel.Children.Add(clean);

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "导出单票 + 清单 CSV",
            Content = panel,
            PrimaryButtonText = "开始导出",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        ViewModel.OutputDir = dirBox.Text;
        ViewModel.CleanOutput = clean.IsChecked == true;
        await ViewModel.ExportCommand.ExecuteAsync(null);
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TicketSplitter.App.Services;
using TicketSplitter.App.ViewModels;

namespace TicketSplitter.App.Steps;

public sealed partial class ReviewStep : UserControl
{
    private bool _syncing;

    public ReviewStep()
    {
        InitializeComponent();

        // 票列表在步骤隐藏期间被填充，显示时容易停在末尾，这里滚回第一张
        Loaded += (_, _) =>
        {
            if (Vm?.Tickets.Count > 0) TicketsList.ScrollIntoView(Vm.Tickets[0]);
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    // ================= 票 <-> 页 双向联动 =================

    private void OnTicketsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || Vm?.SelectedTicket is not { } ticket) return;

        _syncing = true;
        try
        {
            var page = Vm.Pages.FirstOrDefault(p => p.PageIndex == ticket.StartPageIndex);
            if (page is not null)
            {
                Vm.SelectedPage = page;
                PagesList.ScrollIntoView(page);
            }
        }
        finally { _syncing = false; }
    }

    private void OnPagesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || Vm?.SelectedPage is not { } page) return;

        _syncing = true;
        try
        {
            var ticket = Vm.Tickets.FirstOrDefault(
                t => page.PageIndex >= t.StartPageIndex && page.PageIndex < t.EndPageIndex);
            if (ticket is not null)
            {
                Vm.SelectedTicket = ticket;
                TicketsList.ScrollIntoView(ticket);
            }
        }
        finally { _syncing = false; }
    }

    // ================= 导出 =================

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (Vm is null || !Vm.HasTickets || XamlRoot is null) return;

        var summary = new TextBlock
        {
            Text = Vm.TicketSummary,
            Opacity = 0.75,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };

        var dirBox = new TextBox { Text = Vm.OutputDir, Header = "输出目录" };
        var browse = new Button { Content = "浏览…", VerticalAlignment = VerticalAlignment.Bottom };
        browse.Click += async (_, _) =>
        {
            var picked = await PickerHelper.PickFolderAsync(
                WinRT.Interop.WindowNative.GetWindowHandle(App.Host!));
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
            IsChecked = Vm.CleanOutput,
        };

        var twoUp = new CheckBox
        {
            Content = "同时生成双页合并版（每张票左右并排，A4 横版）",
            IsChecked = Vm.AlsoTwoUp,
        };
        var twoUpHint = new TextBlock
        {
            Text = "输出到「双页合并」子目录，一张票按左右两张排一面，奇数页的最后一面右半留空。",
            FontSize = 12,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(28, -6, 0, 0),
        };

        var panel = new StackPanel { Spacing = 12, Width = 500 };
        panel.Children.Add(summary);
        panel.Children.Add(dirRow);
        panel.Children.Add(clean);
        panel.Children.Add(twoUp);
        panel.Children.Add(twoUpHint);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "导出单票 + 清单 CSV",
            Content = panel,
            PrimaryButtonText = "开始导出",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        Vm.OutputDir = dirBox.Text;
        Vm.CleanOutput = clean.IsChecked == true;
        Vm.AlsoTwoUp = twoUp.IsChecked == true;
        await Vm.ExportCommand.ExecuteAsync(null);
    }
}
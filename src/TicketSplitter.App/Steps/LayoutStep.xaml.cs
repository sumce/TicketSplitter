using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TicketSplitter.App.ViewModels;
using TicketSplitter.Core.Pdf;

namespace TicketSplitter.App.Steps;

public sealed partial class LayoutStep : UserControl
{
    public LayoutStep() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnStyleTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is FrameworkElement { Tag: LayoutStyleViewModel style }) Vm.SelectedStyle = style;
    }

    private async void OnAddStyleClick(object sender, RoutedEventArgs e)
    {
        if (Vm is null || XamlRoot is null) return;

        var name = new TextBox { Header = "样式名称", PlaceholderText = "例如：一面两页 · 左右并排（A3）" };
        var desc = new TextBox
        {
            Header = "说明（可选）",
            PlaceholderText = "这个样式适用于什么样的扫描件",
            TextWrapping = TextWrapping.Wrap,
        };
        var mode = new ComboBox { Header = "切开方式", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.Items.Add("不切开（每页就是一张票的一页）");
        mode.Items.Add("按中线拆成左右两页");
        mode.Items.Add("按中线拆成上下两页");

        var panel = new StackPanel { Spacing = 12, Width = 420 };
        panel.Children.Add(name);
        panel.Children.Add(desc);
        panel.Children.Add(mode);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "添加样式",
            Content = panel,
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(name.Text))
        {
            Vm.StatusText = "样式名称不能为空";
            return;
        }

        var splitMode = mode.SelectedIndex switch
        {
            1 => SheetSplitMode.LeftRight,
            2 => SheetSplitMode.TopBottom,
            _ => SheetSplitMode.None,
        };

        Vm.AddCustomStyle(name.Text, string.IsNullOrWhiteSpace(desc.Text) ? "自定义样式" : desc.Text, splitMode);
    }

    private void OnDeleteStyleClick(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is Button { Tag: LayoutStyleViewModel style }) Vm.RemoveCustomStyle(style);
    }
}

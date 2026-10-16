using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TicketSplitter.App.ViewModels;

namespace TicketSplitter.App.Steps;

public sealed partial class SubmitStep : UserControl
{
    public SubmitStep() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "松开以提交 PDF";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (Vm is null) return;
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count == 0) return;
        await Vm.SubmitDroppedFileAsync(items[0].Path);
    }

    private void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is HyperlinkButton { Tag: string path }) Vm.UseFileCommand.Execute(path);
    }
}

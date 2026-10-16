using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TicketSplitter.App.ViewModels;

namespace TicketSplitter.App.Steps;

public sealed partial class ChunkPlanPanel : UserControl
{
    public ChunkPlanPanel() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnSplitChunkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ChunkRowViewModel row }) Vm?.SplitChunkCommand.Execute(row);
    }

    private void OnMergeChunkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ChunkRowViewModel row }) Vm?.MergeChunkCommand.Execute(row);
    }

    /// <summary>点切点附近的任意一页，把切点挪到它之后。</summary>
    private void OnBoundaryPageTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PageThumbViewModel page }) Vm?.MoveBoundaryTo(page);
    }
}

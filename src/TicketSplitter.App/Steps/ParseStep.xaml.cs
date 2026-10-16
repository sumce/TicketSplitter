using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TicketSplitter.App.ViewModels;

namespace TicketSplitter.App.Steps;

public sealed partial class ParseStep : UserControl
{
    public ParseStep() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnSplitChunkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ChunkRowViewModel row }) Vm?.SplitChunkCommand.Execute(row);
    }

    private void OnMergeChunkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ChunkRowViewModel row }) Vm?.MergeChunkCommand.Execute(row);
    }
}

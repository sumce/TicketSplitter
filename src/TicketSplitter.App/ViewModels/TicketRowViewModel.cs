using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace TicketSplitter.App.ViewModels;

public partial class TicketRowViewModel : ObservableObject
{
    /// <summary>0 基起始页，仅内部使用。</summary>
    public int StartPageIndex { get; init; }
    public int EndPageIndex { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageRange))]
    private int _startPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageRange))]
    private int _endPage;

    [ObservableProperty] private int _index;
    [ObservableProperty] private string _type = string.Empty;
    [ObservableProperty] private string _area = string.Empty;
    [ObservableProperty] private string _time = string.Empty;
    [ObservableProperty] private int _pageCount;
    [ObservableProperty] private bool _hasIssue;
    [ObservableProperty] private string _issueText = string.Empty;
    [ObservableProperty] private Brush? _typeBrush;

    public string PageRange => $"{StartPage}-{EndPage}";
    public string IndexText => Index.ToString();
    public string PageCountText => PageCount.ToString();
}

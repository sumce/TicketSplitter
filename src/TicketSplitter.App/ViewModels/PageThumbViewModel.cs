using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TicketSplitter.App.ViewModels;

/// <summary>缩略图外的一条页面。</summary>
public partial class PageThumbViewModel : ObservableObject
{
    public PageThumbViewModel(int pageIndex) => PageIndex = pageIndex;

    public int PageIndex { get; }
    public int PageNumber => PageIndex + 1;
    public string PageNumberText => PageNumber.ToString();

    [ObservableProperty] private BitmapImage? _thumbnail;
    [ObservableProperty] private string _badgeText = string.Empty;
    [ObservableProperty] private Brush? _badgeBrush;
    [ObservableProperty] private bool _isTicketStart;
}

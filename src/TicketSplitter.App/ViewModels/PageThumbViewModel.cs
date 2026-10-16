using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
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

    /// <summary>在分块预览里：这一页是当前块的最后一页（切点就在它之后）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameBrush))]
    private bool _isBoundary;

    /// <summary>在分块预览里：这一页属于下一块。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameBrush))]
    private bool _isInNextChunk;

    /// <summary>点这一页要挪的是哪个块的切点（0 基，-1 表示不是分块预览）。</summary>
    public int OwnerChunkIndex { get; init; } = -1;

    /// <summary>预览边框：红=切点所在的最后一页，蓝=已属于下一块，灰=归属当前块。</summary>
    public Brush? FrameBrush => IsBoundary
        ? new SolidColorBrush(ColorHelper.FromArgb(255, 209, 52, 56))
        : IsInNextChunk
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 0, 95, 184))
            : new SolidColorBrush(ColorHelper.FromArgb(255, 205, 205, 205));
}

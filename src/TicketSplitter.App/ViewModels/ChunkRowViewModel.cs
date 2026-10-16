using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TicketSplitter.App.ViewModels;

/// <summary>
/// 解析分块计划里的一行。
/// 关键：把切点两侧的页面直接摊出来，用户点哪一页就把切点挪到那一页之后——
/// 否则让用户在完全看不到页面的情况下决定"哪页是票尾"是不成立的。
/// </summary>
public sealed partial class ChunkRowViewModel : ObservableObject
{
    public required int Index { get; init; }
    public required int StartPage { get; init; }
    public required int EndPage { get; init; }
    public required bool OverLimit { get; init; }
    public required bool CanMerge { get; init; }

    /// <summary>最后一块没有切点，也就没有预览。</summary>
    public bool HasBoundary { get; init; }

    public int PageCount => EndPage - StartPage;
    public string Title => $"块 {Index}";
    public string Range => $"第 {StartPage + 1} – {EndPage} 页";
    public string PageCountText => $"{PageCount} 页";
    public string BoundaryHint => $"切点在第 {EndPage} 页之后（点下面任意一页可挪动）";

    /// <summary>切点前后各若干页的缩略图。</summary>
    public ObservableCollection<PageThumbViewModel> BoundaryPages { get; } = [];
}

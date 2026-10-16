using CommunityToolkit.Mvvm.ComponentModel;

namespace TicketSplitter.App.ViewModels;

/// <summary>解析分块计划里的一行，结束页可被用户调整。</summary>
public sealed partial class ChunkRowViewModel : ObservableObject
{
    public required int Index { get; init; }
    public required int StartPage { get; init; }
    public required int EndPage { get; init; }
    public required bool OverLimit { get; init; }
    public required bool CanMerge { get; init; }

    public int PageCount => EndPage - StartPage;
    public string Title => $"块 {Index}";
    public string Range => $"第 {StartPage + 1} – {EndPage} 页";
    public string PageCountText => $"{PageCount} 页";
}

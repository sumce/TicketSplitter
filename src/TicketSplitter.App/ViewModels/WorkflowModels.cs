using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using TicketSplitter.Core.Pdf;

namespace TicketSplitter.App.ViewModels;

/// <summary>一种"样式"：一页 PDF 上装了几张票、怎么排。</summary>
public partial class LayoutStyleViewModel : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required SheetSplitMode Mode { get; init; }

    /// <summary>内置样式不可删除。</summary>
    public bool BuiltIn { get; init; }

    public bool CanDelete => !BuiltIn;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isRecommended;

    /// <summary>适用时给一句人话结论，例如 "→ 38 页将变成 76 页"。</summary>
    [ObservableProperty] private string _outcome = string.Empty;
}

/// <summary>工作流里的一步。</summary>
public sealed class StepViewModel
{
    public required int Number { get; init; }
    public required string Title { get; init; }
    public required string Glyph { get; init; }
    public required bool IsCurrent { get; init; }
    public required bool IsDone { get; init; }
    public required bool ShowArrow { get; init; }

    public string NumberText => Number.ToString();
    public double TextOpacity => IsCurrent ? 1.0 : 0.55;

    public Brush BadgeBrush => new SolidColorBrush(IsCurrent || IsDone
        ? ColorHelper.FromArgb(255, 0, 95, 184)
        : ColorHelper.FromArgb(255, 150, 150, 150));

    public Windows.UI.Text.FontWeight TitleWeight => IsCurrent
        ? Microsoft.UI.Text.FontWeights.SemiBold
        : Microsoft.UI.Text.FontWeights.Normal;
}

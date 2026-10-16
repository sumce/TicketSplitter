using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TicketSplitter.App.Services;
using TicketSplitter.Core.Export;
using TicketSplitter.Core.Mineru;
using TicketSplitter.Core.Pdf;
using TicketSplitter.Core.Tickets;

namespace TicketSplitter.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly Window _window;
    private readonly nint _hwnd;
    private readonly AppSettings _settings;

    private CancellationTokenSource? _cts;
    private PageThumbnailRenderer? _renderer;

    private LayoutAnalysis? _analysis;
    private int _sourcePageCount;
    private string? _workingPdf;
    private int _workingPageCount;
    private List<Ticket> _detected = [];
    private List<Ticket> _current = [];

    public MainViewModel(Window window)
    {
        _window = window;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _settings = AppSettings.Load();

        Token = _settings.ReadToken() ?? Environment.GetEnvironmentVariable("MINERU_TOKEN") ?? string.Empty;
        RememberToken = _settings.RememberToken;
        ReuseExistingParse = _settings.ReuseExistingParse;
        OutputDir = _settings.LastOutputDir ?? string.Empty;
        CleanOutput = true;

        if (!string.IsNullOrEmpty(_settings.LastSourcePath) && File.Exists(_settings.LastSourcePath))
            _ = LoadSourceAsync(_settings.LastSourcePath);
    }

    // ---------------- 集合 ----------------
    public ObservableCollection<TicketRowViewModel> Tickets { get; } = [];
    public ObservableCollection<PageThumbViewModel> Pages { get; } = [];
    public ObservableCollection<string> WarningList { get; } = [];

    // ---------------- 源文件 / 版式 ----------------
    [ObservableProperty] private string? _sourcePath;
    [ObservableProperty] private string _sourceInfo = "尚未选择文件";
    [ObservableProperty] private string _sourceName = string.Empty;
    [ObservableProperty] private string _sourceMeta = string.Empty;
    [ObservableProperty] private string _layoutShort = string.Empty;
    [ObservableProperty] private string _selectedPageLabel = "未选中页面";
    [ObservableProperty] private bool _canEditBoundary;
    [ObservableProperty] private string _layoutLabel = string.Empty;
    [ObservableProperty] private string _layoutDetail = string.Empty;
    [ObservableProperty] private string _layoutNotes = string.Empty;
    [ObservableProperty] private bool _hasLayout;
    [ObservableProperty] private bool _hasNotes;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoSource))]
    private bool _hasSource;

    [ObservableProperty] private int _layoutModeIndex;
    [ObservableProperty] private bool _showPages = true;

    public bool NoSource => !HasSource;

    // ---------------- 解析 ----------------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenButtonText))]
    private string _token = string.Empty;

    public string TokenButtonText => string.IsNullOrWhiteSpace(Token) ? "设置 Token" : "Token 已设置";
    [ObservableProperty] private bool _rememberToken = true;
    [ObservableProperty] private bool _reuseExistingParse;
    [ObservableProperty] private bool _canReuseExisting;

    // ---------------- 运行状态 ----------------
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _progressIsIndeterminate;
    [ObservableProperty] private bool _isBusy;

    // ---------------- 结果 ----------------
    [ObservableProperty] private string _ticketSummary = "尚未解析";
    [ObservableProperty] private bool _hasTickets;
    [ObservableProperty] private bool _hasWarnings;
    [ObservableProperty] private string _warningHeader = string.Empty;
    [ObservableProperty] private TicketRowViewModel? _selectedTicket;
    [ObservableProperty] private PageThumbViewModel? _selectedPage;

    // ---------------- 导出 ----------------
    [ObservableProperty] private string _outputDir = string.Empty;
    [ObservableProperty] private bool _cleanOutput = true;
    [ObservableProperty] private string _lastExportInfo = string.Empty;
    [ObservableProperty] private bool _hasExport;

    partial void OnSelectedPageChanged(PageThumbViewModel? value)
    {
        SelectedPageLabel = value is null ? "未选中页面" : $"选中 第 {value.PageNumber} 页";
        CanEditBoundary = value is not null && HasTickets;
    }

    partial void OnHasTicketsChanged(bool value) => CanEditBoundary = value && SelectedPage is not null;

    partial void OnLayoutModeIndexChanged(int value)
    {
        if (SourcePath is null || IsBusy) return;
        _ = LoadSourceAsync(SourcePath);
    }

    partial void OnTokenChanged(string value) => PersistToken();

    partial void OnReuseExistingParseChanged(bool value)
    {
        _settings.ReuseExistingParse = value;
        _settings.Save();
    }
    partial void OnRememberTokenChanged(bool value) => PersistToken();

    private void PersistToken()
    {
        _settings.RememberToken = RememberToken;
        _settings.WriteToken(RememberToken ? Token : null);
        _settings.Save();
    }

    // ================= 命令 =================

    [RelayCommand]
    private async Task PickSourceAsync()
    {
        var path = await PickerHelper.PickPdfAsync(_hwnd);
        if (path is null) return;
        await LoadSourceAsync(path);
    }

    [RelayCommand]
    private async Task PickOutputDirAsync()
    {
        var dir = await PickerHelper.PickFolderAsync(_hwnd);
        if (dir is null) return;
        OutputDir = dir;
        _settings.LastOutputDir = dir;
        _settings.Save();
    }

    [RelayCommand]
    private void OpenOutput()
    {
        var dir = ResolveOutputDir();
        if (Directory.Exists(dir)) PickerHelper.RevealInExplorer(dir);
    }

    [RelayCommand]
    private async Task StartParseAsync()
    {
        if (SourcePath is null || _workingPdf is null) { StatusText = "请先选择 PDF"; return; }
        if (IsBusy) return;

        _cts = new CancellationTokenSource();
        IsBusy = true;
        HasExport = false;
        try
        {
            var workDir = WorkDirFor(SourcePath);
            var mineruDir = Path.Combine(workDir, "mineru");
            var merged = Path.Combine(mineruDir, "merged_content_list.json");

            if (ReuseExistingParse && File.Exists(merged))
            {
                StatusText = "复用已有解析结果…";
                ProgressIsIndeterminate = true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Token))
                {
                    StatusText = "缺少 MinerU Token，请在设置里填写";
                    return;
                }

                var options = new MineruOptions { Token = Token };
                using var client = new MineruClient(options);
                var progress = new Progress<ParseProgress>(p =>
                {
                    StatusText = p.Message;
                    if (p.Percent > 0) { ProgressValue = p.Percent; ProgressIsIndeterminate = false; }
                    else ProgressIsIndeterminate = true;
                });

                var result = await client.ParseAsync(_workingPdf, mineruDir, _workingPageCount, progress, _cts.Token);
                merged = result.MergedContentListPath;
            }

            StatusText = "正在识别票边界…";
            ProgressIsIndeterminate = true;

            var content = await Task.Run(() => MineruContentList.Load(merged), _cts.Token);
            _detected = [.. TicketDetector.Detect(content, _workingPageCount)];
            ApplyTickets(_detected);

            StatusText = _detected.Count == 0
                ? "没有识别到任何作业票，请检查解析结果"
                : $"识别到 {_detected.Count} 张票";
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消";
        }
        catch (Exception ex)
        {
            StatusText = "解析失败：" + ex.Message;
        }
        finally
        {
            ProgressIsIndeterminate = false;
            ProgressValue = 0;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (SourcePath is null || _current.Count == 0) { StatusText = "还没有可导出的票"; return; }
        if (IsBusy) return;

        _cts = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            var outDir = ResolveOutputDir();
            StatusText = "正在导出…";
            ProgressIsIndeterminate = false;
            ProgressValue = 0;

            var progress = new Progress<int>(n =>
            {
                ProgressValue = 100.0 * n / Math.Max(1, _current.Count);
                StatusText = $"正在导出 {n}/{_current.Count}…";
            });

            var tickets = _current;
            var sourcePath = SourcePath!;
            var result = await Task.Run(() =>
            {
                using var source = PdfSource.Open(sourcePath);
                return TicketArchiveWriter.Write(source, tickets, outDir,
                    new ArchiveOptions { Clean = CleanOutput }, progress, _cts.Token);
            }, _cts.Token);

            _settings.LastOutputDir = outDir;
            _settings.Save();

            LastExportInfo = $"已导出 {result.Rows.Count} 张票 · {result.TotalBytes / 1048576.0:F1} MB\n{result.CsvPath}";
            HasExport = true;
            StatusText = "导出完成";
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消";
        }
        catch (Exception ex)
        {
            StatusText = "导出失败：" + ex.Message;
        }
        finally
        {
            ProgressValue = 0;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ---------------- 人工校正 ----------------

    [RelayCommand]
    private void SplitAtPage()
    {
        if (SelectedPage is null) { StatusText = "请先在左侧页面列表里选中一页"; return; }
        var page = SelectedPage.PageIndex;
        var i = _current.FindIndex(t => page >= t.StartPage && page < t.EndPage);
        if (i < 0) return;
        var t = _current[i];
        if (page == t.StartPage) { StatusText = "这一页已经是某张票的首页"; return; }

        _current[i] = t with { EndPage = page };
        _current.Insert(i + 1, t with { StartPage = page });
        ApplyTickets(_current);
        StatusText = $"已在第 {page + 1} 页处切开";
    }

    [RelayCommand]
    private void MergePrevious()
    {
        if (SelectedPage is null) { StatusText = "请先选中一页"; return; }
        var page = SelectedPage.PageIndex;
        var i = _current.FindIndex(t => page >= t.StartPage && page < t.EndPage);
        if (i <= 0) { StatusText = "没有可合并的上一张票"; return; }

        _current[i - 1] = _current[i - 1] with { EndPage = _current[i].EndPage };
        _current.RemoveAt(i);
        ApplyTickets(_current);
        StatusText = "已并入上一张票";
    }

    [RelayCommand]
    private void DeleteTicket()
    {
        if (SelectedTicket is null) { StatusText = "请先选中一张票"; return; }
        var i = Tickets.IndexOf(SelectedTicket);
        if (i < 0) return;
        _current.RemoveAt(i);
        ApplyTickets(_current);
        StatusText = "已删除该票（其页面将不再被归档）";
    }

    [RelayCommand]
    private void ResetBoundaries()
    {
        if (_detected.Count == 0) { StatusText = "还没有自动切分结果"; return; }
        ApplyTickets(_detected);
        StatusText = "已恢复自动切分结果";
    }

    // ================= 内部流程 =================

    private async Task LoadSourceAsync(string path)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsBusy = true;
        HasExport = false;
        StatusText = "正在分析版式…";
        ProgressIsIndeterminate = true;

        try
        {
            SourcePath = path;
            _settings.LastSourcePath = path;
            if (string.IsNullOrEmpty(OutputDir))
                OutputDir = Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + "_split");
            _settings.LastOutputDir = OutputDir;
            _settings.Save();

            var pageCount = 0;
            var analysis = await Task.Run(() =>
            {
                using var src = PdfSource.Open(path);
                pageCount = src.PageCount;
                return PdfLayoutAnalyzer.Analyze(src.Pages);
            }, ct);

            ct.ThrowIfCancellationRequested();

            _analysis = analysis;
            _sourcePageCount = pageCount;
            SourceInfo = $"{Path.GetFileName(path)}\n{new FileInfo(path).Length / 1048576.0:F1} MB · {pageCount} 页";
            SourceName = Path.GetFileName(path);
            SourceMeta = $"{new FileInfo(path).Length / 1048576.0:F1} MB · {pageCount} 页";
            LayoutLabel = analysis.Label;
            LayoutDetail = analysis.Detail;
            LayoutNotes = string.Join("\n", analysis.Notes);
            HasNotes = analysis.Notes.Count > 0;
            HasLayout = true;
            HasSource = true;

            await PrepareWorkingPdfAsync(path, analysis, ct);
            LayoutShort = _workingPageCount != _sourcePageCount
                ? $"整版 → 拆半幅（{_workingPageCount} 页）"
                : $"半幅（{_workingPageCount} 页）";

            Tickets.Clear();
            WarningList.Clear();
            HasTickets = false;
            HasWarnings = false;
            TicketSummary = "尚未解析";
            _detected = [];
            _current = [];

            await StartThumbnailRenderAsync(ct);
            StatusText = analysis.NeedsHalfSplit || LayoutModeIndex == 2
                ? $"版式已就绪，将按 {_workingPageCount} 页（已拆半幅）解析"
                : $"版式已就绪，共 {_workingPageCount} 页";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusText = "打开失败：" + ex.Message;
        }
        finally
        {
            ProgressIsIndeterminate = false;
            ProgressValue = 0;
            IsBusy = false;
        }
    }

    private async Task PrepareWorkingPdfAsync(string path, LayoutAnalysis analysis, CancellationToken ct)
    {
        var mustSplit = LayoutModeIndex switch
        {
            1 => false,          // 强制半幅
            2 => true,           // 强制整版
            _ => analysis.NeedsHalfSplit,
        };

        if (analysis.Layout == PdfLayout.Mixed && LayoutModeIndex == 0)
            StatusText = "混合版式：已按半幅处理，建议在版式里手动切换确认";

        if (!mustSplit)
        {
            _workingPdf = path;
            _workingPageCount = _sourcePageCount;
            CanReuseExisting = File.Exists(Path.Combine(WorkDirFor(path), "mineru", "merged_content_list.json"));
            return;
        }

        var workDir = WorkDirFor(path);
        Directory.CreateDirectory(workDir);
        var halves = Path.Combine(workDir, $"_halves_{Path.GetFileNameWithoutExtension(path)}.pdf");

        if (!File.Exists(halves))
        {
            StatusText = "正在拆分整版为左右半幅…";
            var written = await Task.Run(() =>
            {
                using var src = PdfSource.Open(path);
                return src.WriteHalves(halves);
            }, ct);
            _workingPageCount = written;
        }
        else
        {
            _workingPageCount = await Task.Run(() =>
            {
                using var src = PdfSource.Open(halves);
                return src.PageCount;
            }, ct);
        }

        _workingPdf = halves;
        CanReuseExisting = File.Exists(Path.Combine(workDir, "mineru", "merged_content_list.json"));
    }

    private async Task StartThumbnailRenderAsync(CancellationToken ct)
    {
        _renderer?.Dispose();
        _renderer = null;
        Pages.Clear();
        for (var i = 0; i < _workingPageCount; i++) Pages.Add(new PageThumbViewModel(i));
        PaintPageBadges();

        if (_workingPdf is null) return;

        try
        {
            _renderer = await PageThumbnailRenderer.LoadAsync(_workingPdf);
            for (var i = 0; i < Pages.Count; i++)
            {
                if (ct.IsCancellationRequested) return;
                var bmp = await _renderer.RenderAsync(i, 150, ct);
                if (bmp is not null) Pages[i].Thumbnail = bmp;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // 缩略图失败不影响主流程
        }
    }

    private void ApplyTickets(List<Ticket> tickets)
    {
        _current = [.. tickets.Select((t, i) => t with { Index = i + 1 })];

        var warnings = TicketValidator.Validate(_current, _workingPageCount);
        var issuesByTicket = warnings
            .Where(w => w.Issue is TicketIssue.SinglePage or TicketIssue.MissingTime
                        or TicketIssue.UnknownArea or TicketIssue.UnknownType)
            .GroupBy(w => w.TicketIndex)
            .ToDictionary(g => g.Key, g => string.Join("；", g.Select(x => x.Message)));

        var shortIssues = warnings
            .Where(w => w.TicketIndex > 0)
            .GroupBy(w => w.TicketIndex)
            .ToDictionary(g => g.Key, g => string.Join("、", g.Select(x => ShortIssue(x.Issue)).Distinct()));

        Tickets.Clear();
        foreach (var t in _current)
        {
            var hasIssue = issuesByTicket.TryGetValue(t.Index, out var issue);
            Tickets.Add(new TicketRowViewModel
            {
                Index = t.Index,
                Type = t.Type,
                Area = t.Area,
                Time = t.Time ?? "",
                StartPage = t.StartPage1Based,
                EndPage = t.EndPage,
                PageCount = t.PageCount,
                HasIssue = hasIssue,
                IssueText = issue ?? "",
                IssueShort = shortIssues.TryGetValue(t.Index, out var s) ? s : "",
                TypeBrush = BrushFor(t.Type),
                StartPageIndex = t.StartPage,
                EndPageIndex = t.EndPage,
            });
        }

        HasTickets = _current.Count > 0;

        var byType = string.Join(" · ", _current.GroupBy(t => t.Type)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key.Replace("作业申请表", "")} {g.Count()}"));
        TicketSummary = _current.Count == 0
            ? "尚未解析"
            : $"共 {_current.Count} 张票（{byType}），覆盖 {_current.Sum(t => t.PageCount)} 页";

        WarningList.Clear();
        foreach (var w in warnings) WarningList.Add(w.Message);
        HasWarnings = warnings.Count > 0;
        WarningHeader = $"发现 {warnings.Count} 处需要关注";
        if (HasWarnings) HasNotes = true;

        PaintPageBadges();
    }

    private void PaintPageBadges()
    {
        foreach (var p in Pages)
        {
            p.BadgeText = string.Empty;
            p.BadgeBrush = null;
            p.IsTicketStart = false;
        }
        if (Pages.Count == 0 || _current.Count == 0) return;

        foreach (var t in _current)
        {
            for (var i = t.StartPage; i < t.EndPage && i < Pages.Count; i++)
            {
                var p = Pages[i];
                p.BadgeText = t.Index.ToString();
                p.BadgeBrush = BrushFor(t.Type);
                if (i == t.StartPage) p.IsTicketStart = true;
            }
        }
    }

    private static string ShortIssue(TicketIssue issue) => issue switch
    {
        TicketIssue.SinglePage => "单页残片",
        TicketIssue.MissingTime => "缺施工时间",
        TicketIssue.UnknownArea => "缺施工区域",
        TicketIssue.UnknownType => "票类型未知",
        TicketIssue.PageGap => "页缺口",
        TicketIssue.PageOverlap => "页重叠",
        _ => issue.ToString(),
    };

    private static Brush BrushFor(string type) => type switch
    {
        TicketTitle.HotWork => new SolidColorBrush(ColorHelper.FromArgb(255, 209, 52, 56)),
        TicketTitle.HighWork => new SolidColorBrush(ColorHelper.FromArgb(255, 0, 95, 184)),
        TicketTitle.Contractor => new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16)),
        _ => new SolidColorBrush(Colors.Gray),
    };

    private string WorkDirFor(string sourcePath)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sourcePath)))[..8];
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TicketSplitter", "work", $"{stem}_{hash}");
    }

    private string ResolveOutputDir() =>
        string.IsNullOrWhiteSpace(OutputDir)
            ? Path.Combine(Path.GetDirectoryName(SourcePath ?? ".") ?? ".", "作业票切分结果")
            : OutputDir;

    public void Shutdown()
    {
        _cts?.Cancel();
        _renderer?.Dispose();
        _renderer = null;
    }
}

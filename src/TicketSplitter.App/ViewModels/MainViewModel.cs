using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TicketSplitter.App.Services;
using TicketSplitter.Core.Export;
using TicketSplitter.Core.Mineru;
using TicketSplitter.Core.Pdf;
using TicketSplitter.Core.Tickets;

namespace TicketSplitter.App.ViewModels;

/// <summary>工作流的五个环节。切开这一步在"每页一张票"的样式下会被跳过。</summary>
public enum WorkflowStep { Submit, Layout, Split, Parse, Review }

public partial class MainViewModel : ObservableObject
{
    /// <summary>内置样式。用户可以在此基础上自行添加。</summary>
    public static readonly (string Id, string Name, string Desc, SheetSplitMode Mode)[] BuiltInStyles =
    [
        ("single", "每页一张票",
            "一页 PDF 就是一张票的一页（半幅扫描），不需要切开，原样送去识别。",
            SheetSplitMode.None),
        ("lr", "一面两页 · 左右并排",
            "一页 PDF 左右各排了一张票（横版整版扫描）。先按中线拆成左右两页，再送去识别。",
            SheetSplitMode.LeftRight),
        ("tb", "一面两页 · 上下排列",
            "一页 PDF 上下各排了一张票（竖版 A4，两张 A5 叠放）。先按中线拆成上下两页，再送去识别。",
            SheetSplitMode.TopBottom),
    ];

    private readonly Window _window;
    private readonly nint _hwnd;
    private readonly AppSettings _settings;

    private CancellationTokenSource? _cts;
    private PageThumbnailRenderer? _renderer;

    private LayoutAnalysis? _analysis;
    private int _sourcePageCount;
    private string? _workingPdf;
    private int _workingPageCount;
    private SheetSplitMode? _appliedMode;
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

        foreach (var f in _settings.RecentFiles.Where(File.Exists).Take(5)) RecentFiles.Add(f);

        RebuildSteps();
        UpdateNavigation();

        if (!string.IsNullOrEmpty(_settings.LastSourcePath) && File.Exists(_settings.LastSourcePath))
            _ = LoadSourceAsync(_settings.LastSourcePath);
    }

    // ==================== 集合 ====================

    public ObservableCollection<StepViewModel> Steps { get; } = [];
    public ObservableCollection<LayoutStyleViewModel> Styles { get; } = [];
    public ObservableCollection<TicketRowViewModel> Tickets { get; } = [];
    public ObservableCollection<PageThumbViewModel> Pages { get; } = [];
    public ObservableCollection<string> WarningList { get; } = [];
    public ObservableCollection<string> RecentFiles { get; } = [];

    // ==================== 工作流 ====================

    [ObservableProperty] private WorkflowStep _currentStep = WorkflowStep.Submit;
    [ObservableProperty] private bool _canGoNext;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private string _nextText = "下一步";
    [ObservableProperty] private string _backText = "上一步";
    [ObservableProperty] private bool _isOnSubmitStep = true;
    [ObservableProperty] private bool _isOnLayoutStep;
    [ObservableProperty] private bool _isOnSplitStep;
    [ObservableProperty] private bool _isOnParseStep;
    [ObservableProperty] private bool _isOnReviewStep;

    // ==================== 源文件 / 版式 ====================

    [ObservableProperty] private string? _sourcePath;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoSource))]
    private bool _hasSource;

    public bool NoSource => !HasSource;

    [ObservableProperty] private string _sourceName = string.Empty;
    [ObservableProperty] private string _sourceMeta = string.Empty;
    [ObservableProperty] private string _layoutLabel = string.Empty;
    [ObservableProperty] private string _layoutDetail = string.Empty;
    [ObservableProperty] private string _layoutNotes = string.Empty;
    [ObservableProperty] private bool _hasLayout;
    [ObservableProperty] private bool _hasNotes;
    [ObservableProperty] private string _layoutShort = string.Empty;
    [ObservableProperty] private string _layoutTooltip = string.Empty;
    [ObservableProperty] private string _layoutGlyph = "\uE946";
    [ObservableProperty] private Brush? _layoutBrush;

    // ==================== 样式 ====================

    [ObservableProperty] private LayoutStyleViewModel? _selectedStyle;
    [ObservableProperty] private bool _needsSplit;
    [ObservableProperty] private bool _hasCustomStyles;

    // ==================== 样式预览 ====================

    [ObservableProperty] private ImageSource? _previewSource;
    [ObservableProperty] private ImageSource? _previewFirst;
    [ObservableProperty] private ImageSource? _previewSecond;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private bool _hasSplitPreview;

    // ==================== 切开结果 ====================

    [ObservableProperty] private bool _splitDone;
    [ObservableProperty] private string _splitSummary = string.Empty;
    [ObservableProperty] private int _splitSourcePages;
    [ObservableProperty] private int _splitResultPages;
    [ObservableProperty] private string _splitSourceLabel = string.Empty;
    [ObservableProperty] private string _splitResultLabel = string.Empty;

    // ==================== 解析 ====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenButtonText))]
    private string _token = string.Empty;

    public string TokenButtonText => string.IsNullOrWhiteSpace(Token) ? "设置 Token" : "Token 已设置";

    [ObservableProperty] private bool _rememberToken = true;
    [ObservableProperty] private bool _reuseExistingParse;
    [ObservableProperty] private bool _canReuseExisting;

    // ==================== 运行状态 ====================

    [ObservableProperty] private string _statusText = "请选择一个扫描件作业票 PDF";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _progressIsIndeterminate;
    [ObservableProperty] private bool _isBusy;

    // ==================== 结果 ====================

    [ObservableProperty] private string _ticketSummary = "尚未识别";
    [ObservableProperty] private bool _hasTickets;
    [ObservableProperty] private bool _hasWarnings;
    [ObservableProperty] private string _warningHeader = string.Empty;
    [ObservableProperty] private TicketRowViewModel? _selectedTicket;
    [ObservableProperty] private PageThumbViewModel? _selectedPage;
    [ObservableProperty] private string _selectedPageLabel = "未选中页面";
    [ObservableProperty] private bool _canEditBoundary;
    [ObservableProperty] private bool _showPages = true;

    // ==================== 导出 ====================

    [ObservableProperty] private string _outputDir = string.Empty;
    [ObservableProperty] private bool _cleanOutput = true;
    [ObservableProperty] private string _lastExportInfo = string.Empty;
    [ObservableProperty] private bool _hasExport;

    // ==================== 变更钩子 ====================

    partial void OnTokenChanged(string value) => PersistToken();
    partial void OnRememberTokenChanged(bool value) => PersistToken();

    partial void OnReuseExistingParseChanged(bool value)
    {
        _settings.ReuseExistingParse = value;
        _settings.Save();
    }

    partial void OnSelectedPageChanged(PageThumbViewModel? value)
    {
        SelectedPageLabel = value is null ? "未选中页面" : $"选中 第 {value.PageNumber} 页";
        CanEditBoundary = value is not null && HasTickets;
    }

    partial void OnHasTicketsChanged(bool value) => CanEditBoundary = value && SelectedPage is not null;

    partial void OnSelectedStyleChanged(LayoutStyleViewModel? value)
    {
        foreach (var s in Styles) s.IsSelected = ReferenceEquals(s, value);

        NeedsSplit = value is { Mode: not SheetSplitMode.None };
        _appliedMode = null;
        SplitDone = false;

        if (value is { Mode: SheetSplitMode.None } && SourcePath is not null)
        {
            // 不需要切开时直接确认工作文件，切开这一步会被跳过
            _workingPdf = SourcePath;
            _workingPageCount = _sourcePageCount;
            _appliedMode = SheetSplitMode.None;
            SplitSourcePages = _sourcePageCount;
            SplitResultPages = _sourcePageCount;
            SplitSummary = $"该样式不需要切开，{_sourcePageCount} 页将原样送去识别。";
            SplitDone = true;
        }

        RefreshSplitLabels();
        RebuildSteps();
        UpdateNavigation();

        if (SourcePath is not null) _ = RefreshPreviewAsync();
    }

    partial void OnCurrentStepChanged(WorkflowStep value)
    {
        IsOnSubmitStep = value == WorkflowStep.Submit;
        IsOnLayoutStep = value == WorkflowStep.Layout;
        IsOnSplitStep = value == WorkflowStep.Split;
        IsOnParseStep = value == WorkflowStep.Parse;
        IsOnReviewStep = value == WorkflowStep.Review;

        RebuildSteps();
        UpdateNavigation();

        switch (value)
        {
            case WorkflowStep.Layout: _ = RefreshPreviewAsync(); break;
            case WorkflowStep.Split: _ = ApplyStyleAsync(); break;
        }
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private async Task PickSourceAsync()
    {
        var path = await PickerHelper.PickPdfAsync(_hwnd);
        if (path is null) return;
        await LoadSourceAsync(path);
    }

    [RelayCommand]
    private void GoNext()
    {
        var next = NeighbourStep(CurrentStep, +1);
        if (next is { } s) CurrentStep = s;
    }

    [RelayCommand]
    private void GoBack()
    {
        var prev = NeighbourStep(CurrentStep, -1);
        if (prev is { } s) CurrentStep = s;
    }

    [RelayCommand]
    private void UseFile(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) _ = LoadSourceAsync(path);
    }

    /// <summary>拖放进来的文件。</summary>
    public async Task SubmitDroppedFileAsync(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { StatusText = "拖入的不是文件"; return; }
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) { StatusText = "只支持 PDF 文件"; return; }
        await LoadSourceAsync(path);
    }

    private void RememberRecent(string path)
    {
        _settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _settings.RecentFiles.Insert(0, path);
        if (_settings.RecentFiles.Count > 5) _settings.RecentFiles.RemoveRange(5, _settings.RecentFiles.Count - 5);
        _settings.Save();

        RecentFiles.Clear();
        foreach (var f in _settings.RecentFiles.Where(File.Exists)) RecentFiles.Add(f);
    }

    /// <summary>添加一个自定义样式（由"添加样式"对话框调用）。</summary>
    public void AddCustomStyle(string name, string description, SheetSplitMode mode)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        _settings.CustomStyles.RemoveAll(s => s.Name == name);
        _settings.CustomStyles.Add(new StyleRecord(name.Trim(), description.Trim(), mode));
        _settings.Save();

        BuildStyles();
        SelectedStyle = Styles.LastOrDefault(s => !s.BuiltIn) ?? SelectedStyle;
        StatusText = $"已添加样式「{name}」";
    }

    public void RemoveCustomStyle(LayoutStyleViewModel style)
    {
        if (style.BuiltIn) return;
        _settings.CustomStyles.RemoveAll(s => s.Name == style.Name);
        _settings.Save();
        var wasSelected = ReferenceEquals(SelectedStyle, style);
        BuildStyles();
        if (wasSelected) SelectedStyle = Styles.FirstOrDefault();
        StatusText = $"已删除样式「{style.Name}」";
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
        if (SourcePath is null) { StatusText = "请先选择 PDF"; return; }
        if (IsBusy) return;

        await EnsureWorkingPdfAsync();
        if (_workingPdf is null) { StatusText = "工作文件尚未准备好"; return; }

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
                    StatusText = "缺少 MinerU Token，请点「设置 Token」填写";
                    return;
                }

                using var client = new MineruClient(new MineruOptions { Token = Token });
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
        catch (OperationCanceledException) { StatusText = "已取消"; }
        catch (Exception ex) { StatusText = "解析失败：" + ex.Message; }
        finally
        {
            ProgressIsIndeterminate = false;
            ProgressValue = 0;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            UpdateNavigation();
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
            var writtenFrom = _workingPdf!;
            var result = await Task.Run(() =>
            {
                using var source = PdfSource.Open(writtenFrom);
                return TicketArchiveWriter.Write(source, tickets, outDir,
                    new ArchiveOptions { Clean = CleanOutput }, progress, _cts.Token);
            }, _cts.Token);

            _settings.LastOutputDir = outDir;
            _settings.Save();

            LastExportInfo = $"已导出 {result.Rows.Count} 张票 · {result.TotalBytes / 1048576.0:F1} MB\n{result.CsvPath}";
            HasExport = true;
            StatusText = "导出完成";
        }
        catch (OperationCanceledException) { StatusText = "已取消"; }
        catch (Exception ex) { StatusText = "导出失败：" + ex.Message; }
        finally
        {
            ProgressValue = 0;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ==================== 人工校正 ====================

    [RelayCommand]
    private void SplitAtPage()
    {
        if (SelectedPage is null) { StatusText = "请先在页面列表里选中一页"; return; }
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

    // ==================== 内部流程 ====================

    private void PersistToken()
    {
        _settings.RememberToken = RememberToken;
        _settings.WriteToken(RememberToken ? Token : null);
        _settings.Save();
    }

    private async Task LoadSourceAsync(string path)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsBusy = true;
        HasExport = false;
        StatusText = "正在读取文件…";
        ProgressIsIndeterminate = true;

        try
        {
            SourcePath = path;
            _settings.LastSourcePath = path;
            if (string.IsNullOrEmpty(OutputDir))
                OutputDir = Path.Combine(Path.GetDirectoryName(path) ?? ".",
                    Path.GetFileNameWithoutExtension(path) + "_split");
            _settings.LastOutputDir = OutputDir;
            _settings.Save();
            RememberRecent(path);

            var (pageCount, analysis) = await Task.Run(() =>
            {
                using var src = PdfSource.Open(path);
                return (src.PageCount, PdfLayoutAnalyzer.Analyze(src.Pages));
            }, ct);
            ct.ThrowIfCancellationRequested();

            _analysis = analysis;
            _sourcePageCount = pageCount;

            SourceName = Path.GetFileName(path);
            SourceMeta = $"{new FileInfo(path).Length / 1048576.0:F1} MB · {pageCount} 页";
            LayoutLabel = analysis.Label;
            LayoutDetail = analysis.Detail;
            LayoutNotes = string.Join("\n", analysis.Notes);
            HasNotes = analysis.Notes.Count > 0;
            HasLayout = true;
            HasSource = true;

            LayoutShort = analysis.SuggestedSplitMode switch
            {
                SheetSplitMode.LeftRight => "识别为 左右并排",
                SheetSplitMode.TopBottom => "识别为 上下排列",
                _ => "识别为 每页一张",
            };
            LayoutGlyph = analysis.Layout == PdfLayout.Mixed ? "\uE7BA" : "\uE946";
            LayoutBrush = new SolidColorBrush(analysis.Layout == PdfLayout.Mixed
                ? ColorHelper.FromArgb(255, 157, 93, 0)
                : ColorHelper.FromArgb(255, 0, 95, 184));

            var tip = $"{analysis.Label}\n{analysis.Detail}";
            if (analysis.Notes.Count > 0) tip += "\n\n" + string.Join("\n", analysis.Notes);
            LayoutTooltip = tip;

            // 换文件时清掉上一份的产物
            Tickets.Clear();
            WarningList.Clear();
            HasTickets = false;
            HasWarnings = false;
            TicketSummary = "尚未识别";
            _detected = [];
            _current = [];
            _appliedMode = null;
            _workingPdf = null;
            _workingPageCount = 0;
            SelectedTicket = null;
            SelectedPage = null;
            _renderer?.Dispose();
            _renderer = null;
            Pages.Clear();

            BuildStyles();                 // 会选中推荐样式并触发预览
            CurrentStep = WorkflowStep.Layout;

            StatusText = analysis.SuggestedSplitMode == SheetSplitMode.None
                ? $"已读取 {pageCount} 页，请确认样式"
                : $"已读取 {pageCount} 页，识别为{analysis.Label}，请确认样式";
        }
        catch (OperationCanceledException) { }
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

    private void BuildStyles()
    {
        Styles.Clear();
        foreach (var (id, name, desc, mode) in BuiltInStyles)
            Styles.Add(new LayoutStyleViewModel
            {
                Id = id, Name = name, Description = desc, Mode = mode, BuiltIn = true,
            });

        foreach (var c in _settings.CustomStyles)
            Styles.Add(new LayoutStyleViewModel
            {
                Id = "custom:" + c.Name, Name = c.Name, Description = c.Description, Mode = c.Mode,
            });

        HasCustomStyles = _settings.CustomStyles.Count > 0;

        var suggested = _analysis?.SuggestedSplitMode ?? SheetSplitMode.None;
        foreach (var s in Styles)
        {
            s.IsRecommended = s.Mode == suggested;
            s.Outcome = _sourcePageCount == 0
                ? string.Empty
                : s.Mode == SheetSplitMode.None
                    ? $"{_sourcePageCount} 页 —— 不切开"
                    : $"{_sourcePageCount} 页 → 切开后 {_sourcePageCount * 2} 页";
        }

        // 优先保留用户上一次手动选的样式，否则用推荐样式
        var keep = SelectedStyle is { } cur ? Styles.FirstOrDefault(s => s.Id == cur.Id) : null;
        SelectedStyle = keep ?? Styles.FirstOrDefault(s => s.Mode == suggested) ?? Styles.FirstOrDefault();
    }

    /// <summary>进入切开步骤时执行；样式未变且产物还在就不重复切。</summary>
    private async Task ApplyStyleAsync()
    {
        if (SourcePath is null || SelectedStyle is null || IsBusy) return;
        if (_appliedMode == SelectedStyle.Mode && _workingPdf is not null && File.Exists(_workingPdf)) return;

        var mode = SelectedStyle.Mode;
        if (mode == SheetSplitMode.None)
        {
            _workingPdf = SourcePath;
            _workingPageCount = _sourcePageCount;
            _appliedMode = SheetSplitMode.None;
            SplitDone = true;
            RefreshSplitLabels();
            UpdateNavigation();
            return;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsBusy = true;
        SplitDone = false;
        UpdateNavigation();

        try
        {
            var workDir = WorkDirFor(SourcePath);
            Directory.CreateDirectory(workDir);
            var target = Path.Combine(workDir,
                $"_split_{mode}_{Path.GetFileNameWithoutExtension(SourcePath)}.pdf");

            ProgressIsIndeterminate = false;
            var progress = new Progress<int>(p =>
            {
                ProgressValue = 100.0 * p / Math.Max(1, _sourcePageCount);
                StatusText = $"正在切开 {p}/{_sourcePageCount} 页…";
            });

            var written = await Task.Run(() =>
            {
                using var src = PdfSource.Open(SourcePath);
                return src.WriteSplit(target, mode, progress, ct);
            }, ct);

            _workingPdf = target;
            _workingPageCount = written;
            _appliedMode = mode;

            SplitSourcePages = _sourcePageCount;
            SplitResultPages = written;
            SplitSummary = $"已按「{SelectedStyle.Name}」切开：{_sourcePageCount} 页 → {written} 页。接下来送去 MinerU 识别。";
            SplitDone = true;
            StatusText = SplitSummary;
        }
        catch (OperationCanceledException) { StatusText = "已取消"; }
        catch (Exception ex) { StatusText = "切开失败：" + ex.Message; }
        finally
        {
            ProgressValue = 0;
            ProgressIsIndeterminate = false;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            RefreshSplitLabels();
            UpdateNavigation();
        }
    }

    private async Task EnsureWorkingPdfAsync()
    {
        if (SelectedStyle is null || SourcePath is null) return;
        if (_workingPdf is not null && File.Exists(_workingPdf) && _appliedMode == SelectedStyle.Mode) return;
        await ApplyStyleAsync();
    }

    private void RefreshSplitLabels()
    {
        SplitSourceLabel = _sourcePageCount == 0 ? "—" : $"{_sourcePageCount}";
        SplitResultLabel = SplitResultPages == 0 ? "—" : $"{SplitResultPages}";
        CanReuseExisting = SourcePath is not null &&
            File.Exists(Path.Combine(WorkDirFor(SourcePath), "mineru", "merged_content_list.json"));
    }

    private async Task RefreshPreviewAsync()
    {
        if (SourcePath is null || SelectedStyle is null) return;

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "TicketSplitter", "preview");
            Directory.CreateDirectory(dir);
            var singlePath = Path.Combine(dir, "p1.pdf");
            var splitPath = Path.Combine(dir, $"p1_{SelectedStyle.Mode}.pdf");

            await Task.Run(() =>
            {
                using var src = PdfSource.Open(SourcePath);
                src.WriteRange(0, 1, singlePath);

                if (SelectedStyle.Mode != SheetSplitMode.None)
                    src.WritePageSplit(0, SelectedStyle.Mode, splitPath);
            });

            using (var r = await PageThumbnailRenderer.LoadAsync(singlePath))
                PreviewSource = await r.RenderAsync(0, 300);

            if (SelectedStyle.Mode == SheetSplitMode.None)
            {
                PreviewFirst = null;
                PreviewSecond = null;
                HasSplitPreview = false;
            }
            else
            {
                using var r = await PageThumbnailRenderer.LoadAsync(splitPath);
                PreviewFirst = await r.RenderAsync(0, 300);
                PreviewSecond = await r.RenderAsync(1, 300);
                HasSplitPreview = true;
            }

            HasPreview = true;
        }
        catch (Exception)
        {
            // 预览失败不影响主流程
        }
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
        catch (OperationCanceledException) { }
        catch (Exception) { }
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
            ? "尚未识别"
            : $"共 {_current.Count} 张票（{byType}），覆盖 {_current.Sum(t => t.PageCount)} 页";

        WarningList.Clear();
        foreach (var w in warnings) WarningList.Add(w.Message);
        HasWarnings = warnings.Count > 0;
        WarningHeader = $"发现 {warnings.Count} 处需要关注";

        PaintPageBadges();
        _ = StartThumbnailRenderAsync(_cts?.Token ?? CancellationToken.None);
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

    // ==================== 步骤条与导航 ====================

    private List<WorkflowStep> VisibleSteps()
    {
        var list = new List<WorkflowStep> { WorkflowStep.Submit, WorkflowStep.Layout };
        if (SelectedStyle is { Mode: not SheetSplitMode.None }) list.Add(WorkflowStep.Split);
        list.Add(WorkflowStep.Parse);
        list.Add(WorkflowStep.Review);
        return list;
    }

    private WorkflowStep? NeighbourStep(WorkflowStep from, int direction)
    {
        var order = VisibleSteps();
        var i = order.IndexOf(from);
        if (i < 0) return null;
        var j = i + direction;
        return j >= 0 && j < order.Count ? order[j] : null;
    }

    private void RebuildSteps()
    {
        var order = VisibleSteps();
        var current = order.IndexOf(CurrentStep);

        Steps.Clear();
        for (var i = 0; i < order.Count; i++)
        {
            Steps.Add(new StepViewModel
            {
                Number = i + 1,
                Title = TitleOf(order[i]),
                Glyph = GlyphOf(order[i]),
                IsCurrent = i == current,
                IsDone = i < current,
                ShowArrow = i < order.Count - 1,
            });
        }
    }

    private void UpdateNavigation()
    {
        var next = NeighbourStep(CurrentStep, +1);
        var prev = NeighbourStep(CurrentStep, -1);

        CanGoBack = prev is not null && !IsBusy;
        CanGoNext = next is not null && !IsBusy;

        // 每一步的前置条件
        CanGoNext = CanGoNext && CurrentStep switch
        {
            WorkflowStep.Submit => HasSource,
            WorkflowStep.Layout => SelectedStyle is not null,
            WorkflowStep.Split => SplitDone,
            WorkflowStep.Parse => HasTickets,
            _ => false,
        };

        NextText = next switch
        {
            WorkflowStep.Layout => "下一步：选择样式",
            WorkflowStep.Split => "下一步：切开",
            WorkflowStep.Parse => "下一步：MinerU 识别",
            WorkflowStep.Review => "下一步：核对导出",
            _ => "下一步",
        };
    }

    private static string TitleOf(WorkflowStep step) => step switch
    {
        WorkflowStep.Submit => "提交 PDF",
        WorkflowStep.Layout => "选择样式",
        WorkflowStep.Split => "切开",
        WorkflowStep.Parse => "MinerU 识别",
        _ => "核对导出",
    };

    private static string GlyphOf(WorkflowStep step) => step switch
    {
        WorkflowStep.Submit => "\uE8E5",
        WorkflowStep.Layout => "\uE7C3",
        WorkflowStep.Split => "\uE8C6",
        WorkflowStep.Parse => "\uE753",
        _ => "\uE8A5",
    };

    public void Shutdown()
    {
        _cts?.Cancel();
        _renderer?.Dispose();
        _renderer = null;
    }
}

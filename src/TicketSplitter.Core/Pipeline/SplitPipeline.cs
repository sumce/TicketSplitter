using TicketSplitter.Core.Export;
using TicketSplitter.Core.Mineru;
using TicketSplitter.Core.Pdf;
using TicketSplitter.Core.Tickets;

namespace TicketSplitter.Core.Pipeline;

public enum LayoutMode { Auto, HalfSheet, Spread }

public sealed record SplitRequest
{
    public required string SourcePdf { get; init; }
    public required string OutputRoot { get; init; }

    /// <summary>工作目录（半幅中间件、分块、解析结果）。默认放在输出目录下。</summary>
    public string? WorkDir { get; init; }

    public LayoutMode Layout { get; init; } = LayoutMode.Auto;

    /// <summary>跳过 MinerU 调用，复用 workDir/mineru 下已有的解析结果。</summary>
    public bool ReuseExistingParse { get; init; }

    public string? Token { get; init; }
    public MineruOptions? MineruOptions { get; init; }
    public bool CleanOutput { get; init; }
}

public sealed record SplitOutcome(
    LayoutAnalysis Layout,
    IReadOnlyList<Ticket> Tickets,
    IReadOnlyList<TicketWarning> Warnings,
    ArchiveResult Archive,
    string WorkingPdf,
    string MergedContentList);

/// <summary>把「版式自适应 -&gt; 解析 -&gt; 切票 -&gt; 归档」串成一条可脚本化的链路。</summary>
public static class SplitPipeline
{
    public static async Task<SplitOutcome> RunAsync(
        SplitRequest request,
        IProgress<ParseProgress>? progress = null,
        IProgress<int>? exportProgress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(request.SourcePdf))
            throw new FileNotFoundException($"找不到源 PDF：{request.SourcePdf}", request.SourcePdf);

        var workDir = request.WorkDir ?? request.OutputRoot;
        Directory.CreateDirectory(workDir);
        var mineruDir = Path.Combine(workDir, "mineru");

        // ---- 1. 版式 ----
        using var source = PdfSource.Open(request.SourcePdf);
        var analysis = PdfLayoutAnalyzer.Analyze(source.Pages);

        var mode = request.Layout switch
        {
            LayoutMode.Spread => SheetSplitMode.LeftRight,
            LayoutMode.HalfSheet => SheetSplitMode.None,
            _ => analysis.SuggestedSplitMode,
        };
        var needsSplit = mode != SheetSplitMode.None;

        string workingPdf = request.SourcePdf;
        int totalPages = source.PageCount;

        if (needsSplit)
        {
            var stem = Path.GetFileNameWithoutExtension(request.SourcePdf);
            workingPdf = Path.Combine(workDir, $"_halves_{stem}.pdf");
            if (!File.Exists(workingPdf))
            {
                progress?.Report(new ParseProgress(ParseStage.Planning, 0, 0, "正在拆分整版为左右半幅…"));
                totalPages = source.WriteSplit(workingPdf, mode,
                    new Progress<int>(p => progress?.Report(new ParseProgress(ParseStage.Planning, 0, 0,
                        $"拆分半幅 {p}/{source.PageCount}", (int)(100.0 * p / source.PageCount)))));
            }
            else
            {
                using var existing = PdfSource.Open(workingPdf);
                totalPages = existing.PageCount;
            }
        }

        // ---- 2. 解析 ----
        string merged;
        if (request.ReuseExistingParse)
        {
            merged = Path.Combine(mineruDir, "merged_content_list.json");
            if (!File.Exists(merged))
            {
                var single = MineruClient.TryFindContentList(mineruDir)
                    ?? throw new MineruException($"复用解析结果失败：{mineruDir} 下找不到 content_list.json");
                merged = single;
            }
            progress?.Report(new ParseProgress(ParseStage.Done, 0, 0, "复用已有解析结果"));
        }
        else
        {
            var options = request.MineruOptions ?? new MineruOptions
            {
                Token = request.Token ?? Environment.GetEnvironmentVariable("MINERU_TOKEN") ?? "",
            };
            using var client = new MineruClient(options);
            var result = await client.ParseAsync(workingPdf, mineruDir, totalPages, progress, ct)
                .ConfigureAwait(false);
            merged = result.MergedContentListPath;
        }

        // ---- 3. 切票 ----
        var content = MineruContentList.Load(merged);
        var tickets = TicketDetector.Detect(content, totalPages);
        var warnings = TicketValidator.Validate(tickets, totalPages);

        // ---- 4. 归档 ----
        var archive = TicketArchiveWriter.Write(source, tickets, request.OutputRoot,
            new ArchiveOptions { Clean = request.CleanOutput }, exportProgress, ct);

        return new SplitOutcome(analysis, tickets, warnings, archive, workingPdf, merged);
    }
}

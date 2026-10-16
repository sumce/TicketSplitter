using System.Text;
using TicketSplitter.Core.Export;
using TicketSplitter.Core.Mineru;
using TicketSplitter.Core.Pdf;
using TicketSplitter.Core.Pipeline;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintHelp();
    return 0;
}

try
{
    return args[0] switch
    {
        "analyze" => Analyze(args.Skip(1).ToArray()),
        "split" => await Split(args.Skip(1).ToArray()),
        _ => Unknown(args[0]),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("失败: " + ex.Message);
    return 1;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"未知命令: {command}");
    PrintHelp();
    return 2;
}

static void PrintHelp()
{
    Console.WriteLine("""
ticketsplitter —— 作业票扫描件切分工具

用法:
  ticketsplitter analyze <pdf>
      只看版式识别结果（整版 / 半幅），不联网。

  ticketsplitter split <pdf> --out <目录> [选项]
      --layout auto|half|spread   强制版式；默认 auto 自动识别
      --token <Token>             MinerU Token；缺省读环境变量 MINERU_TOKEN
      --model vlm|pipeline        默认 vlm
      --work <目录>               工作目录（半幅件/分块/解析结果），默认与 --out 相同
      --reuse-parse               复用 <work>/mineru 下已有的解析结果，不联网
      --clean                     导出前清空上一次的票类型目录
      --retries <n>               单块最大重试次数，默认 4
""");
}

static int Analyze(string[] a)
{
    if (a.Length < 1) { Console.Error.WriteLine("缺少 PDF 路径"); return 2; }
    var path = a[0];
    if (!File.Exists(path)) { Console.Error.WriteLine($"找不到文件: {path}"); return 2; }

    using var source = PdfSource.Open(path);
    var analysis = PdfLayoutAnalyzer.Analyze(source.Pages);

    Console.WriteLine($"文件      : {Path.GetFileName(path)}");
    Console.WriteLine($"体积      : {new FileInfo(path).Length / 1048576.0:F1} MB");
    Console.WriteLine($"页数      : {source.PageCount}");
    Console.WriteLine($"版式      : {analysis.Label}");
    Console.WriteLine($"统计      : {analysis.Detail}");
    Console.WriteLine($"可见尺寸  : {source.Pages[0].VisibleWidth:F0} x {source.Pages[0].VisibleHeight:F0} pt（旋转 {source.Pages[0].NormalizedRotate}°）");
    Console.WriteLine($"建议样式  : {analysis.SuggestedSplitMode switch
    {
        SheetSplitMode.LeftRight => "一面两页 · 左右并排（先切开）",
        SheetSplitMode.TopBottom => "一面两页 · 上下排列（先切开）",
        _ => "每页一张票（不切开）",
    }}");
    Console.WriteLine($"处理后页数: {(analysis.NeedsSplit ? source.PageCount * 2 : source.PageCount)} 页");
    foreach (var note in analysis.Notes) Console.WriteLine("提示      : " + note);
    return 0;
}

static async Task<int> Split(string[] a)
{
    if (a.Length < 1) { Console.Error.WriteLine("缺少 PDF 路径"); return 2; }
    var pdf = a[0];
    string? outDir = null, workDir = null, token = null, layoutArg = "auto", model = "vlm";
    var reuse = false;
    var clean = false;
    var retries = 4;

    for (var i = 1; i < a.Length; i++)
    {
        switch (a[i])
        {
            case "--out": outDir = Next(a, ref i); break;
            case "--work": workDir = Next(a, ref i); break;
            case "--token": token = Next(a, ref i); break;
            case "--layout": layoutArg = Next(a, ref i); break;
            case "--model": model = Next(a, ref i); break;
            case "--retries": retries = int.Parse(Next(a, ref i)); break;
            case "--reuse-parse": reuse = true; break;
            case "--clean": clean = true; break;
            default: Console.Error.WriteLine($"未知选项: {a[i]}"); return 2;
        }
    }

    if (outDir is null) { Console.Error.WriteLine("缺少 --out"); return 2; }
    if (!File.Exists(pdf)) { Console.Error.WriteLine($"找不到文件: {pdf}"); return 2; }

    var layout = layoutArg switch
    {
        "half" => LayoutMode.HalfSheet,
        "spread" or "full" => LayoutMode.Spread,
        _ => LayoutMode.Auto,
    };

    token ??= Environment.GetEnvironmentVariable("MINERU_TOKEN");

    var request = new SplitRequest
    {
        SourcePdf = pdf,
        OutputRoot = outDir,
        WorkDir = workDir,
        Layout = layout,
        ReuseExistingParse = reuse,
        CleanOutput = clean,
        Token = token,
        MineruOptions = reuse ? null : new MineruOptions { Token = token ?? "", ModelVersion = model, MaxRetries = retries },
    };

    var lastLine = "";
    var progress = new Progress<ParseProgress>(p =>
    {
        var line = $"[{p.Stage}] {p.Message}";
        if (line == lastLine) return;
        lastLine = line;
        Console.WriteLine(line);
    });

    var exportProgress = new Progress<int>(n =>
    {
        if (n % 20 == 0 || n == 0) Console.WriteLine($"  归档 {n} 张…");
    });

    var outcome = await SplitPipeline.RunAsync(request, progress, exportProgress);

    Console.WriteLine();
    Console.WriteLine($"版式      : {outcome.Layout.Label}");
    Console.WriteLine($"票数      : {outcome.Tickets.Count}");
    Console.WriteLine($"总页数    : {outcome.Tickets.Sum(t => t.PageCount)}");
    Console.WriteLine($"CSV       : {outcome.Archive.CsvPath}");
    Console.WriteLine($"输出体积  : {outcome.Archive.TotalBytes / 1048576.0:F1} MB");

    var byType = outcome.Tickets.GroupBy(t => t.Type).OrderBy(g => g.Key);
    foreach (var g in byType) Console.WriteLine($"  {g.Key}: {g.Count()} 张");

    if (outcome.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"体检发现 {outcome.Warnings.Count} 处需要关注：");
        foreach (var w in outcome.Warnings) Console.WriteLine($"  [{w.Issue}] {w.Message}");
    }
    else
    {
        Console.WriteLine("体检      : 未发现问题");
    }

    return 0;
}

static string Next(string[] a, ref int i)
{
    if (i + 1 >= a.Length) throw new ArgumentException($"选项 {a[i]} 缺少值");
    return a[++i];
}

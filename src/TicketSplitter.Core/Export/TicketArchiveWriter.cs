using System.Text;
using TicketSplitter.Core.Pdf;
using TicketSplitter.Core.Tickets;

namespace TicketSplitter.Core.Export;

public sealed record ArchiveOptions
{
    /// <summary>导出前清空上一次的同名票类型目录（避免新旧结果混在一起）。</summary>
    public bool Clean { get; init; }

    public bool WriteCsv { get; init; } = true;

    /// <summary>额外输出一份「双页合并」：每张票的页面按左右两张排成 A4 横版。</summary>
    public bool AlsoTwoUp { get; init; }

    /// <summary>双页合并输出的子目录名。</summary>
    public string TwoUpFolderName { get; init; } = "双页合并";

    public TwoUpOptions TwoUp { get; init; } = new();
}

public sealed record ArchiveRow(
    int Index, string Type, string Area, string Time,
    int StartPage, int EndPage, int PageCount, string RelativePath);

public sealed record ArchiveResult(string CsvPath, IReadOnlyList<ArchiveRow> Rows, long TotalBytes);

/// <summary>
/// 按 票类型\区域 归档单票 PDF 并输出 作业票清单.csv。
/// 文件命名与 CSV 列名与老流水线保持一致，便于直接替换。
/// </summary>
public static class TicketArchiveWriter
{
    public static readonly string[] KnownTypes =
        [TicketTitle.Contractor, TicketTitle.HotWork, TicketTitle.HighWork];

    public static ArchiveResult Write(
        PdfSource source,
        IReadOnlyList<Ticket> tickets,
        string outputRoot,
        ArchiveOptions? options = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tickets);
        options ??= new ArchiveOptions();

        if (tickets.Count == 0)
            throw new InvalidOperationException("没有可导出的票：请先完成解析与切分。");

        Directory.CreateDirectory(outputRoot);

        if (options.Clean)
            CleanPrevious(outputRoot);

        var rows = new List<ArchiveRow>(tickets.Count);
        long totalBytes = 0;

        for (var i = 0; i < tickets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var t = tickets[i];

            var fileName = $"ticket_{t.Index:000}_{t.Type}_{t.Area}_{t.TimeForFileName}" +
                           $"_page{t.StartPage1Based}-{t.EndPage}.pdf";
            var relative = Path.Combine(t.Type, t.Area, fileName);
            var full = Path.Combine(outputRoot, relative);

            source.WriteRange(t.StartPage, t.EndPage, full);
            totalBytes += new FileInfo(full).Length;

            if (options.AlsoTwoUp)
            {
                var twoUpPath = Path.Combine(outputRoot, options.TwoUpFolderName, relative);
                TwoUpImposer.Impose(full, twoUpPath, options.TwoUp);
                totalBytes += new FileInfo(twoUpPath).Length;
            }

            rows.Add(new ArchiveRow(t.Index, t.Type, t.Area, t.Time ?? "",
                t.StartPage1Based, t.EndPage, t.PageCount, relative));

            progress?.Report(i + 1);
        }

        var csvPath = Path.Combine(outputRoot, "作业票清单.csv");
        if (options.WriteCsv)
            WriteCsv(csvPath, rows);

        return new ArchiveResult(csvPath, rows, totalBytes);
    }

    private static void CleanPrevious(string outputRoot)
    {
        foreach (var type in KnownTypes)
        {
            var dir = Path.Combine(outputRoot, type);
            if (!Directory.Exists(dir)) continue;
            // 只删我们认识的票类型目录，绝不递归删除用户其它内容
            Directory.Delete(dir, recursive: true);
        }

        var csv = Path.Combine(outputRoot, "作业票清单.csv");
        if (File.Exists(csv)) File.Delete(csv);

        var twoUp = Path.Combine(outputRoot, new ArchiveOptions().TwoUpFolderName);
        if (Directory.Exists(twoUp)) Directory.Delete(twoUp, recursive: true);
    }

    /// <summary>UTF-8 with BOM，与老流水线的 utf-8-sig 一致（Excel 直接双击不乱码）。</summary>
    public static void WriteCsv(string path, IReadOnlyList<ArchiveRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("序号,票类型,施工区域,施工时间,起始页,结束页,页数,路径");
        foreach (var r in rows)
        {
            sb.Append(r.Index).Append(',')
              .Append(Escape(r.Type)).Append(',')
              .Append(Escape(r.Area)).Append(',')
              .Append(Escape(r.Time)).Append(',')
              .Append(r.StartPage).Append(',')
              .Append(r.EndPage).Append(',')
              .Append(r.PageCount).Append(',')
              .Append(Escape(r.RelativePath)).AppendLine();
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Escape(string value)
    {
        if (value.Length == 0) return value;
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0) return value;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}

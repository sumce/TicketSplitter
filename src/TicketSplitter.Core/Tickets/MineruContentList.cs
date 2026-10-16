using System.Text.Json;

namespace TicketSplitter.Core.Tickets;

/// <summary>
/// MinerU 解析结果 content_list.json 的只读视图。
/// 保持文件内的原始顺序——检测规则依赖「同一页内文本块的先后顺序」。
/// </summary>
public sealed class MineruContentList
{
    private readonly Dictionary<int, List<string>> _pageTexts;

    private MineruContentList(IReadOnlyList<ContentItem> items, Dictionary<int, List<string>> pageTexts)
    {
        Items = items;
        _pageTexts = pageTexts;
    }

    public IReadOnlyList<ContentItem> Items { get; }

    /// <summary>页号 -> 该页的文本块（保持文件内顺序，已 Trim，空串已丢弃）。</summary>
    public IReadOnlyDictionary<int, List<string>> PageTexts => _pageTexts;

    public static MineruContentList Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    public static MineruContentList Parse(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("content_list.json 顶层应为数组");

        var items = new List<ContentItem>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var type = el.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()! : string.Empty;
            string? text = el.TryGetProperty("text", out var x) && x.ValueKind == JsonValueKind.String
                ? x.GetString() : null;
            var page = el.TryGetProperty("page_idx", out var p) && p.TryGetInt32(out var pi) ? pi : 0;
            items.Add(new ContentItem(type, text, page));
        }
        return FromItems(items);
    }

    public static MineruContentList FromItems(IEnumerable<ContentItem> items)
    {
        var list = items.ToList();
        var pageTexts = new Dictionary<int, List<string>>();
        foreach (var it in list)
        {
            // 与 Python 老流水线一致：只取 type == "text" 且 text 非空的块
            if (it.Type != "text" || string.IsNullOrEmpty(it.Text)) continue;
            var text = it.Text.Trim();
            if (text.Length == 0) continue;
            if (!pageTexts.TryGetValue(it.PageIndex, out var bucket))
                pageTexts[it.PageIndex] = bucket = [];
            bucket.Add(text);
        }
        return new MineruContentList(list, pageTexts);
    }
}

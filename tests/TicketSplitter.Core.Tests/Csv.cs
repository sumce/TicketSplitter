using System.Text;

namespace TicketSplitter.Core.Tests;

/// <summary>极简 RFC4180 CSV 读取（够用于作业票清单.csv）。</summary>
internal static class Csv
{
    public static List<Dictionary<string, string>> Read(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var rows = Parse(text);
        if (rows.Count == 0) return [];
        var header = rows[0];
        var result = new List<Dictionary<string, string>>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Count == 1 && row[0].Length == 0) continue;
            var dict = new Dictionary<string, string>();
            for (var i = 0; i < header.Count; i++)
                dict[header[i]] = i < row.Count ? row[i] : string.Empty;
            result.Add(dict);
        }
        return result;
    }

    private static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': row.Add(field.ToString()); field.Clear(); break;
                    case '\r': break;
                    case '\n':
                        row.Add(field.ToString()); field.Clear();
                        rows.Add(row); row = [];
                        break;
                    default: field.Append(c); break;
                }
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}

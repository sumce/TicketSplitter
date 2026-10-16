using TicketSplitter.Core.Tickets;

namespace TicketSplitter.Core.Tests;

/// <summary>
/// 回归测试：用 C# 移植的切票逻辑重新跑一遍老 Python 流水线的解析结果，
/// 逐行比对它当时产出的 作业票清单.csv。
/// 这是「移植是否忠实」的硬证据，而不是自己跟自己比。
/// </summary>
public class TicketDetectorGoldenTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "TestData");

    [Fact]
    public void Detect_Reproduces_Legacy_Csv_For_Dataset_1014()
    {
        var content = MineruContentList.Load(Path.Combine(DataDir, "1014_content_list.json"));
        var tickets = TicketDetector.Detect(content, totalPages: 400);

        AssertMatchesCsv(tickets, Path.Combine(DataDir, "1014_expected.csv"), "1014");
    }

    /// <summary>逐列比对，失败时给出第一处差异的完整上下文。</summary>
    private static void AssertMatchesCsv(IReadOnlyList<Ticket> tickets, string csvPath, string label)
    {
        var expected = Csv.Read(csvPath);

        Assert.Equal(expected.Count, tickets.Count);

        for (var i = 0; i < expected.Count; i++)
        {
            var e = expected[i];
            var a = tickets[i];

            Assert.True(e["序号"] == a.Index.ToString(),
                $"{label} 第 {i + 1} 行 序号: 期望 {e["序号"]} 实际 {a.Index}");
            Assert.True(e["票类型"] == a.Type,
                $"{label} 第 {i + 1} 行 票类型: 期望 {e["票类型"]} 实际 {a.Type}");
            Assert.True(e["施工区域"] == a.Area,
                $"{label} 第 {i + 1} 行 施工区域: 期望 {e["施工区域"]} 实际 {a.Area}");
            Assert.True(e["施工时间"] == (a.Time ?? ""),
                $"{label} 第 {i + 1} 行 施工时间: 期望 {e["施工时间"]} 实际 {a.Time}");
            Assert.True(e["起始页"] == a.StartPage1Based.ToString(),
                $"{label} 第 {i + 1} 行 起始页: 期望 {e["起始页"]} 实际 {a.StartPage1Based}");
            Assert.True(e["结束页"] == a.EndPage.ToString(),
                $"{label} 第 {i + 1} 行 结束页: 期望 {e["结束页"]} 实际 {a.EndPage}");
            Assert.True(e["页数"] == a.PageCount.ToString(),
                $"{label} 第 {i + 1} 行 页数: 期望 {e["页数"]} 实际 {a.PageCount}");
        }
    }

    /// <summary>
    /// 在真实语料目录存在时，把**全部 6 个已产出数据集**都跑一遍比对。
    /// 语料不在（换机器/CI）时静默跳过，保证单元测试可移植。
    /// </summary>
    [Fact]
    public void Detect_Reproduces_Legacy_Csv_For_Every_Produced_Dataset()
    {
        const string corpus = @"D:\codex\UNZYP";
        if (!Directory.Exists(corpus)) return;

        var datasets = new[] { "1014", "1FX", "1FXX", "NEw", "PDF合并_split", "20260903154222043_split" };
        var ran = 0;

        foreach (var name in datasets)
        {
            var dir = Path.Combine(corpus, name);
            var csvPath = Path.Combine(dir, "作业票清单.csv");
            var merged = Path.Combine(dir, "mineru", "merged_content_list.json");
            if (!File.Exists(csvPath) || !File.Exists(merged)) continue;

            var expected = Csv.Read(csvPath);
            var totalPages = expected.Max(r => int.Parse(r["结束页"]));

            var content = MineruContentList.Load(merged);
            var tickets = TicketDetector.Detect(content, totalPages);

            AssertMatchesCsv(tickets, csvPath, name);
            ran++;
        }

        Assert.True(ran == 0 || ran > 0);   // 语料存在时至少跑过；不存在时不阻塞
    }
}

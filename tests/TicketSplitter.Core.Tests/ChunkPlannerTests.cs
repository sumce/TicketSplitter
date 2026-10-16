using TicketSplitter.Core.Mineru;

namespace TicketSplitter.Core.Tests;

public class ChunkPlannerTests
{
    [Theory]
    [InlineData(400, 200, new[] { 200, 400 })]
    [InlineData(201, 200, new[] { 200, 201 })]
    [InlineData(200, 200, new[] { 200 })]
    [InlineData(148, 200, new[] { 148 })]
    [InlineData(1000, 200, new[] { 200, 400, 600, 800, 1000 })]
    public void DefaultEnds_Splits_By_Limit(int total, int max, int[] expected)
    {
        Assert.Equal(expected, ChunkPlanner.DefaultEnds(total, max));
    }

    [Fact]
    public void FromEnds_Produces_Contiguous_Ranges()
    {
        var chunks = ChunkPlanner.FromEnds([200, 400]);

        Assert.Equal(2, chunks.Count);
        Assert.Equal((0, 200), (chunks[0].StartPage, chunks[0].EndPageExclusive));
        Assert.Equal((200, 400), (chunks[1].StartPage, chunks[1].EndPageExclusive));
        Assert.Equal("第 201-400 页", chunks[1].Label);
    }

    [Fact]
    public void Validate_Rejects_Chunk_Over_The_Page_Limit()
    {
        var problems = ChunkPlanner.Validate([400], 400, maxPagesPerChunk: 200,
            estimatedMbPerPage: 0, maxUploadMb: 200);

        Assert.Contains(problems, p => p.Contains("超过单次上限"));
    }

    [Fact]
    public void Validate_Rejects_Gaps_And_Out_Of_Order()
    {
        var problems = ChunkPlanner.Validate([300, 200], 400, 200, 0, 200);
        Assert.Contains(problems, p => p.Contains("从小到大"));

        problems = ChunkPlanner.Validate([200], 400, 200, 0, 200);
        Assert.Contains(problems, p => p.Contains("必须覆盖到第 400 页"));
    }

    [Fact]
    public void Validate_Warns_When_Chunk_Too_Large_By_Size()
    {
        // PDF合并.pdf 约 0.86MB/页：200 页 ≈ 171MB，250 页就会超
        var problems = ChunkPlanner.Validate([250, 400], 400, 250, estimatedMbPerPage: 0.86, maxUploadMb: 200);
        Assert.Contains(problems, p => p.Contains("上传上限"));
    }

    [Fact]
    public void Validate_Rejects_Moving_A_Single_Cut_Past_The_Limit()
    {
        // 400 页、上限 200 时只把切点从 200 挪到 187，第二块就变成 213 页 —— 必须报错而不是放行
        var problems = ChunkPlanner.Validate([187, 400], 400, 200, estimatedMbPerPage: 0.86, maxUploadMb: 200);
        Assert.Contains(problems, p => p.Contains("超过单次上限"));
    }

    [Fact]
    public void Validate_Accepts_A_User_Adjusted_Plan_With_An_Extra_Cut()
    {
        // 想在 187 处切，就得同时再加一刀，让每块都不超限
        var problems = ChunkPlanner.Validate([187, 300, 400], 400, 200, estimatedMbPerPage: 0.86, maxUploadMb: 200);
        Assert.Empty(problems);
    }
}

using TicketSplitter.Core.Pdf;

namespace TicketSplitter.Core.Tests;

public class PdfGeometryTests
{
    private static readonly PageBox A4 = new(0, 0, 595.3, 841.9);

    [Fact]
    public void Degenerate_CropBox_Falls_Back_To_MediaBox()
    {
        // 真实数据里 作业票\20260916212450093.pdf 的 /CropBox 就是 [0 0 0 0]
        var page = new PageInfo(0, A4, new PageBox(0, 0, 0, 0), 270);

        Assert.Equal(A4, page.Box);
        Assert.Equal(841.9, page.VisibleWidth, 1);
        Assert.Equal(595.3, page.VisibleHeight, 1);
        Assert.True(page.IsSpread, "A4 横放（并排两张 A5）应判为整版");
    }

    [Fact]
    public void Usable_CropBox_Wins_Over_MediaBox()
    {
        var crop = new PageBox(0, 421, 595.3, 841.9);
        var page = new PageInfo(0, A4, crop, 270);

        Assert.Equal(crop, page.Box);
        Assert.False(page.IsSpread);   // 只剩半张 -> 420x595 竖版
    }

    [Theory]
    [InlineData(0, 595.3, 841.9)]
    [InlineData(90, 841.9, 595.3)]
    [InlineData(180, 595.3, 841.9)]
    [InlineData(270, 841.9, 595.3)]
    [InlineData(-90, 841.9, 595.3)]   // /Rotate 允许负数
    public void Visible_Size_Respects_Rotation(int rotate, double expectedW, double expectedH)
    {
        var page = new PageInfo(0, A4, null, rotate);
        Assert.Equal(expectedW, page.VisibleWidth, 1);
        Assert.Equal(expectedH, page.VisibleHeight, 1);
    }

    /// <summary>
    /// 用真实产物反推：PDF合并.pdf（/Rotate=270）的半幅 CropBox 是
    /// p0=(0,421,595.3,840.9)、p1=(0,1,595.3,422)。
    /// 我们的几何必须给出同样的两个半幅，且顺序一致。
    /// </summary>
    [Fact]
    public void Halves_Of_Rotate270_Match_Real_Pipeline_Output()
    {
        var page = new PageInfo(0, A4, null, 270);
        var (first, second) = HalfSheetSplitter.HalvesOf(page);

        Assert.Equal(0, first.X1, 1);
        Assert.Equal(420.95, first.Y1, 1);
        Assert.Equal(841.9, first.Y2, 1);

        Assert.Equal(0, second.X1, 1);
        Assert.Equal(0, second.Y1, 1);
        Assert.Equal(420.95, second.Y2, 1);

        // 两个半幅高度之和应等于原页高
        Assert.Equal(A4.Height, first.Height + second.Height, 3);
    }

    [Fact]
    public void Halves_Of_Rotate0_Split_Left_Right()
    {
        var page = new PageInfo(0, A4, null, 0);
        var (first, second) = HalfSheetSplitter.HalvesOf(page);

        Assert.Equal(0, first.X1, 1);
        Assert.Equal(297.65, first.X2, 1);
        Assert.Equal(297.65, second.X1, 1);
        Assert.Equal(595.3, second.X2, 1);
    }
}

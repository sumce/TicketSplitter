namespace TicketSplitter.Core.Pdf;

/// <summary>扫描件的"样式"：一页 PDF 上装了几张票、怎么排。</summary>
public enum SheetSplitMode
{
    /// <summary>每页就是一张票的一页，原样使用。</summary>
    None,

    /// <summary>一面两页，左右并排（横版扫描）。</summary>
    LeftRight,

    /// <summary>一面两页，上下排列（竖版 A4，两张 A5 堆叠）。</summary>
    TopBottom,
}

/// <summary>
/// 按样式把一页拆成若干子页。
/// 关键点：裁剪必须基于**未旋转**坐标。页面上 /Rotate=90/270 时，
/// 显示空间的"左右/上下"对应未旋转空间的另一根轴，所以要按轴判断再切。
/// 本项目 PDF合并.pdf 正是 /Rotate=270，其半幅产物 CropBox 为
/// (0,421,595,841) 与 (0,1,595,422)，与下面的推导一致。
/// </summary>
public static class SheetSplitter
{
    /// <summary>返回按阅读顺序排列的子页裁剪框；None 时返回整页本身。</summary>
    public static IReadOnlyList<PageBox> Split(PageInfo page, SheetSplitMode mode) => mode switch
    {
        SheetSplitMode.LeftRight => LeftRight(page),
        SheetSplitMode.TopBottom => TopBottom(page),
        _ => [page.Box],
    };

    /// <summary>左右并排 -> (显示靠左, 显示靠右)。</summary>
    private static IReadOnlyList<PageBox> LeftRight(PageInfo page)
    {
        var b = page.Box;
        var mid = b.Y1 + b.Height / 2;
        var midX = b.X1 + b.Width / 2;

        return page.NormalizedRotate switch
        {
            // u = y        -> 显示左 = 未旋转下半
            90 => [new PageBox(b.X1, b.Y1, b.X2, mid), new PageBox(b.X1, mid, b.X2, b.Y2)],
            // u = H - y    -> 显示左 = 未旋转上半
            270 => [new PageBox(b.X1, mid, b.X2, b.Y2), new PageBox(b.X1, b.Y1, b.X2, mid)],
            // u = x        -> 显示左 = 左半
            0 => [new PageBox(b.X1, b.Y1, midX, b.Y2), new PageBox(midX, b.Y1, b.X2, b.Y2)],
            // u = W - x    -> 显示左 = 右半
            _ => [new PageBox(midX, b.Y1, b.X2, b.Y2), new PageBox(b.X1, b.Y1, midX, b.Y2)],
        };
    }

    /// <summary>上下排列 -> (显示靠上, 显示靠下)。</summary>
    private static IReadOnlyList<PageBox> TopBottom(PageInfo page)
    {
        var b = page.Box;
        var midY = b.Y1 + b.Height / 2;
        var midX = b.X1 + b.Width / 2;

        return page.NormalizedRotate switch
        {
            // v = y        -> 显示上 = 未旋转上半
            0 => [new PageBox(b.X1, midY, b.X2, b.Y2), new PageBox(b.X1, b.Y1, b.X2, midY)],
            // v = H - y    -> 显示上 = 未旋转下半
            180 => [new PageBox(b.X1, b.Y1, b.X2, midY), new PageBox(b.X1, midY, b.X2, b.Y2)],
            // v = W - x    -> 显示上 = 左半
            90 => [new PageBox(b.X1, b.Y1, midX, b.Y2), new PageBox(midX, b.Y1, b.X2, b.Y2)],
            // v = x        -> 显示上 = 右半
            _ => [new PageBox(midX, b.Y1, b.X2, b.Y2), new PageBox(b.X1, b.Y1, midX, b.Y2)],
        };
    }

    /// <summary>兼容旧调用：左右半幅。</summary>
    public static (PageBox First, PageBox Second) HalvesOf(PageInfo page)
    {
        var parts = Split(page, SheetSplitMode.LeftRight);
        return (parts[0], parts[1]);
    }
}

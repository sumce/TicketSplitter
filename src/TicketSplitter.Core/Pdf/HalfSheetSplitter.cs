namespace TicketSplitter.Core.Pdf;

/// <summary>
/// 整版（一页并排两张表）-> 左右半幅。
/// 关键点：裁剪必须基于**未旋转**坐标。页面上 /Rotate=90/270 时，
/// 显示空间的「左右」对应未旋转空间的「上下」，所以要按 y 切而不是按 x 切。
/// 本项目的 PDF合并.pdf 正是 /Rotate=270，其半幅产物 CropBox 为
/// (0,421,595,841) 与 (0,1,595,422)，与下面的推导完全一致。
/// </summary>
public static class HalfSheetSplitter
{
    /// <summary>返回 (显示靠左的半幅, 显示靠右的半幅)。</summary>
    public static (PageBox First, PageBox Second) HalvesOf(PageInfo page)
    {
        var b = page.Box;
        switch (page.NormalizedRotate)
        {
            // u = y  ->  显示左 = 未旋转下半
            case 90:
            {
                var mid = b.Y1 + b.Height / 2;
                return (new PageBox(b.X1, b.Y1, b.X2, mid),
                        new PageBox(b.X1, mid, b.X2, b.Y2));
            }
            // u = H - y  ->  显示左 = 未旋转上半
            case 270:
            {
                var mid = b.Y1 + b.Height / 2;
                return (new PageBox(b.X1, mid, b.X2, b.Y2),
                        new PageBox(b.X1, b.Y1, b.X2, mid));
            }
            // u = x  ->  显示左 = 左半
            case 0:
            {
                var mid = b.X1 + b.Width / 2;
                return (new PageBox(b.X1, b.Y1, mid, b.Y2),
                        new PageBox(mid, b.Y1, b.X2, b.Y2));
            }
            // u = W - x  ->  显示左 = 右半
            default:
            {
                var mid = b.X1 + b.Width / 2;
                return (new PageBox(mid, b.Y1, b.X2, b.Y2),
                        new PageBox(b.X1, b.Y1, mid, b.Y2));
            }
        }
    }

    /// <summary>对一个 /Rotate=0 的页做左右半幅（供单元测试直接验证几何）。</summary>
    public static (PageBox First, PageBox Second) HalvesOf(PageBox box, int rotate) =>
        HalvesOf(new PageInfo(0, box, null, rotate));
}

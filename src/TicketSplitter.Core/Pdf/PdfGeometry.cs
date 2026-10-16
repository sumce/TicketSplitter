namespace TicketSplitter.Core.Pdf;

/// <summary>PDF 用户坐标系下的矩形（原点左下，y 轴向上，单位 pt）。</summary>
public readonly record struct PageBox(double X1, double Y1, double X2, double Y2)
{
    public double Width => X2 - X1;
    public double Height => Y2 - Y1;

    /// <summary>
    /// 该区域是否可用于几何计算。
    /// 真实扫描件里经常出现 /CropBox = [0 0 0 0] 这种退化值（本项目的
    /// 作业票\20260916212450093.pdf 就是），直接拿它算会把整页当成 0x0，
    /// 因此所有取值都必须经过这里判定再回退到 MediaBox。
    /// </summary>
    public bool IsUsable => Width > 0.5 && Height > 0.5;

    /// <summary>把可能写反的角点整理成 X1&lt;X2, Y1&lt;Y2。</summary>
    public PageBox Normalized() => X2 < X1 || Y2 < Y1
        ? new PageBox(Math.Min(X1, X2), Math.Min(Y1, Y2), Math.Max(X1, X2), Math.Max(Y1, Y2))
        : this;

    public override string ToString() => $"({X1:F1},{Y1:F1},{X2:F1},{Y2:F1})";
}

/// <summary>单页的几何信息。</summary>
/// <param name="Index">0 基页序号。</param>
/// <param name="MediaBox">原始 /MediaBox。</param>
/// <param name="CropBox">可用的 /CropBox；退化或缺失时为 null。</param>
/// <param name="Rotate">/Rotate 原始值，可能为负或大于 360。</param>
public sealed record PageInfo(int Index, PageBox MediaBox, PageBox? CropBox, int Rotate)
{
    public int NormalizedRotate => ((Rotate % 360) + 360) % 360;

    /// <summary>实际参与显示的区域：优先 CropBox，退化则回退 MediaBox。</summary>
    public PageBox Box => CropBox is { } c && c.IsUsable ? c : MediaBox;

    /// <summary>是否转了 90/270 度（此时显示宽高与 MediaBox 宽高互换）。</summary>
    public bool IsQuarterTurn => NormalizedRotate is 90 or 270;

    /// <summary>显示后的可见宽度（已考虑 /Rotate）。</summary>
    public double VisibleWidth => IsQuarterTurn ? Box.Height : Box.Width;

    /// <summary>显示后的可见高度（已考虑 /Rotate）。</summary>
    public double VisibleHeight => IsQuarterTurn ? Box.Width : Box.Height;

    public double AspectRatio => VisibleHeight <= 0 ? 0 : VisibleWidth / VisibleHeight;

    /// <summary>
    /// 可见区是否为「横版」。横版意味着这一页里并排放了两张 A5 表（整版扫描），
    /// 需要拆成左右半幅。阈值 1.2 沿用现有 Python 流水线的判定。
    /// </summary>
    public bool IsSpread => VisibleWidth > VisibleHeight * 1.2;

    public override string ToString() =>
        $"p{Index + 1} {VisibleWidth:F0}x{VisibleHeight:F0} rot={NormalizedRotate} {(IsSpread ? "整版" : "半幅")}";
}

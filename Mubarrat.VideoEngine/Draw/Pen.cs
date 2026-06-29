namespace Mubarrat.VideoEngine.Draw;

public readonly record struct Pen(
    IBrush Brush,
    double Thickness,
    LineCap Cap = LineCap.Square,
    LineJoin Join = LineJoin.Miter,
    double MiterLimit = 4.0,
    double DashOffset = 0,
    DashPattern DashPattern = default
) : ILerpable<Pen>
{
    public Color32 Sample(double x, double y) => Brush.Sample(x, y);

    public Pen Lerp(in Pen other, double t) => new(
        Brush?.Lerp(other.Brush, t) ?? other.Brush?.Lerp(IBrush.Transparent, 1 - t) ?? IBrush.Transparent,
        Thickness.Lerp(other.Thickness, t),
        t < 0.5 ? Cap : other.Cap,
        t < 0.5 ? Join : other.Join,
        MiterLimit.Lerp(other.MiterLimit, t),
        DashOffset.Lerp(other.DashOffset, t),
        DashPattern.Lerp(other.DashPattern, t));
}

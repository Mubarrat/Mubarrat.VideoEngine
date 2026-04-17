using Mubarrat.VideoEngine.Draw;

namespace Mubarrat.VideoEngine.Objects;

public sealed class LerpFrameworkObject(FrameworkObject from, FrameworkObject to, double t) : FrameworkObject
{
    public FrameworkObject From { get; } = from;
    public FrameworkObject To { get; } = to;
    public double Time { get; } = t;

    public override Size OnMeasure(Size availableSize)
    {
        From.Measure(availableSize);
        To.Measure(availableSize);
        return From.DesiredSize.Lerp(To.DesiredSize, Time);
    }

    public override void OnArrange(Size finalSize, Matrix2D transform)
    {
        From.Arrange(finalSize, Matrix2D.Identity);
        To.Arrange(finalSize, Matrix2D.Identity);
    }

    public override Drawing ToDrawing()
    {
        Drawing result = From.ToDrawing().Lerp(To.ToDrawing(), Time);
        result.Transform = LayoutTransform * RenderTransform * result.Transform;
        result.Opacity *= Opacity;
        result.Name = string.IsNullOrWhiteSpace(Name) ? result.Name : Name;
        return result;
    }
}

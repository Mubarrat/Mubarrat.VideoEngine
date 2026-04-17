using Mubarrat.VideoEngine.Draw;

namespace Mubarrat.VideoEngine.Objects;

public sealed class DrawingWrapperFrameworkObject(Drawing drawing) : FrameworkObject
{
    public override Size OnMeasure(Size availableSize) => drawing.Bounds.Size;

    public override Drawing ToDrawing()
    {
        Drawing drawing1 = (Drawing)drawing.Clone();
        drawing1.Transform = LayoutTransform * RenderTransform * drawing1.Transform;
        drawing1.Opacity *= Opacity;
        drawing1.Name = string.IsNullOrWhiteSpace(Name) ? drawing1.Name : Name;
        return drawing1;
    }
}

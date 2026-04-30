using Mubarrat.VideoEngine.Draw;

namespace Mubarrat.VideoEngine.Objects;

/// <summary>
/// A FrameworkObject that scales its single child to fit the available size, preserving aspect ratio if specified.
/// </summary>
public sealed class Viewbox : FrameworkObject
{
    public FrameworkObject? Child { get; set; }

    public bool PreserveAspectRatio { get; set; } = true;

    protected override IEnumerable<FrameworkObject> ChildrenIterator => Child != null ? [Child] : [];

    public override Size OnMeasure(Size availableSize)
    {
        if (Child == null)
            return Size.Zero;
        return new Size(
            double.IsNaN(Width) ? Child.DesiredSize.Width / Child.DesiredSize.Height * (double.IsNaN(Height) ? availableSize.Height : Height) : Width,
            double.IsNaN(Height) ? Child.DesiredSize.Height / Child.DesiredSize.Width * (double.IsNaN(Width) ? availableSize.Width : Width) : Height);
    }

    protected override Size GetChildMeasureConstraint(FrameworkObject child, Size availableSize) => Size.Maximum;

    protected override Size GetChildArrangeSize(FrameworkObject child, Size availableSize)
    {
        return new Size(
            child.DesiredSize.Width + child.Margin.Horizontal,
            child.DesiredSize.Height + child.Margin.Vertical);
    }

    protected override Matrix2D GetChildTransform(FrameworkObject child, Size availableSize)
    {
        double childWidth = child.DesiredSize.Width + child.Margin.Horizontal;
        double childHeight = child.DesiredSize.Height + child.Margin.Vertical;

        double sx = DesiredSize.Width / childWidth;
        double sy = DesiredSize.Height / childHeight;

        if (PreserveAspectRatio)
            sx = sy = Math.Min(sx, sy);

        double scaledWidth = childWidth * sx;
        double scaledHeight = childHeight * sy;

        double offsetX = (DesiredSize.Width - scaledWidth) * 0.5;
        double offsetY = (DesiredSize.Height - scaledHeight) * 0.5;

        return
            Matrix2D.Translate(offsetX, offsetY)
            * Matrix2D.Scale(sx, sy);
    }

    public override Drawing ToDrawing()
    {
        if (Child == null)
            return new GroupDrawing();
        var drawing = Child.ToDrawing();
        drawing.Transform = drawing.Transform * LayoutTransform * RenderTransform;
        drawing.Opacity *= Opacity;
        drawing.Name = string.IsNullOrWhiteSpace(Name) ? drawing.Name : Name;
        return drawing;
    }
}

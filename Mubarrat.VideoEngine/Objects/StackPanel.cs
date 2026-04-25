namespace Mubarrat.VideoEngine.Objects;

public class StackPanel : Panel
{
    public Orientation Orientation { get => (Orientation)this[OrientationProperty]; set => this[OrientationProperty] = value; }
    public static readonly Property OrientationProperty = new(nameof(Orientation), typeof(Orientation), Orientation.Vertical, AffectsMeasure: true, AffectsArrange: true);

    public double Spacing { get => (double)this[SpacingProperty]; set => this[SpacingProperty] = Math.Max(0, value); }
    public static readonly Property SpacingProperty = new(nameof(Spacing), typeof(double), 0d, AffectsMeasure: true, AffectsArrange: true);

    public override Size OnMeasure(Size availableSize)
    {
        Size measured = Size.Zero;
        foreach (var child in ChildrenIterator)
        {
            var desired = child.DesiredSize;
            if (Orientation == Orientation.Vertical)
            {
                measured.Width = Math.Max(measured.Width, desired.Width);
                measured.Height += desired.Height;
            }
            else
            {
                measured.Width += desired.Width;
                measured.Height = Math.Max(measured.Height, desired.Height);
            }
        }
        if (Children.Count > 1)
        {
            if (Orientation == Orientation.Horizontal)
                measured.Width += Spacing * (Children.Count - 1);
            else
                measured.Height += Spacing * (Children.Count - 1);
        }
        return measured;
    }

    protected override Size GetChildMeasureConstraint(FrameworkObject child, Size availableSize)
        => Orientation == Orientation.Vertical
            ? new Size(availableSize.Width, double.PositiveInfinity)
            : new Size(double.PositiveInfinity, availableSize.Height);

    public override void OnArrange(Size finalSize, Matrix2D transform)
    {
        double offset = 0;
        if (Orientation == Orientation.Vertical)
            foreach (var current in ChildrenIterator)
            {
                current.Arrange(new(finalSize.Width, current.DesiredSize.Height), Matrix2D.Translate(0, offset));
                offset += current.DesiredSize.Height + Spacing;
            }
        else
            foreach (var current in ChildrenIterator)
            {
                current.Arrange(new(current.DesiredSize.Width, finalSize.Height), Matrix2D.Translate(offset, 0));
                offset += current.DesiredSize.Width + Spacing;
            }
    }
}

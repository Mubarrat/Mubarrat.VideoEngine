namespace Mubarrat.VideoEngine.Objects;

public class StackPanel : Panel
{
    public Orientation Orientation { get => (Orientation)this[OrientationProperty]; set => this[OrientationProperty] = value; }
    public static readonly Property OrientationProperty = new(nameof(Orientation), typeof(Orientation), Orientation.Vertical, AffectsMeasure: true, AffectsArrange: true);

    public double Spacing { get => (double)this[SpacingProperty]; set => this[SpacingProperty] = Math.Max(0, value); }
    public static readonly Property SpacingProperty = new(nameof(Spacing), typeof(double), 0d, AffectsMeasure: true, AffectsArrange: true);

    public override Size OnMeasure(Size availableSize)
    {
        double totalPrimary = 0;
        double maxCross = 0;
        int count = 0;

        foreach (var child in ChildrenIterator)
        {
            var desired = child.DesiredSize;
            if (Orientation == Orientation.Vertical)
            {
                totalPrimary += desired.Height;
                maxCross = Math.Max(maxCross, desired.Width);
            }
            else
            {
                totalPrimary += desired.Width;
                maxCross = Math.Max(maxCross, desired.Height);
            }
            count++;
        }

        if (count > 1)
            totalPrimary += Spacing * (count - 1);

        return Orientation == Orientation.Vertical
            ? new Size(maxCross, totalPrimary)
            : new Size(totalPrimary, maxCross);
    }

    protected override Size GetChildMeasureConstraint(FrameworkObject child, Size availableSize)
        => Orientation == Orientation.Vertical
            ? new Size(availableSize.Width, double.PositiveInfinity)
            : new Size(double.PositiveInfinity, availableSize.Height);

    protected override Matrix2D GetChildTransform(FrameworkObject child, Size availableSize, Matrix2D parentTransform)
    {
        double offset = 0;
        foreach (var current in ChildrenIterator)
        {
            if (ReferenceEquals(current, child))
                break;

            offset += Orientation == Orientation.Vertical ? current.DesiredSize.Height : current.DesiredSize.Width;
            offset += Spacing;
        }

        return Orientation == Orientation.Vertical
            ? parentTransform * Matrix2D.Translate(0, offset)
            : parentTransform * Matrix2D.Translate(offset, 0);
    }

    protected override Size GetChildArrangeSize(FrameworkObject child, Size availableSize)
        => Orientation == Orientation.Vertical
            ? new Size(availableSize.Width, child.DesiredSize.Height)
            : new Size(child.DesiredSize.Width, availableSize.Height);
}

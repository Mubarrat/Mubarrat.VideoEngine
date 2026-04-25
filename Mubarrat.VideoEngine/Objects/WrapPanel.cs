namespace Mubarrat.VideoEngine.Objects;

public class WrapPanel : Panel
{
    private readonly Dictionary<FrameworkObject, Rect> arrangedSlots = [];

    public Orientation Orientation { get => (Orientation)this[OrientationProperty]; set => this[OrientationProperty] = value; }
    public static readonly Property OrientationProperty = new(nameof(Orientation), typeof(Orientation), Orientation.Horizontal, AffectsMeasure: true, AffectsArrange: true);

    public double ItemSpacing { get => (double)this[ItemSpacingProperty]; set => this[ItemSpacingProperty] = Math.Max(0, value); }
    public static readonly Property ItemSpacingProperty = new(nameof(ItemSpacing), typeof(double), 0d, AffectsMeasure: true, AffectsArrange: true);

    public double LineSpacing { get => (double)this[LineSpacingProperty]; set => this[LineSpacingProperty] = Math.Max(0, value); }
    public static readonly Property LineSpacingProperty = new(nameof(LineSpacing), typeof(double), 0d, AffectsMeasure: true, AffectsArrange: true);

    public override Size OnMeasure(Size availableSize)
    {
        bool horizontal = Orientation == Orientation.Horizontal;
        double maxPrimary = horizontal ? availableSize.Width : availableSize.Height;
        bool wrap = double.IsFinite(maxPrimary) && maxPrimary > 0;

        double linePrimary = 0;
        double lineCross = 0;
        double totalCross = 0;
        double maxLinePrimary = 0;

        foreach (var child in ChildrenIterator)
        {
            var size = child.DesiredSize;
            double childPrimary = horizontal ? size.Width : size.Height;
            double childCross = horizontal ? size.Height : size.Width;

            double nextPrimary = linePrimary == 0 ? childPrimary : linePrimary + ItemSpacing + childPrimary;
            if (wrap && linePrimary > 0 && nextPrimary > maxPrimary)
            {
                totalCross += lineCross;
                if (totalCross > 0)
                    totalCross += LineSpacing;

                maxLinePrimary = Math.Max(maxLinePrimary, linePrimary);
                linePrimary = childPrimary;
                lineCross = childCross;
            }
            else
            {
                linePrimary = nextPrimary;
                lineCross = Math.Max(lineCross, childCross);
            }
        }

        maxLinePrimary = Math.Max(maxLinePrimary, linePrimary);
        totalCross += lineCross;

        return horizontal
            ? new Size(maxLinePrimary, totalCross)
            : new Size(totalCross, maxLinePrimary);
    }

    public override void OnArrange(Size finalSize, Matrix2D transform)
    {
        arrangedSlots.Clear();

        bool horizontal = Orientation == Orientation.Horizontal;
        double maxPrimary = horizontal ? finalSize.Width : finalSize.Height;
        bool wrap = double.IsFinite(maxPrimary) && maxPrimary > 0;

        double linePrimary = 0;
        double lineCross = 0;
        double crossOffset = 0;

        foreach (var child in ChildrenIterator)
        {
            var desired = child.DesiredSize;
            double childPrimary = horizontal ? desired.Width : desired.Height;
            double childCross = horizontal ? desired.Height : desired.Width;

            double nextPrimary = linePrimary == 0 ? childPrimary : linePrimary + ItemSpacing + childPrimary;
            if (wrap && linePrimary > 0 && nextPrimary > maxPrimary)
            {
                crossOffset += lineCross + LineSpacing;
                linePrimary = 0;
                lineCross = 0;
                nextPrimary = childPrimary;
            }

            double primaryOffset = linePrimary == 0 ? 0 : linePrimary + ItemSpacing;
            Rect slot = horizontal
                ? new Rect(primaryOffset, crossOffset, childPrimary, childCross)
                : new Rect(crossOffset, primaryOffset, childCross, childPrimary);

            arrangedSlots[child] = slot;
            linePrimary = nextPrimary;
            lineCross = Math.Max(lineCross, childCross);
        }
    }

    protected override Size GetChildMeasureConstraint(FrameworkObject child, Size availableSize)
        => availableSize;

    protected override Matrix2D GetChildTransform(FrameworkObject child, Size availableSize)
    {
        if (!arrangedSlots.TryGetValue(child, out var slot))
            return Matrix2D.Identity;

        return Matrix2D.Translate(slot.X, slot.Y);
    }

    protected override Size GetChildArrangeSize(FrameworkObject child, Size availableSize)
        => arrangedSlots.TryGetValue(child, out var slot)
            ? slot.Size
            : child.DesiredSize;
}

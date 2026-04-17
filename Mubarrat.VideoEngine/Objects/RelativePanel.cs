namespace Mubarrat.VideoEngine.Objects;

public class RelativePanel : Panel
{
    private readonly Dictionary<FrameworkObject, Rect> arrangedSlots = [];

    public static readonly double Unset = double.NaN;

    public static double GetLeft(FrameworkObject element) => (double)element[LeftProperty];
    public static void SetLeft(FrameworkObject element, double value) => element[LeftProperty] = value;
    public static readonly Property LeftProperty = new("Left", typeof(double), Unset, AffectsParentMeasure: true, AffectsParentArrange: true);

    public static double GetTop(FrameworkObject element) => (double)element[TopProperty];
    public static void SetTop(FrameworkObject element, double value) => element[TopProperty] = value;
    public static readonly Property TopProperty = new("Top", typeof(double), Unset, AffectsParentMeasure: true, AffectsParentArrange: true);

    public static double GetRight(FrameworkObject element) => (double)element[RightProperty];
    public static void SetRight(FrameworkObject element, double value) => element[RightProperty] = value;
    public static readonly Property RightProperty = new("Right", typeof(double), Unset, AffectsParentMeasure: true, AffectsParentArrange: true);

    public static double GetBottom(FrameworkObject element) => (double)element[BottomProperty];
    public static void SetBottom(FrameworkObject element, double value) => element[BottomProperty] = value;
    public static readonly Property BottomProperty = new("Bottom", typeof(double), Unset, AffectsParentMeasure: true, AffectsParentArrange: true);

    public static bool GetCenterHorizontal(FrameworkObject element) => (bool)element[CenterHorizontalProperty];
    public static void SetCenterHorizontal(FrameworkObject element, bool value) => element[CenterHorizontalProperty] = value;
    public static readonly Property CenterHorizontalProperty = new("CenterHorizontal", typeof(bool), false, AffectsParentArrange: true);

    public static bool GetCenterVertical(FrameworkObject element) => (bool)element[CenterVerticalProperty];
    public static void SetCenterVertical(FrameworkObject element, bool value) => element[CenterVerticalProperty] = value;
    public static readonly Property CenterVerticalProperty = new("CenterVertical", typeof(bool), false, AffectsParentArrange: true);

    public override Size OnMeasure(Size availableSize)
    {
        double width = 0;
        double height = 0;

        foreach (var child in ChildrenIterator)
        {
            double left = GetLeft(child);
            double right = GetRight(child);
            double top = GetTop(child);
            double bottom = GetBottom(child);
            var desired = child.DesiredSize;

            double requiredWidth = (double.IsFinite(left) ? left : 0) + desired.Width + (double.IsFinite(right) ? right : 0);
            double requiredHeight = (double.IsFinite(top) ? top : 0) + desired.Height + (double.IsFinite(bottom) ? bottom : 0);

            width = Math.Max(width, requiredWidth);
            height = Math.Max(height, requiredHeight);
        }

        return new Size(width, height);
    }

    protected override Size GetChildMeasureConstraint(FrameworkObject child, Size availableSize)
        => availableSize;

    public override void OnArrange(Size finalSize, Matrix2D transform)
    {
        arrangedSlots.Clear();

        foreach (var child in ChildrenIterator)
        {
            var desired = child.DesiredSize;
            double left = GetLeft(child);
            double right = GetRight(child);
            double top = GetTop(child);
            double bottom = GetBottom(child);

            double width = desired.Width;
            if (double.IsFinite(left) && double.IsFinite(right))
                width = Math.Max(0, finalSize.Width - left - right);

            double height = desired.Height;
            if (double.IsFinite(top) && double.IsFinite(bottom))
                height = Math.Max(0, finalSize.Height - top - bottom);

            double x = double.IsFinite(left)
                ? left
                : double.IsFinite(right)
                    ? finalSize.Width - right - width
                    : 0;

            double y = double.IsFinite(top)
                ? top
                : double.IsFinite(bottom)
                    ? finalSize.Height - bottom - height
                    : 0;

            if (GetCenterHorizontal(child) && !double.IsFinite(left) && !double.IsFinite(right))
                x = (finalSize.Width - width) * 0.5;

            if (GetCenterVertical(child) && !double.IsFinite(top) && !double.IsFinite(bottom))
                y = (finalSize.Height - height) * 0.5;

            arrangedSlots[child] = new Rect(x, y, Math.Max(0, width), Math.Max(0, height));
        }
    }

    protected override Matrix2D GetChildTransform(FrameworkObject child, Size availableSize, Matrix2D parentTransform)
    {
        if (!arrangedSlots.TryGetValue(child, out var slot))
            return parentTransform;

        return parentTransform * Matrix2D.Translate(slot.X, slot.Y);
    }

    protected override Size GetChildArrangeSize(FrameworkObject child, Size availableSize)
        => arrangedSlots.TryGetValue(child, out var slot)
            ? slot.Size
            : child.DesiredSize;
}

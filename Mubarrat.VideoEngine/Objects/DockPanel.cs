namespace Mubarrat.VideoEngine.Objects;

public class DockPanel : Panel
{
    private readonly Dictionary<FrameworkObject, Rect> arrangedSlots = [];

    public bool LastChildFill { get => (bool)this[LastChildFillProperty]; set => this[LastChildFillProperty] = value; }
    public static readonly Property LastChildFillProperty = new(nameof(LastChildFill), typeof(bool), true, AffectsMeasure: true, AffectsArrange: true);

    public static Dock GetDock(FrameworkObject element) => (Dock)element[DockProperty];
    public static void SetDock(FrameworkObject element, Dock dock) => element[DockProperty] = dock;
    public static readonly Property DockProperty = new(nameof(Dock), typeof(Dock), Dock.Left, AffectsParentMeasure: true, AffectsParentArrange: true);

    public override Size OnMeasure(Size availableSize)
    {
        double width = 0;
        double height = 0;
        double consumedWidth = 0;
        double consumedHeight = 0;

        var children = ChildrenIterator.ToArray();
        for (int i = 0; i < children.Length; i++)
        {
            var child = children[i];
            bool isFill = LastChildFill && i == children.Length - 1;
            if (isFill)
            {
                width = Math.Max(width, consumedWidth + child.DesiredSize.Width);
                height = Math.Max(height, consumedHeight + child.DesiredSize.Height);
                continue;
            }

            switch (GetDock(child))
            {
                case Dock.Left:
                case Dock.Right:
                    consumedWidth += child.DesiredSize.Width;
                    width = Math.Max(width, consumedWidth);
                    height = Math.Max(height, consumedHeight + child.DesiredSize.Height);
                    break;

                case Dock.Top:
                case Dock.Bottom:
                    consumedHeight += child.DesiredSize.Height;
                    width = Math.Max(width, consumedWidth + child.DesiredSize.Width);
                    height = Math.Max(height, consumedHeight);
                    break;
            }
        }

        return new Size(width, height);
    }

    public override void OnArrange(Size finalSize, Matrix2D transform)
    {
        arrangedSlots.Clear();
        var children = ChildrenIterator.ToArray();
        Rect remaining = new(0, 0, finalSize.Width, finalSize.Height);

        for (int i = 0; i < children.Length; i++)
        {
            var child = children[i];
            bool isFill = LastChildFill && i == children.Length - 1;

            if (isFill)
            {
                arrangedSlots[child] = remaining;
                continue;
            }

            var desired = child.DesiredSize;
            switch (GetDock(child))
            {
                case Dock.Left:
                {
                    double dockWidth = Math.Min(desired.Width, remaining.Width);
                    arrangedSlots[child] = new Rect(remaining.X, remaining.Y, dockWidth, remaining.Height);
                    remaining = new Rect(remaining.X + dockWidth, remaining.Y, Math.Max(0, remaining.Width - dockWidth), remaining.Height);
                    break;
                }
                case Dock.Right:
                {
                    double dockWidth = Math.Min(desired.Width, remaining.Width);
                    arrangedSlots[child] = new Rect(remaining.Right - dockWidth, remaining.Y, dockWidth, remaining.Height);
                    remaining = new Rect(remaining.X, remaining.Y, Math.Max(0, remaining.Width - dockWidth), remaining.Height);
                    break;
                }
                case Dock.Top:
                {
                    double dockHeight = Math.Min(desired.Height, remaining.Height);
                    arrangedSlots[child] = new Rect(remaining.X, remaining.Y, remaining.Width, dockHeight);
                    remaining = new Rect(remaining.X, remaining.Y + dockHeight, remaining.Width, Math.Max(0, remaining.Height - dockHeight));
                    break;
                }
                case Dock.Bottom:
                {
                    double dockHeight = Math.Min(desired.Height, remaining.Height);
                    arrangedSlots[child] = new Rect(remaining.X, remaining.Bottom - dockHeight, remaining.Width, dockHeight);
                    remaining = new Rect(remaining.X, remaining.Y, remaining.Width, Math.Max(0, remaining.Height - dockHeight));
                    break;
                }
            }
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

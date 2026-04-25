using Mubarrat.VideoEngine.Draw;
using Mubarrat.VideoEngine.Path;

namespace Mubarrat.VideoEngine.Objects;

public class Border : FrameworkObject
{
    public FrameworkObject? Child
    {
        get => (FrameworkObject?)this[ChildProperty];
        set => this[ChildProperty] = value!;
    }

    public static readonly Property ChildProperty = new(nameof(Child), typeof(FrameworkObject), AffectsMeasure: true, AffectsArrange: true);

    public IBrush? Background
    {
        get => (IBrush?)this[BackgroundProperty];
        set => this[BackgroundProperty] = value;
    }

    public static readonly Property BackgroundProperty = new(nameof(Background), typeof(IBrush), AffectsArrange: true);

    public Pen BorderPen
    {
        get => (Pen)this[BorderProperty];
        set => this[BorderProperty] = value;
    }

    public static readonly Property BorderProperty = new(nameof(Border), typeof(Pen), new Pen(), AffectsMeasure: true, AffectsArrange: true);

    public double CornerRadius
    {
        get => (double)this[CornerRadiusProperty];
        set => this[CornerRadiusProperty] = Math.Max(0, value);
    }

    public static readonly Property CornerRadiusProperty = new(nameof(CornerRadius), typeof(double), 0d, AffectsMeasure: true, AffectsArrange: true);

    public Thickness Padding
    {
        get => (Thickness)this[PaddingProperty];
        set => this[PaddingProperty] = value;
    }

    public static readonly Property PaddingProperty = new(nameof(Padding), typeof(Thickness), new Thickness(0), AffectsMeasure: true, AffectsArrange: true);

    protected override IEnumerable<FrameworkObject> ChildrenIterator => Child is null ? [] : [Child];

    protected override void OnPropertyChanged(Property property, object? oldValue, object? newValue)
    {
        if (property == ChildProperty)
        {
            if (oldValue is FrameworkObject oldChild && ReferenceEquals(oldChild.Parent, this))
                oldChild.Parent = null;

            if (newValue is FrameworkObject newChild)
                newChild.Parent = this;
        }

        base.OnPropertyChanged(property, oldValue, newValue);
    }

    public override Size OnMeasure(Size availableSize)
    {
        Size insetSize = new(HorizontalContentInset * 2, VerticalContentInset * 2);
        return Child is not FrameworkObject child ? insetSize : child.DesiredSize + insetSize;
    }

    protected override Size GetChildMeasureConstraint(FrameworkObject child, Size availableSize) => Size.Max(Size.Zero, availableSize - new Size(HorizontalContentInset * 2, VerticalContentInset * 2));

    protected override Matrix2D GetChildTransform(FrameworkObject child, Size availableSize) => Matrix2D.Translate(HorizontalContentInset, VerticalContentInset);

    protected override Size GetChildArrangeSize(FrameworkObject child, Size availableSize) => Size.Max(Size.Zero, availableSize - new Size(HorizontalContentInset * 2, VerticalContentInset * 2));

    public override Drawing ToDrawing()
    {
        var drawings = new List<Drawing>(2);
        double width = ActualBounds.Width;
        double height = ActualBounds.Height;
        var border = BorderPen;
        double thickness = Math.Max(0, border.Thickness);
        double radius = CornerRadius;

        bool hasFill = Background is not null;
        bool hasStroke = border.Brush is not null && thickness > 0;

        if ((hasFill || hasStroke) && width > 0 && height > 0)
        {
            double inset = hasStroke ? thickness * 0.5 : 0;
            Rect rect = new(
                inset,
                inset,
                Math.Max(0, width - inset * 2),
                Math.Max(0, height - inset * 2));

            if (rect.Width > 0 && rect.Height > 0)
            {
                drawings.Add(new PathDrawing
                {
                    Path = BuildRectPath(rect, Math.Max(0, radius - inset)).Build(),
                    Fill = hasFill ? Background! : null!,
                    Stroke = hasStroke ? border : default,
                    Name = Name
                });
            }
        }

        if (Child is not null)
            drawings.Add(Child.ToDrawing());

        return new GroupDrawing
        {
            Drawings = drawings,
            Transform = LayoutTransform * RenderTransform,
            Opacity = Opacity,
            Name = Name
        };
    }

    private static PathBuilder BuildRectPath(Rect rect, double radius)
        => radius > 0 ? PathBuilder.RoundedRectangle(rect, radius) : PathBuilder.Rectangle(rect);

    private double HorizontalContentInset => Math.Max(0, BorderPen.Thickness) + Padding.Horizontal;
    private double VerticalContentInset => Math.Max(0, BorderPen.Thickness) + Padding.Vertical;
}

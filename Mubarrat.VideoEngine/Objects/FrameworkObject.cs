using Mubarrat.VideoEngine.Draw;
using Mubarrat.VideoEngine.Path;

namespace Mubarrat.VideoEngine.Objects;

public abstract class FrameworkObject : BaseObject, ILerpable<FrameworkObject>
{
    public Matrix2D LayoutTransform { get; protected set; } = Matrix2D.Identity;
    public Matrix2D RenderTransform { get; set; } = Matrix2D.Identity;

    public FrameworkObject? Parent { get => (FrameworkObject?)this[ParentProperty]; set => this[ParentProperty] = value!; }
    public static readonly Property ParentProperty = new(nameof(Parent), typeof(FrameworkObject), AffectsParentMeasure: true);

    public double Width { get => (double)this[WidthProperty]; set => this[WidthProperty] = value; }
    public static readonly Property WidthProperty = new(nameof(Width), typeof(double), double.NaN, AffectsMeasure: true);

    public double Height { get => (double)this[HeightProperty]; set => this[HeightProperty] = value; }
    public static readonly Property HeightProperty = new(nameof(Height), typeof(double), double.NaN, AffectsMeasure: true);

    public double Opacity { get => (double)this[OpacityProperty]; set => this[OpacityProperty] = value; }
    public static readonly Property OpacityProperty = new(nameof(Opacity), typeof(double), 1.0);

    public string Name { get => (string)this[NameProperty]; set => this[NameProperty] = value; }
    public static readonly Property NameProperty = new(nameof(Name), typeof(string));

    public Size DesiredSize { get; private set; } = Size.NaN;
    public Rect ActualBounds { get; private set; } = Rect.NaN;
    public Size ActualSize => ActualBounds.Size;

    public bool IsMeasureValid { get; private set; }
    public bool IsArrangeValid { get; private set; }

    protected virtual IEnumerable<FrameworkObject> ChildrenIterator => [];

    protected override object GetDefaultValue(Property property) => property == ParentProperty ? null! : (Parent?[property] ?? base.GetDefaultValue(property));

    protected override void OnPropertyChanged(Property property, object? oldValue, object? newValue)
    {
        base.OnPropertyChanged(property, oldValue, newValue);

        if (property.IsInherited)
        {
            foreach (var child in ChildrenIterator)
                child.OnPropertyChanged(property, oldValue, newValue);
        }

        if (property.AffectsMeasure)
            InvalidateMeasure();

        if (property.AffectsArrange)
            InvalidateArrange();

        if (property.AffectsParentMeasure)
            Parent?.InvalidateMeasure();

        if (property.AffectsParentArrange)
            Parent?.InvalidateArrange();
    }

    public void InvalidateMeasure()
    {
        if (!IsMeasureValid)
            return;

        IsMeasureValid = false;
        IsArrangeValid = false;

        foreach (var child in ChildrenIterator)
            child.InvalidateArrange();

        Parent?.InvalidateMeasure();
    }

    public void InvalidateArrange()
    {
        if (!IsArrangeValid)
            return;

        IsArrangeValid = false;

        foreach (var child in ChildrenIterator)
            child.InvalidateArrange();
    }

    internal void MeasureSubtree(Size availableSize)
    {
        if (IsMeasureValid)
            return;

        foreach (var child in ChildrenIterator)
        {
            child.MeasureSubtree(GetChildMeasureConstraint(child, availableSize));
        }

        Measure(availableSize);
        IsMeasureValid = true;
    }

    internal void ArrangeSubtree(Size availableSize, Matrix2D parentTransform)
    {
        if (IsArrangeValid)
            return;

        Arrange(availableSize, parentTransform);
        IsArrangeValid = true;

        foreach (var child in ChildrenIterator)
        {
            var childTransform = GetChildTransform(child, availableSize, parentTransform);
            var childSize = GetChildArrangeSize(child, availableSize);

            child.ArrangeSubtree(childSize, childTransform);
        }
    }

    public void Measure(Size availableSize)
    {
        if (double.IsNaN(availableSize.Width) || availableSize.Width < 0 ||
            double.IsNaN(availableSize.Height) || availableSize.Height < 0)
            throw new ArgumentException("Available size must be valid.", nameof(availableSize));

        Size measuredSize = OnMeasure(availableSize);

        if (double.IsNaN(measuredSize.Width) || measuredSize.Width < 0 ||
            double.IsNaN(measuredSize.Height) || measuredSize.Height < 0)
            throw new InvalidOperationException("OnMeasure must return a valid Size.");

        double width = double.IsNaN(Width) ? measuredSize.Width : Width;
        double height = double.IsNaN(Height) ? measuredSize.Height : Height;

        DesiredSize = Size.Min(new Size(width, height), availableSize);
    }

    public void Arrange(Size availableSize, Matrix2D parentTransform)
    {
        if (double.IsNaN(availableSize.Width) || availableSize.Width < 0 ||
            double.IsNaN(availableSize.Height) || availableSize.Height < 0)
            throw new ArgumentException("Available size must be valid.", nameof(availableSize));

        ActualBounds = new Rect(0, 0, availableSize.Width, availableSize.Height);

        LayoutTransform = parentTransform;

        OnArrange(availableSize, parentTransform);
    }

    public virtual Size OnMeasure(Size availableSize) => Size.Zero;

    public virtual void OnArrange(Size finalSize, Matrix2D transform) { }

    protected virtual Size GetChildMeasureConstraint(FrameworkObject child, Size availableSize) => availableSize;

    protected virtual Matrix2D GetChildTransform(FrameworkObject child, Size availableSize, Matrix2D parentTransform) => parentTransform * Matrix2D.Translate(0, 0);

    protected virtual Size GetChildArrangeSize(FrameworkObject child, Size availableSize) => child.DesiredSize;

    public void UpdateLayout(Size availableSize, Matrix2D transform)
    {
        MeasureSubtree(availableSize);
        ArrangeSubtree(availableSize, transform);
    }

    public virtual FrameworkObject Lerp(in FrameworkObject target, double t) => new LerpFrameworkObject(this, target, t);
    // Default implementation returns a LerpFrameworkObject that interpolates between the two objects. Override for more efficient implementations.

    public abstract Drawing ToDrawing();
}

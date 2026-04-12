namespace Mubarrat.VideoEngine.Objects;

public class FrameworkObject : BaseObject
{
    public Matrix2D Transform { get => (Matrix2D)this[TransformProperty]; set => this[TransformProperty] = value; }
    public static readonly Property TransformProperty = new(nameof(Transform), typeof(Matrix2D), defaultValue: Matrix2D.Identity);

    public FrameworkObject Parent { get => (FrameworkObject)this[ParentProperty]; set => this[ParentProperty] = value; }
    public static readonly Property ParentProperty = new(nameof(Parent), typeof(FrameworkObject), AffectsLayout: true);

    public double Width { get => (double)this[WidthProperty]; set => this[WidthProperty] = value; }
    public static readonly Property WidthProperty = new(nameof(Width), typeof(double));

    public double Height { get => (double)this[HeightProperty]; set => this[HeightProperty] = value; }
    public static readonly Property HeightProperty = new(nameof(Height), typeof(double));

    public double Opacity { get => (double)this[OpacityProperty]; set => this[OpacityProperty] = value; }
    public static readonly Property OpacityProperty = new(nameof(Opacity), typeof(double));

    public string Name { get => (string)this[NameProperty]; set => this[NameProperty] = value; }
    public static readonly Property NameProperty = new(nameof(Name), typeof(string), AffectsLayout: true);

    public virtual IEnumerable<BaseObject> Children => [];

    protected override object GetDefaultValue(Property property) => Parent?[property] ?? base.GetDefaultValue(property);

    protected override void OnPropertyChanged(Property property, object? oldValue, object? newValue)
    {
        base.OnPropertyChanged(property, oldValue, newValue);
        if (property.AffectsLayout)
            OnLayout();
    }

    protected virtual void OnLayout()
    {
    }
}

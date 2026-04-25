using Mubarrat.VideoEngine.Objects;

namespace Mubarrat.VideoEngine.Draw;

public abstract class Drawing : BaseObject, ILerpable<Drawing>
{
#if DEBUG
    public string? DebugName { get; set; }
#else
    public string? DebugName { get => null; set { } }
#endif

    public string Name { get => (string)this[NameProperty]; set => this[NameProperty] = value; }
    public static readonly Property NameProperty = new(nameof(Name), typeof(string), DefaultValue: string.Empty);

    public Matrix2D Transform { get => (Matrix2D)this[TransformProperty]; set => this[TransformProperty] = value; }
    public static readonly Property TransformProperty = new(nameof(Transform), typeof(Matrix2D), DefaultValue: Matrix2D.Identity);

    public double Opacity { get => (double)this[OpacityProperty]; set => this[OpacityProperty] = value; }
    public static readonly Property OpacityProperty = new(nameof(Opacity), typeof(double), DefaultValue: 1d);

    public IBrush Fill { get => (IBrush)this[FillProperty]; set => this[FillProperty] = value; }
    public static readonly Property FillProperty = new(nameof(Fill), typeof(IBrush), DefaultValue: null);

    public Pen Stroke { get => (Pen)this[StrokeProperty]; set => this[StrokeProperty] = value; }
    public static readonly Property StrokeProperty = new(nameof(Stroke), typeof(Pen), DefaultValue: new Pen());

    public abstract Rect Bounds { get; }

    public abstract Drawing Lerp(in Drawing other, double t);
}

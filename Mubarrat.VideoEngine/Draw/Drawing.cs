using Mubarrat.VideoEngine.Objects;

namespace Mubarrat.VideoEngine.Draw;

public abstract class Drawing : BaseObject, ILerpable<Drawing>
{
    public Matrix2D Transform { get => (Matrix2D)this[TransformProperty]; set => this[TransformProperty] = value; }
    public static readonly Property TransformProperty = new(nameof(Transform), typeof(Matrix2D), defaultValue: Matrix2D.Identity);

    public double Opacity { get => (double)this[OpacityProperty]; set => this[OpacityProperty] = value; }
    public static readonly Property OpacityProperty = new(nameof(Opacity), typeof(double), defaultValue: 1d);

    public abstract Rect Bounds { get; }

    public abstract Drawing Lerp(in Drawing other, double t);
}

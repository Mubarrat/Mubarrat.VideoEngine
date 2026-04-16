using Mubarrat.VideoEngine.Immutable;
using Mubarrat.VideoEngine.Objects;

namespace Mubarrat.VideoEngine.Draw;

public sealed class PathDrawing : Drawing
{
    public Path2D Path { get => (Path2D)this[PathProperty]; set => this[PathProperty] = value; }
    public static readonly Property PathProperty = new(nameof(Path), typeof(Path2D), DefaultValue: null);

    public IBrush Fill { get => (IBrush)this[FillProperty]; set => this[FillProperty] = value; }
    public static readonly Property FillProperty = new(nameof(Fill), typeof(IBrush), DefaultValue: null);

    public Pen Stroke { get => (Pen)this[StrokeProperty]; set => this[StrokeProperty] = value; }
    public static readonly Property StrokeProperty = new(nameof(Stroke), typeof(Pen), DefaultValue: new Pen());

    public override Rect Bounds => Path.Bounds.Inflate(Stroke.Thickness / 2, Stroke.Thickness / 2) * Transform;

    public override Drawing Lerp(in Drawing other, double t)
    {
        switch (t)
        {
            case 0: return this;
            case 1: return other;
        }

        if (other is PathDrawing pathDrawing)
        {
            return new PathDrawing
            {
                Path = Path.Lerp(pathDrawing.Path, t),
                Fill = Fill.Lerp(pathDrawing.Fill, t),
                Stroke = Stroke.Lerp(pathDrawing.Stroke, t),
                Transform = Transform.Lerp(pathDrawing.Transform, t),
                Opacity = Opacity.Lerp(pathDrawing.Opacity, t)
            };
        }

        throw new NotImplementedException();
    }
}

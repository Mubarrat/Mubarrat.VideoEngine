using Mubarrat.VideoEngine.Immutable;
using Mubarrat.VideoEngine.Objects;

namespace Mubarrat.VideoEngine.Draw;

public sealed class PathDrawing : Drawing
{
    public Path2D Path { get => (Path2D)this[PathProperty]; set => this[PathProperty] = value; }
    public static readonly Property PathProperty = new(nameof(Path), typeof(Path2D), DefaultValue: null);

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
                Fill = Fill?.Lerp(pathDrawing.Fill, t) ?? pathDrawing.Fill?.Lerp(IBrush.Transparent, 1 - t) ?? null,
                Stroke = Stroke.Lerp(pathDrawing.Stroke, t),
                Transform = Transform.Lerp(pathDrawing.Transform, t),
                Opacity = Opacity.Lerp(pathDrawing.Opacity, t),
                Name = SelectName(Name, pathDrawing.Name, t)
            };
        }

        if (other is GroupDrawing)
            return DrawingMorpher.Lerp(this, other, t);

        throw new NotImplementedException();
    }

    private static string SelectName(string from, string to, double t)
        => !string.IsNullOrWhiteSpace(from) && string.Equals(from, to, StringComparison.Ordinal)
            ? from
            : (t < 0.5 ? from : to);
}

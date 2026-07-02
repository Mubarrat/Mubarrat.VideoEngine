using Mubarrat.VideoEngine.Field;
using Mubarrat.VideoEngine.Objects;

namespace Mubarrat.VideoEngine.Draw;

public sealed class FieldDrawing : Drawing
{
    public Field2D Field { get => (Field2D)this[FieldProperty]; set => this[FieldProperty] = value; }
    public static readonly Property FieldProperty = new(nameof(Field), typeof(Field2D), DefaultValue: ConstantField2D.Empty);

    public override Rect Bounds => Field.Bounds.Inflate(Stroke.Thickness / 2, Stroke.Thickness / 2) * Transform;

    public override Drawing Lerp(in Drawing other, double t)
    {
        switch (t)
        {
            case 0: return this;
            case 1: return other;
        }

        if (other is FieldDrawing fieldDrawing)
        {
            return new FieldDrawing
            {
                Field = Field.Lerp(fieldDrawing.Field, t),
                Fill = Fill?.Lerp(fieldDrawing.Fill, t) ?? fieldDrawing.Fill?.Lerp(IBrush.Transparent, 1 - t) ?? null,
                Stroke = Stroke.Lerp(fieldDrawing.Stroke, t),
                Transform = Transform.Lerp(fieldDrawing.Transform, t),
                Opacity = Opacity.Lerp(fieldDrawing.Opacity, t),
                Name = SelectName(Name, fieldDrawing.Name, t)
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

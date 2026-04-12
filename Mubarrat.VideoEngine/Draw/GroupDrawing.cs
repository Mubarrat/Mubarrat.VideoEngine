using Mubarrat.VideoEngine.Objects;

namespace Mubarrat.VideoEngine.Draw;

public sealed class GroupDrawing : Drawing
{
    public List<Drawing> Drawings { get => (List<Drawing>)this[DrawingsProperty]; set => this[DrawingsProperty] = value; }
    public static readonly Property DrawingsProperty = new(nameof(Drawings), typeof(List<Drawing>), defaultValue: null);

    public override Rect Bounds => Drawings.Aggregate(Rect.Empty, (a, b) => a.Union(b.Bounds)) * Transform;

    public override Drawing Lerp(in Drawing other, double t)
    {
        throw new NotImplementedException();
    }

    public override object Clone()
    {
        var copy = (GroupDrawing)base.Clone();
        copy.Drawings = Drawings.ConvertAll(d => (Drawing)d.Clone());
        return copy;
    }
}

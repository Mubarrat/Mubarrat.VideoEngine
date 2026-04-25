using Mubarrat.OpenType.TextShaping;
using Mubarrat.VideoEngine.Draw;
using Mubarrat.VideoEngine.Path;

namespace Mubarrat.VideoEngine.Latex;

public sealed class SymbolMathAtom(string text = "") : MathAtom
{
    public string Text = text ?? string.Empty;
    public IReadOnlyCollection<string>? ExtraFeatures = null;

    public override void OnProperty()
    {
        if (!Metrics.HasValue)
            throw new ArgumentNullException(nameof(Metrics));
    }

    public override void OnLayout()
    {
        var metrics = Metrics ?? throw new InvalidOperationException("Metrics must be set for SymbolMathAtom");
        var result = OpenTypeTextShaper.Shape(Text, metrics, new OpenTypeShapingOptions(ExtraFeatures));
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        foreach (var g in result.Glyphs)
        {
            minY = Math.Min(minY, g.YOffset);
            maxY = Math.Max(maxY, g.YOffset + g.YAdvance);
        }
        Size = new(result.Width, maxY - minY);
        if (Height == 0)
            Height = metrics.Ascent + metrics.Descent;
        Baseline = metrics.Ascent - minY;
    }

    public override Drawing OnDraw() => new PathDrawing
    {
        Path = OpenTypeTextShaper.Shape(Text, Metrics ?? throw new InvalidOperationException("Metrics must be set for SymbolMathAtom"))
                                 .ToPath2D(Metrics.Value.FontSize, Location + (0, Baseline), false)
    };
}

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
        var result = OpenTypeTextShaper.Shape(Text, metrics, new OpenTypeShapingOptions(GetFinalFeatures()));
        double minY = 0, maxY = 0;
        foreach (var g in result.Glyphs)
        {
            minY = Math.Min(minY, g.YOffset);
            maxY = Math.Max(maxY, g.YOffset);
        }
        var bounds = result.ToPath2D(metrics.FontSize, (0, metrics.Ascent)).Bounds;
        Baseline = metrics.Ascent - bounds.Top;
        Width = result.Width;
        Height = bounds.Height + maxY - minY;
    }

    public override Drawing OnDraw() => new PathDrawing
    {
        Path = OpenTypeTextShaper.Shape(Text, Metrics ?? throw new InvalidOperationException("Metrics must be set for SymbolMathAtom"), new OpenTypeShapingOptions(GetFinalFeatures()))
                                 .ToPath2D(Metrics.Value.FontSize, Location + (0, Baseline)),
        Name = Name
    };

    private IReadOnlyCollection<string> GetFinalFeatures()
    {
        return (ExtraFeatures ?? []).Concat(Style is MathStyle.Script ? ["ssty1"] : Style is MathStyle.ScriptScript ? ["ssty2"] : []).ToArray();
    }
}

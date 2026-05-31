using Mubarrat.OpenType.Tables;
using Mubarrat.VideoEngine.Draw;
using Mubarrat.VideoEngine.Path;

namespace Mubarrat.VideoEngine.Latex;

public sealed class RadicalMathAtom(MathAtom radicand, MathAtom? degree) : MathAtom(MathAtomType.Inner)
{
    public MathAtom Radicand = radicand ?? new SymbolMathAtom();
    public MathAtom? Degree = degree;

    protected override IEnumerable<MathAtom> ChildrenIterator
    {
        get
        {
            yield return Radicand;
            if (Degree is { } d) yield return d;
        }
    }

    public override void OnProperty()
    {
        var metrics = Metrics ?? throw new InvalidOperationException("Metrics must be set for BinomialMathAtom");
        Radicand.Metrics = metrics;
        Radicand.Style = Style;
        Radicand.IsCramped = true;
        if (Degree is not null)
        {
            Degree.Metrics = ScaleMetrics(metrics, Degree.Style = MathStyle.ScriptScript);
            Degree.IsCramped = true;
        }
    }

    double RadicalHeight;

    public override void OnLayout()
    {
        var metrics = Metrics ?? throw new InvalidOperationException("Metrics must be set for BinomialMathAtom");
        var c = MathTable.MathConstants;

        var extraAscender = c.RadicalExtraAscender * metrics.Scale;
        var rule = c.RadicalRuleThickness * metrics.Scale;
        var gap = (Style == MathStyle.Display ? c.RadicalDisplayStyleVerticalGap : c.RadicalVerticalGap) * metrics.Scale;
        var kernBefore = c.RadicalKernBeforeDegree * metrics.Scale;
        var kernAfter = c.RadicalKernAfterDegree * metrics.Scale;
        var degreeRaise = c.RadicalDegreeBottomRaisePercent / 100d;

        Helpers.GetVerticalGlyphInfo('√', metrics, Radicand.Height + gap + rule, out double radicalWidth, out RadicalHeight);

        if (Degree is { })
            radicalWidth += Math.Max(kernBefore + Degree.Width + kernAfter, 0);

        Height = RadicalHeight + extraAscender;
        if (Degree is { })
            Height = Math.Max(RadicalHeight * degreeRaise + Degree.Height, Height);

        Radicand.Location = new(radicalWidth, Height - 0.5 * (RadicalHeight - rule - gap + Radicand.Height));

        Baseline = Radicand.Y + Radicand.Baseline;
        Width = radicalWidth + Radicand.Width;

        Degree?.Location = new(kernBefore, Height - RadicalHeight * degreeRaise - Degree.Height);
    }

    public override Drawing OnDraw()
    {
        var metrics = Metrics ?? throw new InvalidOperationException("Metrics must be set for RadicalMathAtom");
        var c = MathTable.MathConstants;
        var rule = c.RadicalRuleThickness * metrics.Scale;
        var drawing = (GroupDrawing)base.OnDraw();
        drawing.Drawings.Add(new PathDrawing
        {
            Path = PathBuilder.Rectangle(
                new(new(Radicand.X - MathTable.MathVariants.MinConnectorOverlap * metrics.Scale, Height - RadicalHeight),
                new Point(Radicand.Bounds.Right, Height - RadicalHeight + rule))).Build()
        });
        var path2D = Helpers.GetVerticalGlyph('√', metrics, RadicalHeight);
        Rect bounds = path2D.Bounds;
        drawing.Drawings.Add(new PathDrawing
        {
            Path = path2D,
            Transform = Matrix2D.Translate(Width - Radicand.Width - bounds.Right, Height - RadicalHeight - bounds.Top)
        });
        return drawing;
    }
}

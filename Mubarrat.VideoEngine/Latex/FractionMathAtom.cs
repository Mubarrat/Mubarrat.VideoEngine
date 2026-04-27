using Mubarrat.OpenType.Tables;
using Mubarrat.VideoEngine.Draw;
using Mubarrat.VideoEngine.Path;

namespace Mubarrat.VideoEngine.Latex;

public sealed class FractionMathAtom(MathAtom numerator, MathAtom denominator) : MathAtom(MathAtomType.Inner)
{
    public MathAtom Numerator = numerator ?? new SymbolMathAtom();
    public MathAtom Denominator = denominator ?? new SymbolMathAtom();

    protected override IEnumerable<MathAtom> ChildrenIterator
    {
        get
        {
            yield return Numerator;
            yield return Denominator;
        }
    }

    public override void OnProperty()
    {
        var metrics = Metrics ?? throw new InvalidOperationException("Metrics must be set for FractionMathAtom");
        Numerator.Metrics = Denominator.Metrics = ScaleMetrics(metrics,
            Numerator.Style = Denominator.Style = Style is MathStyle.Display ? MathStyle.Display : DownScriptStyle(Style));
        Numerator.IsCramped = IsCramped;
        Denominator.IsCramped = true;
    }

    public override void OnLayout()
    {
        if (Metrics is not { } m)
            throw new InvalidOperationException("Metrics must be set for FractionMathAtom");

        var c = MathTable.MathConstants;
        var rule = c.FractionRuleThickness * m.Scale;

        double numeratorWidth = Numerator is FractionMathAtom ? 2 * MathSpacingEngine.GetAbsoluteSpacing(MathSpacing.Thin, m) + Numerator.Width : Numerator.Width;
        double denominatorWidth = Denominator is FractionMathAtom ? 2 * MathSpacingEngine.GetAbsoluteSpacing(MathSpacing.Thin, m) + Denominator.Width : Denominator.Width;

        Width = Math.Max(numeratorWidth, denominatorWidth);
        Numerator.X = (Width - Numerator.Width) / 2;
        Denominator.X = (Width - Denominator.Width) / 2;

        Numerator.Y = 0;
        Baseline = Math.Max(
            Numerator.Baseline + (Style is MathStyle.Display ? c.FractionNumeratorDisplayStyleShiftUp : c.FractionNumeratorShiftUp) * m.Scale,
            Numerator.Height + (Style is MathStyle.Display ? c.FractionNumeratorDisplayStyleGapMin : c.FractionNumeratorGapMin) * m.Scale + rule / 2 + c.AxisHeight * m.Scale);
        Denominator.Y = Math.Max(
            Baseline - Denominator.Baseline + (Style is MathStyle.Display ? c.FractionDenominatorDisplayStyleShiftDown : c.FractionDenominatorShiftDown) * m.Scale,
            Baseline - c.AxisHeight * m.Scale + rule / 2 + (Style is MathStyle.Display ? c.FractionDenominatorDisplayStyleGapMin : c.FractionDenominatorGapMin) * m.Scale);
        Height = Denominator.Bounds.Bottom;
    }

    public override Drawing OnDraw()
    {
        if (Metrics is not { } m)
            throw new InvalidOperationException("Metrics must be set for FractionMathAtom");
        var c = MathTable.MathConstants;
        var rule = c.FractionRuleThickness * m.Scale;

        var drawing = (GroupDrawing)base.OnDraw();
        drawing.Drawings.Add(new PathDrawing { Path = PathBuilder.Rectangle(new(0, Baseline - c.AxisHeight * m.Scale - rule / 2, Width, rule)).Build() });
        return drawing;
    }
}

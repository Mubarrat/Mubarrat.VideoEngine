using Mubarrat.OpenType.Tables;

namespace Mubarrat.VideoEngine.Latex;

public class StackMathAtom(MathAtom numerator, MathAtom denominator) : MathAtom(MathAtomType.Inner)
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
        var metrics = Metrics ?? throw new InvalidOperationException("Metrics must be set for StackMathAtom");
        Numerator.Metrics = Denominator.Metrics = ScaleMetrics(metrics,
            Numerator.Style = Denominator.Style = Style is MathStyle.Display ? MathStyle.Display : DownScriptStyle(Style));
        Numerator.IsCramped = IsCramped;
        Denominator.IsCramped = true;
    }

    public override void OnLayout()
    {
        if (Metrics is not { } m)
            throw new InvalidOperationException("Metrics must be set for StackMathAtom");

        var c = MathTable.MathConstants;

        double numeratorWidth = Numerator.Width;
        double denominatorWidth = Denominator.Width;

        Width = Math.Max(numeratorWidth, denominatorWidth);
        Numerator.X = (Width - Numerator.Width) / 2;
        Denominator.X = (Width - Denominator.Width) / 2;

        Numerator.Y = 0;
        Baseline = Math.Max(
            Numerator.Baseline + (Style is MathStyle.Display ? c.StackTopDisplayStyleShiftUp : c.StackTopShiftUp) * m.Scale,
            Numerator.Height + (Style is MathStyle.Display ? c.StackDisplayStyleGapMin : c.StackGapMin) * m.Scale);
        Denominator.Y = Baseline - Denominator.Baseline + (Style is MathStyle.Display ? c.StackBottomDisplayStyleShiftDown : c.StackBottomShiftDown) * m.Scale;
        Height = Denominator.Bounds.Bottom;
    }
}

using Mubarrat.OpenType.Tables;
using Mubarrat.VideoEngine.Draw;
using Mubarrat.VideoEngine.Path;

namespace Mubarrat.VideoEngine.Latex;

public sealed class FractionMathAtom(MathAtom numerator, MathAtom denominator) : BinomialMathAtom(numerator, denominator)
{
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

namespace Mubarrat.VideoEngine.Latex;

public sealed class OverUnderMathAtom(MathAtom @base, MathAtom? Under, MathAtom? Over) : ScriptsMathAtom(@base, Under, Over)
{
    public override void OnLayout()
    {
        if (Style is not MathStyle.Display)
        {
            base.OnLayout();
            return;
        }
        if (Metrics is not { } m)
            throw new InvalidOperationException("Metrics must be set for ScriptsMathAtom");
        var c = MathTable.MathConstants;
        Width = Math.Max(Math.Max(Superscript?.Width ?? 0, Subscript?.Width ?? 0), Base.Width);
        Base.X = (Width - Base.Width) * 0.5;
        if (Superscript is { } s)
        {
            s.Location = new((Width - s.Width) * 0.5, 0);
            Base.Y = Math.Max(s.Baseline + c.UpperLimitBaselineRiseMin * m.Scale, s.Height + c.UpperLimitGapMin * m.Scale);
        }
        else
            Base.Y = 0;
        Baseline = Base.Y + Base.Baseline;
        if (Subscript is { } sub)
        {
            sub.Location = new((Width - sub.Width) * 0.5, Base.Y + Math.Max(
                Base.Baseline - sub.Baseline + c.LowerLimitBaselineDropMin * m.Scale,
                Base.Height + c.LowerLimitGapMin * m.Scale));
            Height = sub.Bounds.Bottom;
        }
        else
            Height = Base.Bounds.Bottom;
    }
}

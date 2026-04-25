using Mubarrat.OpenType.Tables;
using Mubarrat.OpenType.TextShaping;

namespace Mubarrat.VideoEngine.Latex;

public sealed class ScriptsMathAtom(MathAtom @base, MathAtom? subscript, MathAtom? superscript) : MathAtom(@base?.Type ?? MathAtomType.Ordinary)
{
    public MathAtom Base = @base ?? new SymbolMathAtom();
    public MathAtom? Subscript = subscript;
    public MathAtom? Superscript = superscript;

    protected override IEnumerable<MathAtom> ChildrenIterator
    {
        get
        {
            yield return Base;
            if (Subscript is { } sub) yield return sub;
            if (Superscript is { } sup) yield return sup;
        }
    }

    public override void OnProperty()
    {
        var metrics = Metrics ?? throw new InvalidOperationException("Metrics must be set for ScriptsMathAtom");
        Base.Metrics = metrics;
        Base.Style = Style;
        Base.IsCramped = IsCramped;
        MathStyle scriptStyle = DownScriptStyle(Style);
        var scriptMetrics = ScaleMetrics(metrics, scriptStyle);
        if (Superscript is not null)
        {
            Superscript.Metrics = scriptMetrics;
            Superscript.Style = scriptStyle;
            Superscript.IsCramped = true;
        }
        if (Subscript is not null)
        {
            Subscript.Metrics = scriptMetrics;
            Subscript.Style = scriptStyle;
            Subscript.IsCramped = true;
        }
    }

    public override void OnLayout()
    {
        if (Metrics is not { } m)
            throw new InvalidOperationException("Metrics must be set for ScriptsMathAtom");
        var c = MathTable.MathConstants;
        Base.Location = new(0);
        Subscript?.X = Base.Width;
        Superscript?.X = Base.Width;
        if (Base.IsExtendedShape)
        {
            Subscript?.Y = Base.Height - Subscript.Baseline + c.SubscriptBaselineDropMin * m.Scale;
            Superscript?.Y = -Superscript.Baseline + c.SuperscriptBaselineDropMax * m.Scale;
        }
        else
        {
            Subscript?.Y = Base.Baseline - Subscript.Baseline + c.SubscriptShiftDown * m.Scale;
            Superscript?.Y = Base.Baseline - Superscript.Baseline - (IsCramped ? c.SuperscriptShiftUpCramped : c.SuperscriptShiftUp) * m.Scale;
        }
        Subscript?.Y = Math.Max(Subscript.Y, Base.Baseline - c.SubscriptTopMax * m.Scale);
        Superscript?.Y = Math.Min(Superscript.Y, Base.Baseline - Superscript.Height - c.SuperscriptBottomMin * m.Scale);
        if (Superscript is not null && Subscript is not null)
        {
            double gap = Subscript.Y - Superscript.Bounds.Bottom;
            double mingap = c.SubSuperscriptGapMin * (double)m.Scale;
            if (gap < mingap)
            {
                Superscript.Y = Math.Max(Superscript.Y - mingap + gap, Base.Baseline - Superscript.Height - c.SuperscriptBottomMaxWithSubscript * m.Scale);
                gap = Subscript.Y - Superscript.Bounds.Bottom;
                if (gap < mingap)
                    Subscript.Y -= mingap - gap;
            }
        }
        if (Superscript is not null)
        {
            Base.Y -= Superscript.Y;
            Subscript?.Y -= Superscript.Y;
            Superscript.Y = 0;
        }
        if (Superscript is { } && Base is SymbolMathAtom symbol
            && MathTable.MathGlyphInfo.ItalicsCorrectionInfo is { } info
            && info.Coverage[OpenTypeTextShaper.Shape(symbol.Text, m, new OpenTypeShapingOptions(symbol.ExtraFeatures)).Glyphs[^1].GlyphId] is { } index)
            Superscript.X += info.ItalicsCorrections[index] * m.Scale;
        Width = Math.Max(Subscript?.Bounds.Right ?? Base.Width, Superscript?.Bounds.Right ?? Base.Width) + c.SpaceAfterScript * m.Scale;
        Height = Subscript?.Bounds.Bottom ?? Base.Bounds.Bottom;
        Baseline = Base.Y + Base.Baseline;
    }
}

namespace Mubarrat.VideoEngine.Latex;

public sealed class StretchOverUnderMathAtom(MathAtom @base, string stretchText = "", bool IsOver = false) : MathAtom(MathAtomType.Inner)
{
    public MathAtom Base = @base ?? new SymbolMathAtom();
    public string StretchText = stretchText ?? string.Empty;
    public bool IsOver = IsOver;

    protected override IEnumerable<MathAtom> ChildrenIterator
    {
        get
        {
            yield return Base;
        }
    }

}

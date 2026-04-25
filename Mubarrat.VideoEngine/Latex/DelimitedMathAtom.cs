namespace Mubarrat.VideoEngine.Latex;

public sealed class DelimitedMathAtom(MathAtom body, string leftDelimiter = "", string rightDelimiter = "") : MathAtom(MathAtomType.Inner)
{
    public string LeftDelimiter = leftDelimiter ?? string.Empty;
    public string RightDelimiter = rightDelimiter ?? string.Empty;
    public MathAtom Body = body ?? new SymbolMathAtom();

    protected override IEnumerable<MathAtom> ChildrenIterator
    {
        get
        {
            yield return Body;
        }
    }

}

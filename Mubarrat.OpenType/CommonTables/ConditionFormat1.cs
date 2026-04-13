namespace Mubarrat.OpenType.CommonTables;

public sealed record ConditionFormat1(ushort AxisIndex, float Min, float Max) : ConditionTable
{
    public override bool Matches(FontVariationContext context)
    {
        float v = context.GetAxis(AxisIndex);
        return v >= Min && v <= Max;
    }

    public static ConditionFormat1 Parse(OpenTypeReader.TableScope scope) => new(
        AxisIndex: scope.Reader.ReadUInt16(),
        Min: scope.Reader.ReadF2Dot14(),
        Max: scope.Reader.ReadF2Dot14());
}

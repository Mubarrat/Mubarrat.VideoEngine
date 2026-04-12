namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public abstract record ConditionTable : IOpenTypeCommonTable<ConditionTable>
{
    public abstract bool Matches(FontVariationContext context);

    public static ConditionTable Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        ushort format = scope.Reader.ReadUInt16();
        return format switch
        {
            1 => ConditionFormat1.Parse(scope),
            _ => throw new NotSupportedException($"Unknown condition format {format}")
        };
    }
}

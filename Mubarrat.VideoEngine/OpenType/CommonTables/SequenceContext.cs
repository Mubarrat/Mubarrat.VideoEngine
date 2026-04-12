using Mubarrat.VideoEngine.OpenType.Tables;

namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public abstract record SequenceContext : IOpenTypeCommonTable<SequenceContext>, GposTable.IGposSubtable, GsubTable.IGsubSubtable
{
    public abstract bool TryMatch(ushort[] glyphs, int startIndex, out SequenceMatch match);

    public static SequenceContext Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        if (param is not ushort format)
            return null!;
        return format switch
        {
            1 => SequenceContextFormat1.Parse(scope),
            2 => SequenceContextFormat2.Parse(scope),
            3 => SequenceContextFormat3.Parse(scope),
            _ => throw new NotSupportedException($"Unsupported SequenceContext format {format}")
        };
    }
}

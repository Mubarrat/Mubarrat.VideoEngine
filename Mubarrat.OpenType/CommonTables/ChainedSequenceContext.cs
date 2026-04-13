using Mubarrat.OpenType.Tables;

namespace Mubarrat.OpenType.CommonTables;

public abstract record ChainedSequenceContext : IOpenTypeCommonTable<ChainedSequenceContext>, GposTable.IGposSubtable, GsubTable.IGsubSubtable
{
    public static ChainedSequenceContext Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        if (param is not ushort format)
            return null!;
        return format switch
        {
            1 => ChainedSequenceContextFormat1.Parse(scope),
            2 => ChainedSequenceContextFormat2.Parse(scope),
            3 => ChainedSequenceContextFormat3.Parse(scope),
            _ => throw new NotSupportedException($"ChainedSequenceContext format {format}")
        };
    }
}

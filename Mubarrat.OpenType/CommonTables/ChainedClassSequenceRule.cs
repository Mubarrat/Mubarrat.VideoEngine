namespace Mubarrat.OpenType.CommonTables;

public readonly record struct ChainedClassSequenceRule(
    ushort[] BacktrackSequence,
    ushort[] InputSequence,
    ushort[] LookaheadSequence,
    SequenceLookup[] SeqLookupRecords) : IOpenTypeCommonTable<ChainedClassSequenceRule>
{
    public static ChainedClassSequenceRule Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
        BacktrackSequence: scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()),
        InputSequence: scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()),
        LookaheadSequence: scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()),
        SeqLookupRecords: SequenceLookup.ParseListContiguous(scope));
}

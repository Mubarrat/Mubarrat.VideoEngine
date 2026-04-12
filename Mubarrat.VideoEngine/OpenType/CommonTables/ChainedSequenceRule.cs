namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public sealed record ChainedSequenceRule(
    ushort[] BacktrackCoverages,
    ushort[] InputCoverages,
    ushort[] LookaheadCoverages,
    SequenceLookup[] Lookups) : IOpenTypeCommonTable<ChainedSequenceRule>
{
    public static ChainedSequenceRule Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
        BacktrackCoverages: scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()),
        InputCoverages: scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16() - 1),
        LookaheadCoverages: scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()),
        Lookups: SequenceLookup.ParseListContiguous(scope));
}

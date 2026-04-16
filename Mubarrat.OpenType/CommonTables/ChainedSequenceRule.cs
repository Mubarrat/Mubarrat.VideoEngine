namespace Mubarrat.OpenType.CommonTables;

public sealed record ChainedSequenceRule(
    Coverage[] BacktrackCoverages,
    Coverage[] InputCoverages,
    Coverage[] LookaheadCoverages,
    SequenceLookup[] Lookups) : IOpenTypeCommonTable<ChainedSequenceRule>
{
    public static ChainedSequenceRule Parse(OpenTypeReader.TableScope scope, object? param = null) => new ChainedSequenceRule(
        BacktrackCoverages: Coverage.ParseListFromOffsets16(scope),
        InputCoverages: Coverage.ParseListFromOffsets16(scope, Math.Max(0, scope.Reader.ReadUInt16() - 1)),
        LookaheadCoverages: Coverage.ParseListFromOffsets16(scope),
        Lookups: SequenceLookup.ParseListContiguous(scope));
}

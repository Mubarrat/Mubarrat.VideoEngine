namespace Mubarrat.OpenType.CommonTables;

public sealed record ChainedSequenceContextFormat3(
    Coverage[] BacktrackCoverages,
    Coverage[] InputCoverages,
    Coverage[] LookaheadCoverages,
    SequenceLookup[] Lookups) : ChainedSequenceContext
{
    public static ChainedSequenceContextFormat3 Parse(OpenTypeReader.TableScope scope) => new(
        BacktrackCoverages: Coverage.ParseListFromOffsets16(scope),
        InputCoverages: Coverage.ParseListFromOffsets16(scope),
        LookaheadCoverages: Coverage.ParseListFromOffsets16(scope),
        Lookups: SequenceLookup.ParseListContiguous(scope));
}

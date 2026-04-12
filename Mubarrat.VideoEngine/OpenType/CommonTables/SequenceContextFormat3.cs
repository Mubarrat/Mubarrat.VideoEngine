namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public sealed record SequenceContextFormat3(
    Coverage[] Coverages,
    SequenceLookup[] Lookups) : SequenceContext
{
    public static SequenceContextFormat3 Parse(OpenTypeReader.TableScope scope)
    {
        ushort glyphCount = scope.Reader.ReadUInt16(), lookupCount = scope.Reader.ReadUInt16();
        return new SequenceContextFormat3(
            Coverages: Coverage.ParseListFromOffsets16(scope, glyphCount),
            Lookups: SequenceLookup.ParseListContiguous(scope, lookupCount));
    }

    public override bool TryMatch(ushort[] glyphs, int startIndex, out SequenceMatch match)
    {
        for (int i = 0; i < Coverages.Length; i++)
        {
            if (!Coverages[i].TryGetIndex(glyphs[startIndex + i], out _))
            {
                match = default;
                return false;
            }
        }
        match = new SequenceMatch(startIndex, Coverages.Length, Lookups);
        return true;
    }
}

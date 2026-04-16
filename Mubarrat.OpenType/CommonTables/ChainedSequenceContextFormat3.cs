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

    internal override bool TryMatchChainedContext(ushort[] glyphs, int startIndex, out SequenceMatch match)
    {
        match = default;

        int backtrackCount = BacktrackCoverages.Length;
        int inputCount = InputCoverages.Length;
        int lookaheadCount = LookaheadCoverages.Length;

        if (inputCount == 0)
            return false;

        if (startIndex - backtrackCount < 0)
            return false;
        if (startIndex + inputCount + lookaheadCount > glyphs.Length)
            return false;

        for (int i = 0; i < backtrackCount; i++)
        {
            if (!BacktrackCoverages[i].TryGetIndex(glyphs[startIndex - i - 1], out _))
                return false;
        }

        for (int i = 0; i < inputCount; i++)
        {
            if (!InputCoverages[i].TryGetIndex(glyphs[startIndex + i], out _))
                return false;
        }

        for (int i = 0; i < lookaheadCount; i++)
        {
            if (!LookaheadCoverages[i].TryGetIndex(glyphs[startIndex + inputCount + i], out _))
                return false;
        }

        match = new SequenceMatch(startIndex, inputCount, Lookups);
        return true;
    }
}

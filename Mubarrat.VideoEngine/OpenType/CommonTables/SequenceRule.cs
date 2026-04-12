namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct SequenceRule(
    ushort[] Input,
    SequenceLookup[] Lookups) : IOpenTypeCommonTable<SequenceRule>
{
    public static SequenceRule Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        ushort glyphCount = scope.Reader.ReadUInt16(), lookupCount = scope.Reader.ReadUInt16();
        return new SequenceRule(
            Input: scope.Reader.ReadUInt16Array(glyphCount - 1),
            Lookups: SequenceLookup.ParseListContiguous(scope, lookupCount));
    }

    public bool TryMatch(ushort[] glyphs, int startIndex, out SequenceMatch match)
    {
        for (int i = 0; i < Input.Length; i++)
        {
            if (glyphs[startIndex + i + 1] != Input[i])
            {
                match = default;
                return false;
            }
        }

        match = new SequenceMatch(startIndex, Input.Length + 1, Lookups);
        return true;
    }
}

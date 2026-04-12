namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct ClassSequenceRule(
    ushort[] InputClasses,
    SequenceLookup[] Lookups) : IOpenTypeCommonTable<ClassSequenceRule>
{
    public static ClassSequenceRule Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        ushort glyphCount = scope.Reader.ReadUInt16(), lookupCount = scope.Reader.ReadUInt16();
        return new ClassSequenceRule(
            InputClasses: scope.Reader.ReadUInt16Array(glyphCount - 1),
            Lookups: SequenceLookup.ParseListContiguous(scope, lookupCount));
    }

    public bool TryMatch(ClassDef classDef, ushort[] glyphs, int startIndex, out SequenceMatch match)
    {
        for (int i = 0; i < InputClasses.Length; i++)
        {
            if (!classDef.TryGetClass(glyphs[startIndex + i + 1], out var cls) || cls != InputClasses[i])
            {
                match = default;
                return false;
            }
        }

        match = new SequenceMatch(startIndex, InputClasses.Length + 1, Lookups);
        return true;
    }
}

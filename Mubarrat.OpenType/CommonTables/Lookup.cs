namespace Mubarrat.OpenType.CommonTables;

public readonly record struct Lookup<TSubtable>(
    ushort LookupType,
    LookupFlag LookupFlag,
    TSubtable[] Subtables,
    ushort MarkFilteringSet) : IOpenTypeCommonTable<Lookup<TSubtable>>
    where TSubtable : IOpenTypeCommonTable<TSubtable>
{
    public static Lookup<TSubtable> Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        ushort lookupType = scope.Reader.ReadUInt16();
        LookupFlag lookupFlag = (LookupFlag)scope.Reader.ReadUInt16();
        var subtables = new TSubtable[scope.Reader.ReadUInt16()];
        for (int i = 0; i < subtables.Length; i++)
            subtables[i] = scope.ParseCommonTable<TSubtable>(scope.Reader.ReadUInt16(), lookupType);
        return new(
            lookupType,
            lookupFlag,
            subtables,
            lookupFlag.HasFlag(LookupFlag.UseMarkFilteringSet) ? scope.Reader.ReadUInt16() : default
        );
    }
}

[Flags]
public enum LookupFlag : ushort
{
    RightToLeft = 0x0001,

    IgnoreBaseGlyphs = 0x0002,
    IgnoreLigatures = 0x0004,
    IgnoreMarks = 0x0008,

    UseMarkFilteringSet = 0x0010,

    Reserved = 0x00E0,

    MarkAttachmentClassFilter = 0xFF00
}

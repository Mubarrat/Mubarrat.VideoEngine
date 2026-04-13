using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed class MergTable : IOpenTypeTable
{
    public string Tag => "MERG";

    public ushort Version { get; private set; }
    public ushort MergeClassCount { get; private set; }
    public ushort MergeDataOffset { get; private set; }
    public ushort ClassDefCount { get; private set; }
    public ushort OffsetToClassDefOffsets { get; private set; }

    public ClassDef[] ClassDefinitions { get; private set; } = [];
    public byte[][] MergeRows { get; private set; } = [];

    [Flags]
    public enum MergeEntryFlags : byte
    {
        MergeLtr = 0x01,
        GroupLtr = 0x02,
        SecondIsSubordinateLtr = 0x04,
        MergeRtl = 0x10,
        GroupRtl = 0x20,
        SecondIsSubordinateRtl = 0x40
    }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Version = scope.Reader.ReadUInt16();
        MergeClassCount = scope.Reader.ReadUInt16();
        MergeDataOffset = scope.Reader.ReadOffset16();
        ClassDefCount = scope.Reader.ReadUInt16();
        OffsetToClassDefOffsets = scope.Reader.ReadOffset16();

        ClassDefinitions = new ClassDef[ClassDefCount];
        if (ClassDefCount > 0 && OffsetToClassDefOffsets > 0)
        {
            using var classDefOffsetsScope = scope.EnterScope(OffsetToClassDefOffsets);
            ushort[] classDefOffsets = classDefOffsetsScope.Reader.ReadUInt16Array(ClassDefCount);
            for (int i = 0; i < ClassDefinitions.Length; i++)
                ClassDefinitions[i] = scope.ParseCommonTable<ClassDef>(classDefOffsets[i]);
        }

        MergeRows = new byte[MergeClassCount][];
        if (MergeClassCount > 0 && MergeDataOffset > 0)
        {
            using var mergeDataScope = scope.EnterScope(MergeDataOffset);
            for (int row = 0; row < MergeRows.Length; row++)
                MergeRows[row] = mergeDataScope.Reader.ReadBytes(MergeClassCount);
        }

        tables.Add(this);
    }
}

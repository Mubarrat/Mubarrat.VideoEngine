using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed class VdmxTable : IOpenTypeTable
{
    public string Tag => "VDMX";

    public ushort Version { get; private set; }
    public ushort NumRecs { get; private set; }
    public ushort NumRatios { get; private set; }
    public VdmxRatioRecord[] Ratios { get; private set; } = [];
    public ushort[] GroupOffsets { get; private set; } = [];
    public VdmxGroup[] Groups { get; private set; } = [];

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Version = scope.Reader.ReadUInt16();
        NumRecs = scope.Reader.ReadUInt16();
        NumRatios = scope.Reader.ReadUInt16();

        Ratios = new VdmxRatioRecord[NumRatios];
        for (int i = 0; i < Ratios.Length; i++)
            Ratios[i] = new VdmxRatioRecord(scope.Reader.ReadUInt8(), scope.Reader.ReadUInt8(), scope.Reader.ReadUInt8(), scope.Reader.ReadUInt8());

        GroupOffsets = scope.Reader.ReadUInt16Array(NumRatios);

        Groups = new VdmxGroup[GroupOffsets.Length];
        for (int i = 0; i < Groups.Length; i++)
        {
            using var groupScope = scope.EnterScope(GroupOffsets[i]);
            ushort recs = groupScope.Reader.ReadUInt16();
            byte startsz = groupScope.Reader.ReadUInt8();
            byte endsz = groupScope.Reader.ReadUInt8();

            VdmxRecord[] entries = new VdmxRecord[recs];
            for (int j = 0; j < entries.Length; j++)
                entries[j] = new VdmxRecord(groupScope.Reader.ReadUInt16(), groupScope.Reader.ReadInt16(), groupScope.Reader.ReadInt16());

            Groups[i] = new VdmxGroup(recs, startsz, endsz, entries);
        }

        tables.Add(this);
    }
}

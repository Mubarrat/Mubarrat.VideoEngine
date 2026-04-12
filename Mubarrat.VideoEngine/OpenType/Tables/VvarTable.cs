using Mubarrat.VideoEngine.OpenType.CommonTables;

namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class VvarTable : IOpenTypeTable
{
    public ushort MajorVersion { get; private set; }
    public ushort MinorVersion { get; private set; }

    public ItemVariationStore ItemVariationStore { get; private set; } = default!;

    public DeltaSetIndexMap? AdvanceHeightMapping { get; private set; }
    public DeltaSetIndexMap? TsbMapping { get; private set; }
    public DeltaSetIndexMap? BsbMapping { get; private set; }
    public DeltaSetIndexMap? VOrgMapping { get; private set; }

    public string Tag => "VVAR";

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scoped)
    {
        MajorVersion = scoped.Reader.ReadUInt16();
        MinorVersion = scoped.Reader.ReadUInt16();
        ItemVariationStore = scoped.ParseCommonTable<ItemVariationStore>(scoped.Reader.ReadOffset32());
        AdvanceHeightMapping = scoped.ParseCommonTableOrDefault<DeltaSetIndexMap>(scoped.Reader.ReadOffset32());
        TsbMapping = scoped.ParseCommonTableOrDefault<DeltaSetIndexMap>(scoped.Reader.ReadOffset32());
        BsbMapping = scoped.ParseCommonTableOrDefault<DeltaSetIndexMap>(scoped.Reader.ReadOffset32());
        VOrgMapping = scoped.ParseCommonTableOrDefault<DeltaSetIndexMap>(scoped.Reader.ReadOffset32());
        tables.Add(this);
    }
}

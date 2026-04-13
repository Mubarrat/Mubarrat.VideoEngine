using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed class HvarTable : IOpenTypeTable
{
    public ushort MajorVersion { get; private set; }
    public ushort MinorVersion { get; private set; }

    public ItemVariationStore ItemVariationStore { get; private set; } = default!;

    public DeltaSetIndexMap? AdvanceWidthMapping { get; private set; }
    public DeltaSetIndexMap? LsbMapping { get; private set; }
    public DeltaSetIndexMap? RsbMapping { get; private set; }

    public string Tag => "HVAR";

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scoped)
    {
        MajorVersion = scoped.Reader.ReadUInt16();
        MinorVersion = scoped.Reader.ReadUInt16();
        ItemVariationStore = scoped.ParseCommonTable<ItemVariationStore>(scoped.Reader.ReadOffset32());
        AdvanceWidthMapping = scoped.ParseCommonTableOrDefault<DeltaSetIndexMap>(scoped.Reader.ReadOffset32());
        LsbMapping = scoped.ParseCommonTableOrDefault<DeltaSetIndexMap>(scoped.Reader.ReadOffset32());
        RsbMapping = scoped.ParseCommonTableOrDefault<DeltaSetIndexMap>(scoped.Reader.ReadOffset32());
        tables.Add(this);
    }
}

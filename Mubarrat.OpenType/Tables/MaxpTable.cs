namespace Mubarrat.OpenType.Tables;

public sealed class MaxpTable : IOpenTypeTable
{
    public uint Version { get; private set; }
    public ushort NumGlyphs { get; private set; }
    public ushort MaxPoints { get; private set; }
    public ushort MaxContours { get; private set; }
    public ushort MaxCompositePoints { get; private set; }
    public ushort MaxCompositeContours { get; private set; }
    public ushort MaxZones { get; private set; }
    public ushort MaxTwilightPoints { get; private set; }
    public ushort MaxStorage { get; private set; }
    public ushort MaxFunctionDefs { get; private set; }
    public ushort MaxInstructionDefs { get; private set; }
    public ushort MaxStackElements { get; private set; }
    public ushort MaxSizeOfInstructions { get; private set; }
    public ushort MaxComponentElements { get; private set; }
    public ushort MaxComponentDepth { get; private set; }

    public string Tag => "maxp";

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        var reader = scope.Reader;
        Version = reader.ReadVersion16Dot16();
        NumGlyphs = reader.ReadUInt16();
        if (Version >= 0x00010000)
        {
            MaxPoints = reader.ReadUInt16();
            MaxContours = reader.ReadUInt16();
            MaxCompositePoints = reader.ReadUInt16();
            MaxCompositeContours = reader.ReadUInt16();
            MaxZones = reader.ReadUInt16();
            MaxTwilightPoints = reader.ReadUInt16();
            MaxStorage = reader.ReadUInt16();
            MaxFunctionDefs = reader.ReadUInt16();
            MaxInstructionDefs = reader.ReadUInt16();
            MaxStackElements = reader.ReadUInt16();
            MaxSizeOfInstructions = reader.ReadUInt16();
            MaxComponentElements = reader.ReadUInt16();
            MaxComponentDepth = reader.ReadUInt16();
        }
        tables.Add(this);
    }
}

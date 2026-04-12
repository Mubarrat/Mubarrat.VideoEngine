namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class PcltTable : IOpenTypeTable
{
    public string Tag => "PCLT";

    public uint Version { get; private set; }
    public uint FontNumber { get; private set; }
    public ushort Pitch { get; private set; }
    public ushort XHeight { get; private set; }
    public ushort Style { get; private set; }
    public ushort TypeFamily { get; private set; }
    public ushort CapHeight { get; private set; }
    public ushort SymbolSet { get; private set; }
    public string Typeface { get; private set; } = string.Empty;
    public string CharacterComplement { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public sbyte StrokeWeight { get; private set; }
    public sbyte WidthType { get; private set; }
    public byte SerifStyle { get; private set; }
    public byte Reserved { get; private set; }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        ushort majorVersion = scope.Reader.ReadUInt16();
        ushort minorVersion = scope.Reader.ReadUInt16();
        Version = ((uint)majorVersion << 16) | minorVersion;
        FontNumber = scope.Reader.ReadUInt32();
        Pitch = scope.Reader.ReadUInt16();
        XHeight = scope.Reader.ReadUInt16();
        Style = scope.Reader.ReadUInt16();
        TypeFamily = scope.Reader.ReadUInt16();
        CapHeight = scope.Reader.ReadUInt16();
        SymbolSet = scope.Reader.ReadUInt16();
        Typeface = System.Text.Encoding.ASCII.GetString(scope.Reader.ReadBytes(16)).TrimEnd('\0', ' ');
        CharacterComplement = System.Text.Encoding.ASCII.GetString(scope.Reader.ReadBytes(8)).TrimEnd('\0', ' ');
        FileName = System.Text.Encoding.ASCII.GetString(scope.Reader.ReadBytes(6)).TrimEnd('\0', ' ');
        StrokeWeight = scope.Reader.ReadInt8();
        WidthType = scope.Reader.ReadInt8();
        SerifStyle = scope.Reader.ReadUInt8();
        Reserved = scope.Reader.ReadUInt8();
        tables.Add(this);
    }
}

namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class MathTable : IOpenTypeTable
{
    public string Tag => "MATH";

    public ushort MajorVersion { get; private set; }
    public ushort MinorVersion { get; private set; }
    public ushort MathConstantsOffset { get; private set; }
    public ushort MathGlyphInfoOffset { get; private set; }
    public ushort MathVariantsOffset { get; private set; }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        MajorVersion = scope.Reader.ReadUInt16();
        MinorVersion = scope.Reader.ReadUInt16();
        MathConstantsOffset = scope.Reader.ReadOffset16();
        MathGlyphInfoOffset = scope.Reader.ReadOffset16();
        MathVariantsOffset = scope.Reader.ReadOffset16();
        tables.Add(this);
    }
}

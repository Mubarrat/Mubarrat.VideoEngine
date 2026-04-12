namespace Mubarrat.VideoEngine.OpenType;

public readonly struct OpenTypeTableInfo
{
    public string Tag { get; init; }    // 4-character table tag
    public uint Offset { get; init; }   // Offset from start of font
    public uint Length { get; init; }   // Length of table data
    public byte[] Data { get; init; }   // Raw table data
}

namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class DsigTable : IOpenTypeTable
{
    public string Tag => "DSIG";

    public uint Version { get; private set; }
    public ushort NumSignatures { get; private set; }
    public ushort Flags { get; private set; }
    public SignatureRecord[] SignatureRecords { get; private set; } = [];
    public SignatureBlock[] SignatureBlocks { get; private set; } = [];

    public readonly record struct SignatureRecord(uint Format, uint Length, uint Offset);
    public readonly record struct SignatureBlock(ushort Reserved1, ushort Reserved2, uint SignatureLength, byte[] SignatureData);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Version = scope.Reader.ReadUInt32();
        NumSignatures = scope.Reader.ReadUInt16();
        Flags = scope.Reader.ReadUInt16();

        SignatureRecords = new SignatureRecord[NumSignatures];
        for (int i = 0; i < SignatureRecords.Length; i++)
            SignatureRecords[i] = new SignatureRecord(scope.Reader.ReadUInt32(), scope.Reader.ReadUInt32(), scope.Reader.ReadUInt32());

        SignatureBlocks = new SignatureBlock[SignatureRecords.Length];
        for (int i = 0; i < SignatureRecords.Length; i++)
        {
            var signatureRecord = SignatureRecords[i];
            using var signatureScope = scope.EnterScope(signatureRecord.Offset);
            ushort reserved1 = signatureScope.Reader.ReadUInt16();
            ushort reserved2 = signatureScope.Reader.ReadUInt16();
            uint signatureLength = signatureScope.Reader.ReadUInt32();
            byte[] signatureData = signatureScope.Reader.ReadBytes(checked((int)signatureLength));
            SignatureBlocks[i] = new SignatureBlock(reserved1, reserved2, signatureLength, signatureData);
        }

        tables.Add(this);
    }
}

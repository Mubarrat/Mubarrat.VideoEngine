namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct SequenceLookup(
    ushort SequenceIndex,
    ushort LookupListIndex) : IOpenTypeCommonTable<SequenceLookup>
{
    public static SequenceLookup Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
        SequenceIndex: scope.Reader.ReadUInt16(),
        LookupListIndex: scope.Reader.ReadUInt16());
}

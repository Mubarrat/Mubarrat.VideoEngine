namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct ScriptRecord(string Tag, Script Script) : IOpenTypeCommonTable<ScriptRecord>
{
    public static ScriptRecord Parse(OpenTypeReader.TableScope scopeOfList, object? param = null) => new(
        Tag: scopeOfList.Reader.ReadTag(),
        Script: scopeOfList.ParseCommonTable<Script>(scopeOfList.Reader.ReadOffset16()));
}

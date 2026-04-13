namespace Mubarrat.OpenType.CommonTables;

public readonly record struct LangSysRecord(string Tag, LangSys LangSys)
    : IOpenTypeCommonTable<LangSysRecord>
{
    public static LangSysRecord Parse(OpenTypeReader.TableScope scopeOfScript, object? param = null) => new(
        Tag: scopeOfScript.Reader.ReadTag(),
        LangSys: scopeOfScript.ParseCommonTable<LangSys>(scopeOfScript.Reader.ReadOffset16()));
}

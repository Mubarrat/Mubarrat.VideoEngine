namespace Mubarrat.OpenType.CommonTables;

public readonly record struct Script(LangSys? DefaultLangSys, LangSysRecord[] LangSysRecords) : IOpenTypeCommonTable<Script>
{
    public static Script Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
        DefaultLangSys: scope.ParseCommonTableOrDefault<LangSys>(scope.Reader.ReadOffset16()),
        LangSysRecords: LangSysRecord.ParseListContiguous(scope));
}

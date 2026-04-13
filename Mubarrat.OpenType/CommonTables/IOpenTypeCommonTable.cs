namespace Mubarrat.OpenType.CommonTables;

public interface IOpenTypeCommonTable<T> where T : IOpenTypeCommonTable<T>
{
    static abstract T Parse(OpenTypeReader.TableScope scope, object? param = null);

    public static T[] ParseListContiguous(OpenTypeReader.TableScope scope, object? param = null)
    {
        var tables = new T[param switch
        {
            byte count => count,
            sbyte count => count,
            ushort count => count,
            short count => count,
            uint count => count,
            int count => count,
            _ => scope.Reader.ReadUInt16()
        }];
        for (int i = 0; i < tables.Length; i++)
            tables[i] = T.Parse(scope, param);
        return tables;
    }

    public static T[] ParseListFromOffsets16(OpenTypeReader.TableScope scope, object? param = null)
    {
        var tables = new T[param switch
        {
            byte count => count,
            sbyte count => count,
            ushort count => count,
            short count => count,
            uint count => count,
            int count => count,
            _ => scope.Reader.ReadUInt16()
        }];
        for (int i = 0; i < tables.Length; i++)
            tables[i] = scope.ParseCommonTable<T>(scope.Reader.ReadOffset16(), param);
        return tables;
    }

    public static T[] ParseListFromOffsets32(OpenTypeReader.TableScope scope, object? param = null)
    {
        var tables = new T[param switch
        {
            byte count => count,
            sbyte count => count,
            ushort count => count,
            short count => count,
            uint count => count,
            int count => count,
            _ => scope.Reader.ReadUInt16()
        }];
        for (int i = 0; i < tables.Length; i++)
            tables[i] = scope.ParseCommonTable<T>(scope.Reader.ReadOffset32(), param);
        return tables;
    }
}

public static class OpenTypeCommonTableExtensions
{
    extension<T>(T) where T : IOpenTypeCommonTable<T>
    {
        public static T[] ParseListContiguous(OpenTypeReader.TableScope scope, object? param = null) => IOpenTypeCommonTable<T>.ParseListContiguous(scope, param);
        public static T[] ParseListFromOffsets16(OpenTypeReader.TableScope scope, object? param = null) => IOpenTypeCommonTable<T>.ParseListFromOffsets16(scope, param);
        public static T[] ParseListFromOffsets32(OpenTypeReader.TableScope scope, object? param = null) => IOpenTypeCommonTable<T>.ParseListFromOffsets32(scope, param);
    }
}

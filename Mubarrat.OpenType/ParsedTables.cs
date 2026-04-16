using Mubarrat.OpenType.Tables;

namespace Mubarrat.OpenType;

public sealed class ParsedTables(OpenTypeReader reader)
{
    private readonly OpenTypeReader reader = reader ?? throw new ArgumentNullException(nameof(reader));
    private readonly Dictionary<Type, IOpenTypeTable> tables = [];
    private readonly Dictionary<Type, Action<IOpenTypeTable>> waitingActions = [];
    private readonly Dictionary<string, (uint Offset, uint Length)> tableRecords = new(StringComparer.Ordinal);

    public void Add(IOpenTypeTable table)
    {
        tables.Add(table.GetType(), table);
        if (waitingActions.TryGetValue(table.GetType(), out var action))
        {
            action(table);
            waitingActions.Remove(table.GetType());
        }
    }

    public void RegisterTableRecord(string tag, uint offset, uint length)
        => tableRecords[tag] = (offset, length);

    public bool TryGetTableRecord(string tag, out (uint Offset, uint Length) record)
        => tableRecords.TryGetValue(tag, out record);

    public void Request<D>(Action<D, OpenTypeReader.TableScope> onAvailable) where D : IOpenTypeTable
    {
        long position = reader.Position;
        Type type = typeof(D);
        if (!tables.TryGetValue(type, out var existing))
        {
            if (!waitingActions.ContainsKey(type)) waitingActions[type] = null!;
            waitingActions[type] += d =>
            {
                using (var scope = reader.EnterScope(position))
                    onAvailable((D)d, scope);
            };
            return;
        }
        using (var scope = reader.EnterScope(position))
            onAvailable((D)existing, scope);
    }

    public void Request<D1, D2>(Action<D1, D2, OpenTypeReader.TableScope> onAvailable) where D1 : IOpenTypeTable where D2 : IOpenTypeTable
        => Request<D1>((d1, _) => Request<D2>((d2, scope) => onAvailable(d1, d2, scope)));

    public void Request<D1, D2, D3>(Action<D1, D2, D3, OpenTypeReader.TableScope> onAvailable) where D1 : IOpenTypeTable where D2 : IOpenTypeTable where D3 : IOpenTypeTable
        => Request<D1, D2>((d1, d2, _) => Request<D3>((d3, scope) => onAvailable(d1, d2, d3, scope)));

    public void Request<D1, D2, D3, D4>(Action<D1, D2, D3, D4, OpenTypeReader.TableScope> onAvailable) where D1 : IOpenTypeTable where D2 : IOpenTypeTable where D3 : IOpenTypeTable where D4 : IOpenTypeTable
        => Request<D1, D2, D3>((d1, d2, d3, _) => Request<D4>((d4, scope) => onAvailable(d1, d2, d3, d4, scope)));

    public bool TryGet<T>(out T table) where T : IOpenTypeTable
    {
        if (tables.TryGetValue(typeof(T), out var t))
        {
            table = (T)t;
            return true;
        }
        table = default!;
        return false;
    }
}

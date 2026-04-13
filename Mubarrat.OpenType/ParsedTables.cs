using Mubarrat.OpenType.Tables;

namespace Mubarrat.OpenType;

public sealed class ParsedTables(OpenTypeReader reader)
{
    private readonly OpenTypeReader reader = reader ?? throw new ArgumentNullException(nameof(reader));
    private readonly Dictionary<Type, IOpenTypeTable> tables = [];
    private readonly Dictionary<Type, List<Action<IOpenTypeTable>>> waitingActions = [];

    public void Add(IOpenTypeTable table)
    {
        tables.Add(table.GetType(), table);
        if (waitingActions.TryGetValue(table.GetType(), out var list))
        {
            list.ForEach(action => action(table));
            waitingActions.Remove(table.GetType());
        }
    }

    public void Request<D>(Action<D, OpenTypeReader.TableScope> onAvailable) where D : IOpenTypeTable
    {
        long position = reader.Position;
        Type type = typeof(D);
        if (!tables.TryGetValue(type, out var existing))
        {
            if (!waitingActions.TryGetValue(type, out var list)) waitingActions[type] = list = [];
            list.Add(d =>
            {
                using (var scope = reader.EnterScope(position))
                    onAvailable((D)d, scope);
            });
            return;
        }
        using (var scope = reader.EnterScope(position))
            onAvailable((D)existing, scope);
    }

    public void Request<D1, D2>(Action<D1, D2, OpenTypeReader.TableScope> onAvailable) where D1 : IOpenTypeTable where D2 : IOpenTypeTable
        => Request<D1>((d1, _) => Request<D2>((d2, scope) => onAvailable(d1, d2, scope)));

    public void Request<D1, D2, D3>(Action<D1, D2, D3, OpenTypeReader.TableScope> onAvailable) where D1 : IOpenTypeTable where D2 : IOpenTypeTable where D3 : IOpenTypeTable
        => Request<D1, D2>((d1, d2, _) => Request<D3>((d3, scope) => onAvailable(d1, d2, d3, scope)));

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

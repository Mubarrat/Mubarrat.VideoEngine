namespace Mubarrat.VideoEngine.Objects;

public class BaseObject : ICloneable
{
    private Dictionary<Property, object> _values = [];

    public object this[Property property]
    {
        get => _values.TryGetValue(property, out var value) ? value : GetDefaultValue(property);
        set
        {
            if (value != null && !property.PropertyType.IsAssignableFrom(value.GetType()))
                throw new InvalidOperationException($"Value must be of type {property.PropertyType.Name}");
            if (!_values.TryGetValue(property, out var old)) old = null;
            _values[property] = value!;
            OnPropertyChanged(property, old, value);
        }
    }

    protected virtual object GetDefaultValue(Property property) => property.DefaultValue!;

    protected virtual void OnPropertyChanged(Property property, object? oldValue, object? newValue) {}

    public virtual object Clone()
    {
        BaseObject copy = (BaseObject)MemberwiseClone();
        copy._values = new(_values); // Need a different dictionary instance to avoid shared state
        return copy;
    }
}

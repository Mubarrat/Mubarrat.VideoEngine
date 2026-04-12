namespace Mubarrat.VideoEngine.Objects;

public record class Property(
    string Name,
    Type PropertyType,
    object? defaultValue = null,
    bool IsInherited = false,
    bool AffectsLayout = false,
    bool IsAnimatable = true)
{
    public override string ToString() => $"{Name} ({PropertyType.Name})";
}

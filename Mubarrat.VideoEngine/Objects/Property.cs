namespace Mubarrat.VideoEngine.Objects;

public record class Property(
    string Name,
    Type PropertyType,
    object? DefaultValue = null,
    bool IsInherited = false,
    bool AffectsMeasure = false,
    bool AffectsArrange = false,
    bool AffectsParentMeasure = false,
    bool AffectsParentArrange = false)
{
    public override string ToString() => $"{Name} ({PropertyType.Name})";
}

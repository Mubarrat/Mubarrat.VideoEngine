namespace Mubarrat.VideoEngine.Path;

// ─────────────────────────────────────────────────────────────────────────────
// CompiledShape — the IL-generated delegate bundle
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class CompiledShape(
    Func<double, double, double> distance,
    Func<double, double, int> winding)
{
    // Generated via IL emit — no virtual dispatch, all constants baked in
    public readonly Func<double, double, double> Distance = distance;
    public readonly Func<double, double, int> Winding = winding;
}

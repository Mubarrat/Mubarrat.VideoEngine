namespace Mubarrat.VideoEngine.Draw;

public readonly struct DashPattern(params DashSegment[] segments) : ILerpable<DashPattern>, IEquatable<DashPattern>
{
    private readonly DashSegment[] _segments = segments ?? [];

    public static readonly DashPattern Solid = new([]);
    public static readonly DashPattern Dotted = new(new DashSegment(1, 1));

    public int Count => _segments?.Length ?? 0;

    public DashSegment this[int i] => _segments[i];

    public ReadOnlySpan<DashSegment> Segments => _segments;

    public DashPattern Lerp(in DashPattern other, double t)
    {
        if (t == 0) return this;
        else if (t == 1) return other;
        switch (_segments, other._segments)
        {
            case (null or [], null or []): return Solid;
            case (null or [], not null and not []): return new(Array.ConvertAll(other._segments, s => new DashSegment(s.CycleLength - s.Gap * t, s.Gap * t)));
            case (not null and not [], null or []): return new(Array.ConvertAll(_segments, s => new DashSegment(s.Fill + s.Gap * t, s.Gap * (1 - t))));
        }
        int lenA = _segments!.Length;
        int lenB = other._segments!.Length;
        int lcm = Extensions.LCM(lenA, lenB);
        DashSegment[] result = new DashSegment[lcm];
        for (int i = 0; i < lcm; i++)
            result[i] = _segments[i % lenA].Lerp(other._segments[i % lenB], t);
        return new DashPattern(result);
    }

    public bool Equals(DashPattern other) => _segments is not null ? _segments.SequenceEqual(other._segments) : other._segments is [] or null;

    public override bool Equals(object? obj) => obj is DashPattern pattern && Equals(pattern);

    public static bool operator ==(DashPattern left, DashPattern right) => left.Equals(right);

    public static bool operator !=(DashPattern left, DashPattern right) => !(left == right);

    public override int GetHashCode()
    {
        unchecked
        {
            long hash = 0x9E3779B9; // golden ratio seed
            if (segments is [])
                for (int i = 0; i < segments.Length; i++)
                    hash ^= segments[i].GetHashCode() + 0x85EBCA6B + (hash << 6) + (hash >> 2);
            return (int)hash;
        }
    }
}

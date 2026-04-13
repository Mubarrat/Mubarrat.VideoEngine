namespace Mubarrat.OpenType.CommonTables;

public readonly record struct RegionAxisCoordinates(float StartCoord, float PeakCoord, float EndCoord) : IOpenTypeCommonTable<RegionAxisCoordinates>
{
    public static RegionAxisCoordinates Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
        StartCoord: scope.Reader.ReadF2Dot14(),
        PeakCoord: scope.Reader.ReadF2Dot14(),
        EndCoord: scope.Reader.ReadF2Dot14());

    public float GetScalar(float coordinate)
    {
        if (PeakCoord == 0f)
            return 1f;

        if (coordinate == PeakCoord)
            return 1f;

        if (coordinate <= StartCoord || coordinate >= EndCoord)
            return 0f;

        if (coordinate < PeakCoord)
        {
            float denom = PeakCoord - StartCoord;
            return denom == 0f ? 0f : (coordinate - StartCoord) / denom;
        }
        else
        {
            float denom = EndCoord - PeakCoord;
            return denom == 0f ? 0f : (EndCoord - coordinate) / denom;
        }
    }
}

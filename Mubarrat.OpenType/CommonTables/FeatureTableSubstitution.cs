namespace Mubarrat.OpenType.CommonTables;

public readonly record struct FeatureTableSubstitution(FeatureSubstitutionRecord[] Records) : IOpenTypeCommonTable<FeatureTableSubstitution>
{
    public bool TryGetSubstitution(int featureIndex, out FeatureSubstitutionRecord record)
    {
        for (int i = 0; i < Records.Length; i++)
        {
            ref var r = ref Records[i];

            if (r.FeatureIndex == featureIndex)
            {
                record = r;
                return true;
            }

            if (r.FeatureIndex > featureIndex)
                break;
        }

        record = default;
        return false;
    }

    public static FeatureTableSubstitution Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        scope.Reader.ReadUInt16(); // major
        scope.Reader.ReadUInt16(); // minor
        var records = new FeatureSubstitutionRecord[scope.Reader.ReadUInt16()];
        for (int i = 0; i < records.Length; i++)
            records[i] = FeatureSubstitutionRecord.Parse(scope);
        return new FeatureTableSubstitution(records);
    }
}

namespace Mubarrat.OpenType.CommonTables;

public readonly record struct DeltaSet(int[] Deltas)
{
    public static DeltaSet Parse(OpenTypeReader.TableScope scope, ushort regionCount, ushort wordCount, bool longWords)
    {
        var r = scope.Reader;

        var deltas = new int[regionCount];

        if (!longWords)
        {
            // int16 + int8
            for (int i = 0; i < wordCount; i++)
                deltas[i] = r.ReadInt16();

            for (int i = wordCount; i < regionCount; i++)
                deltas[i] = r.ReadInt8();
        }
        else
        {
            // int32 + int16
            for (int i = 0; i < wordCount; i++)
                deltas[i] = r.ReadInt32();

            for (int i = wordCount; i < regionCount; i++)
                deltas[i] = r.ReadInt16();
        }

        return new DeltaSet(deltas);
    }
}

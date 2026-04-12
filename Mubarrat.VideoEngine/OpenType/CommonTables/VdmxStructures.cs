namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct VdmxRatioRecord(byte Charset, byte XRatio, byte YStartRatio, byte YEndRatio);
public readonly record struct VdmxRecord(ushort YPelHeight, short YMax, short YMin);
public readonly record struct VdmxGroup(ushort Recs, byte Startsz, byte Endsz, VdmxRecord[] Entries);

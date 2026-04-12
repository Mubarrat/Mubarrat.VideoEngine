namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct SequenceMatch(int Start, int Length, SequenceLookup[] Lookups);

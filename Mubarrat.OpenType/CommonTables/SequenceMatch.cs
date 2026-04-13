namespace Mubarrat.OpenType.CommonTables;

public readonly record struct SequenceMatch(int Start, int Length, SequenceLookup[] Lookups);

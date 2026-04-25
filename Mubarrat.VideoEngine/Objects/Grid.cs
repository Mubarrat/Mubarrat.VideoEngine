namespace Mubarrat.VideoEngine.Objects;

public class Grid : Panel
{
    private readonly Dictionary<FrameworkObject, Rect> arrangedSlots = [];

    public int Rows { get => (int)this[RowsProperty]; set => this[RowsProperty] = Math.Max(1, value); }
    public static readonly Property RowsProperty = new(nameof(Rows), typeof(int), 1, AffectsMeasure: true, AffectsArrange: true);

    public int Columns { get => (int)this[ColumnsProperty]; set => this[ColumnsProperty] = Math.Max(1, value); }
    public static readonly Property ColumnsProperty = new(nameof(Columns), typeof(int), 1, AffectsMeasure: true, AffectsArrange: true);

    public static int GetRow(FrameworkObject element) => Math.Max(0, (int)element[RowProperty]);
    public static void SetRow(FrameworkObject element, int value) => element[RowProperty] = Math.Max(0, value);
    public static readonly Property RowProperty = new("Row", typeof(int), 0, AffectsParentMeasure: true, AffectsParentArrange: true);

    public static int GetColumn(FrameworkObject element) => Math.Max(0, (int)element[ColumnProperty]);
    public static void SetColumn(FrameworkObject element, int value) => element[ColumnProperty] = Math.Max(0, value);
    public static readonly Property ColumnProperty = new("Column", typeof(int), 0, AffectsParentMeasure: true, AffectsParentArrange: true);

    public static int GetRowSpan(FrameworkObject element) => Math.Max(1, (int)element[RowSpanProperty]);
    public static void SetRowSpan(FrameworkObject element, int value) => element[RowSpanProperty] = Math.Max(1, value);
    public static readonly Property RowSpanProperty = new("RowSpan", typeof(int), 1, AffectsParentMeasure: true, AffectsParentArrange: true);

    public static int GetColumnSpan(FrameworkObject element) => Math.Max(1, (int)element[ColumnSpanProperty]);
    public static void SetColumnSpan(FrameworkObject element, int value) => element[ColumnSpanProperty] = Math.Max(1, value);
    public static readonly Property ColumnSpanProperty = new("ColumnSpan", typeof(int), 1, AffectsParentMeasure: true, AffectsParentArrange: true);

    public override Size OnMeasure(Size availableSize)
    {
        int rows = Math.Max(1, Rows);
        int columns = Math.Max(1, Columns);

        foreach (var child in ChildrenIterator)
        {
            rows = Math.Max(rows, GetRow(child) + GetRowSpan(child));
            columns = Math.Max(columns, GetColumn(child) + GetColumnSpan(child));
        }

        double cellWidth = double.IsFinite(availableSize.Width) ? availableSize.Width / columns : double.PositiveInfinity;
        double cellHeight = double.IsFinite(availableSize.Height) ? availableSize.Height / rows : double.PositiveInfinity;

        double[] rowHeights = new double[rows];
        double[] columnWidths = new double[columns];

        foreach (var child in ChildrenIterator)
        {
            int row = Math.Min(GetRow(child), rows - 1);
            int column = Math.Min(GetColumn(child), columns - 1);
            int rowSpan = Math.Min(GetRowSpan(child), rows - row);
            int columnSpan = Math.Min(GetColumnSpan(child), columns - column);

            double desiredWidthPerColumn = child.DesiredSize.Width / columnSpan;
            double desiredHeightPerRow = child.DesiredSize.Height / rowSpan;

            for (int r = row; r < row + rowSpan; r++)
                rowHeights[r] = Math.Max(rowHeights[r], Math.Min(cellHeight, desiredHeightPerRow));

            for (int c = column; c < column + columnSpan; c++)
                columnWidths[c] = Math.Max(columnWidths[c], Math.Min(cellWidth, desiredWidthPerColumn));
        }

        return new Size(columnWidths.Sum(), rowHeights.Sum());
    }

    protected override Size GetChildMeasureConstraint(FrameworkObject child, Size availableSize)
    {
        int rows = Math.Max(1, Rows);
        int columns = Math.Max(1, Columns);
        int row = Math.Min(GetRow(child), rows - 1);
        int column = Math.Min(GetColumn(child), columns - 1);
        int rowSpan = Math.Min(GetRowSpan(child), rows - row);
        int columnSpan = Math.Min(GetColumnSpan(child), columns - column);

        double width = double.IsFinite(availableSize.Width) ? (availableSize.Width / columns) * columnSpan : double.PositiveInfinity;
        double height = double.IsFinite(availableSize.Height) ? (availableSize.Height / rows) * rowSpan : double.PositiveInfinity;
        return new Size(width, height);
    }

    public override void OnArrange(Size finalSize, Matrix2D transform)
    {
        arrangedSlots.Clear();

        int rows = Math.Max(1, Rows);
        int columns = Math.Max(1, Columns);
        double cellWidth = columns == 0 ? 0 : finalSize.Width / columns;
        double cellHeight = rows == 0 ? 0 : finalSize.Height / rows;

        foreach (var child in ChildrenIterator)
        {
            int row = Math.Min(GetRow(child), rows - 1);
            int column = Math.Min(GetColumn(child), columns - 1);
            int rowSpan = Math.Min(GetRowSpan(child), rows - row);
            int columnSpan = Math.Min(GetColumnSpan(child), columns - column);

            arrangedSlots[child] = new Rect(
                column * cellWidth,
                row * cellHeight,
                cellWidth * columnSpan,
                cellHeight * rowSpan);
        }
    }

    protected override Matrix2D GetChildTransform(FrameworkObject child, Size availableSize)
    {
        if (!arrangedSlots.TryGetValue(child, out var slot))
            return Matrix2D.Identity;

        return Matrix2D.Translate(slot.X, slot.Y);
    }

    protected override Size GetChildArrangeSize(FrameworkObject child, Size availableSize)
        => arrangedSlots.TryGetValue(child, out var slot)
            ? slot.Size
            : child.DesiredSize;
}

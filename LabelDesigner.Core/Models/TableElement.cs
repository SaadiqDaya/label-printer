namespace LabelDesigner.Core.Models;

public class TableColumn
{
    public string Header     { get; set; } = "Column";
    public string BoundField { get; set; } = "";
    public double Width      { get; set; } = 60;
}

public class TableElement : LabelElement
{
    public List<TableColumn> Columns         { get; set; } = new() { new TableColumn { Header = "Column 1" } };
    public double  RowHeight                 { get; set; } = 20;
    public string  HeaderBackground          { get; set; } = "#E0E0E0";
    public string  CellBackground            { get; set; } = "#FFFFFF";
    public string  BorderColor               { get; set; } = "#000000";
    public double  BorderThickness           { get; set; } = 1;
    public string  FontFamily                { get; set; } = "Arial";
    public double  HeaderFontSize            { get; set; } = 9;
    public double  CellFontSize              { get; set; } = 9;
    public bool    ShowHeader                { get; set; } = true;

    /// <summary>Manually-entered data rows. Each inner list has one value per column (by index).
    /// When non-empty these rows are printed instead of looking up the single bound-field value.</summary>
    public List<List<string>> StaticRows { get; set; } = new();

    public override ElementType Type => ElementType.Table;

    public override LabelElement Clone() => new TableElement
    {
        Id                = Guid.NewGuid(),
        X = X, Y = Y, Width = Width, Height = Height,
        ZIndex            = ZIndex,
        PrintCondition    = PrintCondition,
        LayerId           = LayerId,
        BackgroundColor   = BackgroundColor,
        Rotation          = Rotation,
        Columns           = Columns.Select(c => new TableColumn
                            { Header = c.Header, BoundField = c.BoundField, Width = c.Width }).ToList(),
        RowHeight         = RowHeight,
        HeaderBackground  = HeaderBackground,
        CellBackground    = CellBackground,
        BorderColor       = BorderColor,
        BorderThickness   = BorderThickness,
        FontFamily        = FontFamily,
        HeaderFontSize    = HeaderFontSize,
        CellFontSize      = CellFontSize,
        ShowHeader        = ShowHeader,
        StaticRows        = StaticRows.Select(r => r.ToList()).ToList()
    };
}

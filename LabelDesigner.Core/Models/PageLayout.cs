namespace LabelDesigner.Core.Models;

/// <summary>
/// Optional sheet-printing layout for a template. When null (the default) the template prints
/// directly on label-sized media — the thermal/Kiaro behaviour, unchanged. When set, labels are
/// composed onto pages of <see cref="PageWidthMm"/> × <see cref="PageHeightMm"/> in a
/// Rows × Columns grid (Avery-style sheet labels, cards, menus). GDI output only — ZPL printers
/// are label-media devices and ignore this.
/// </summary>
public class PageLayout
{
    // Letter by default — the stock in both office printers.
    public double PageWidthMm  { get; set; } = 215.9;
    public double PageHeightMm { get; set; } = 279.4;

    public int Rows    { get; set; } = 1;
    public int Columns { get; set; } = 1;

    /// <summary>Position of the FIRST cell's top-left corner on the page.</summary>
    public double MarginLeftMm { get; set; }
    public double MarginTopMm  { get; set; }

    /// <summary>Spacing between cells (added to the label size to give the grid pitch).</summary>
    public double GutterXMm { get; set; }
    public double GutterYMm { get; set; }

    /// <summary>True (default): fill left→right then next row. False: fill top→bottom then next column.</summary>
    public bool FillAcrossFirst { get; set; } = true;

    /// <summary>
    /// Name of the template printed as the BACK side of each sheet (duplex — e.g. flavor-card
    /// backs). Blank = single-sided. Back cells are mirrored left↔right so a long-edge flip
    /// lines them up with their fronts. Validate on the physical printer before production.
    /// </summary>
    public string BackTemplateName { get; set; } = "";

    public int CellsPerPage => Math.Max(1, Rows) * Math.Max(1, Columns);

    /// <summary>
    /// Top-left of the given cell (0-based, clamped) in page millimetres.
    /// <paramref name="mirrorColumns"/> flips left↔right for duplex backs.
    /// </summary>
    public (double XMm, double YMm) CellOriginMm(int cell, double labelWidthMm, double labelHeightMm,
        bool mirrorColumns = false)
    {
        int cols = Math.Max(1, Columns), rows = Math.Max(1, Rows);
        cell = Math.Clamp(cell, 0, cols * rows - 1);
        int col, row;
        if (FillAcrossFirst) { col = cell % cols; row = cell / cols; }
        else                 { row = cell % rows; col = cell / rows; }
        if (mirrorColumns) col = cols - 1 - col;
        return (MarginLeftMm + col * (labelWidthMm + GutterXMm),
                MarginTopMm  + row * (labelHeightMm + GutterYMm));
    }

    /// <summary>True when two layouts produce the same grid — required for printing rows of
    /// DIFFERENT templates onto one sheet (menus): cells must line up.</summary>
    public bool SameGridAs(PageLayout? other) =>
        other != null &&
        PageWidthMm == other.PageWidthMm && PageHeightMm == other.PageHeightMm &&
        Rows == other.Rows && Columns == other.Columns &&
        MarginLeftMm == other.MarginLeftMm && MarginTopMm == other.MarginTopMm &&
        GutterXMm == other.GutterXMm && GutterYMm == other.GutterYMm &&
        FillAcrossFirst == other.FillAcrossFirst;

    /// <summary>Avery 5160/8160-compatible: 30 labels (2⅝″ × 1″ = 66.7 × 25.4 mm) per Letter sheet,
    /// 3 columns × 10 rows. The label TEMPLATE must be 66.7 × 25.4 mm.</summary>
    public static PageLayout Avery5160() => new()
    {
        PageWidthMm = 215.9, PageHeightMm = 279.4,
        Rows = 10, Columns = 3,
        MarginLeftMm = 4.7625,   // 0.1875"
        MarginTopMm  = 12.7,     // 0.5"
        GutterXMm    = 3.175,    // 0.125"
        GutterYMm    = 0
    };
}

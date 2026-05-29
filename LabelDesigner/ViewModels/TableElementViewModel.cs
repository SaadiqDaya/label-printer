using LabelDesigner.Core.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;

namespace LabelDesigner.ViewModels;

// ─── Cell / Row view-models ──────────────────────────────────────────────────

public class TableCellViewModel : ViewModelBase
{
    private string _value;
    public string Value { get => _value; set => Set(ref _value, value); }
    public TableCellViewModel(string value = "") => _value = value;
}

public class TableRowViewModel : ViewModelBase
{
    public ObservableCollection<TableCellViewModel> Cells { get; } = new();

    public TableRowViewModel(IEnumerable<string>? values, int columnCount)
    {
        var vals = values?.ToList() ?? new List<string>();
        for (int i = 0; i < columnCount; i++)
            Cells.Add(new TableCellViewModel(i < vals.Count ? vals[i] : ""));
    }

    /// <summary>Ensures the Cells collection has exactly <paramref name="columnCount"/> items.</summary>
    public void Resize(int columnCount)
    {
        while (Cells.Count < columnCount) Cells.Add(new TableCellViewModel(""));
        while (Cells.Count > columnCount) Cells.RemoveAt(Cells.Count - 1);
    }
}

// ─── Column view-model ───────────────────────────────────────────────────────

public class TableColumnViewModel : ViewModelBase
{
    private string _header;
    private string _boundField;
    private double _width;

    public string Header
    {
        get => _header;
        set => Set(ref _header, value);
    }

    public string BoundField
    {
        get => _boundField;
        set => Set(ref _boundField, value);
    }

    public double Width
    {
        get => _width;
        set => Set(ref _width, value);
    }

    /// <summary>Field names available for the bound-field ComboBox — shared reference from the parent TableElementViewModel.</summary>
    public ObservableCollection<string> AvailableFields { get; }

    public TableColumnViewModel(TableColumn model, ObservableCollection<string> availableFields)
    {
        _header      = model.Header;
        _boundField  = model.BoundField;
        _width       = model.Width;
        AvailableFields = availableFields;
    }

    public TableColumnViewModel(ObservableCollection<string> availableFields)
        : this(new TableColumn(), availableFields) { }

    public TableColumn ToModel() => new() { Header = _header, BoundField = _boundField, Width = _width };
}

// ─── Table element view-model ────────────────────────────────────────────────

public class TableElementViewModel : ElementViewModelBase
{
    private TableElement _model = new();
    private Dictionary<string, string>? _liveFields;

    public override ElementType ElementType => ElementType.Table;
    public override string DisplayName =>
        $"Table ({Columns.Count} col{(Columns.Count == 1 ? "" : "s")})";

    public ObservableCollection<TableColumnViewModel> Columns { get; } = new();
    public ObservableCollection<TableRowViewModel>    Rows    { get; } = new();

    public TableElementViewModel()
    {
        // Initialize columns from the model's default columns (so a new table starts with Column 1)
        foreach (var col in _model.Columns)
            Columns.Add(new TableColumnViewModel(col, AvailableFields));

        // Seed one editable row so the cell editor in the Properties Panel is immediately visible.
        // Without this a fresh table has Rows.Count == 0 and the editor looks empty — users couldn't
        // tell where to click to add data.
        Rows.Add(new TableRowViewModel(null, Columns.Count));
    }

    // ─── Styling properties ───────────────────────────────────────────────────

    public double RowHeight
    {
        get => _model.RowHeight;
        set { _model.RowHeight = Math.Max(8, value); OnPropertyChanged(); }
    }

    public string HeaderBackground
    {
        get => _model.HeaderBackground;
        set { _model.HeaderBackground = value; OnPropertyChanged(); OnPropertyChanged(nameof(HeaderBrush)); }
    }

    public string CellBackground
    {
        get => _model.CellBackground;
        set { _model.CellBackground = value; OnPropertyChanged(); OnPropertyChanged(nameof(CellBrush)); }
    }

    public string BorderColor
    {
        get => _model.BorderColor;
        set { _model.BorderColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(BorderBrush)); }
    }

    public double TableBorderThickness
    {
        get => _model.BorderThickness;
        set { _model.BorderThickness = Math.Max(0, value); OnPropertyChanged(); }
    }

    public string TableFontFamily
    {
        get => _model.FontFamily;
        set { _model.FontFamily = value; OnPropertyChanged(); }
    }

    public double HeaderFontSize
    {
        get => _model.HeaderFontSize;
        set { _model.HeaderFontSize = Math.Max(1, value); OnPropertyChanged(); }
    }

    public double CellFontSize
    {
        get => _model.CellFontSize;
        set { _model.CellFontSize = Math.Max(1, value); OnPropertyChanged(); }
    }

    public bool ShowHeader
    {
        get => _model.ShowHeader;
        set { _model.ShowHeader = value; OnPropertyChanged(); }
    }

    // Brush helpers for canvas display
    public SolidColorBrush? HeaderBrush => TryBrush(HeaderBackground);
    public SolidColorBrush? CellBrush   => TryBrush(CellBackground);
    public SolidColorBrush? BorderBrush => TryBrush(BorderColor);

    // ─── Column commands ──────────────────────────────────────────────────────

    public ICommand AddColumnCommand    => new RelayCommand(AddColumn);
    public ICommand RemoveColumnCommand => new RelayCommand(RemoveColumn, () => Columns.Count > 1);

    private void AddColumn()
    {
        Columns.Add(new TableColumnViewModel(
            new TableColumn { Header = $"Column {Columns.Count + 1}" },
            AvailableFields));
        // Extend each existing row with an empty cell
        foreach (var row in Rows) row.Resize(Columns.Count);
        OnPropertyChanged(nameof(DisplayName));
    }

    private void RemoveColumn()
    {
        if (Columns.Count <= 1) return;
        Columns.RemoveAt(Columns.Count - 1);
        foreach (var row in Rows) row.Resize(Columns.Count);
        OnPropertyChanged(nameof(DisplayName));
    }

    // ─── Row commands ─────────────────────────────────────────────────────────

    public ICommand AddRowCommand    => new RelayCommand(AddRow);
    public ICommand RemoveRowCommand => new RelayCommand(RemoveRow, () => Rows.Count > 0);

    private void AddRow()
    {
        Rows.Add(new TableRowViewModel(null, Columns.Count));
    }

    private void RemoveRow()
    {
        if (Rows.Count > 0) Rows.RemoveAt(Rows.Count - 1);
    }

    // ─── Preview ──────────────────────────────────────────────────────────────

    public override void UpdatePreview(Dictionary<string, string>? fields)
    {
        _liveFields = fields;
    }

    // ─── Serialisation ────────────────────────────────────────────────────────

    public override LabelElement ToModel()
    {
        _model.Id             = Id;
        _model.X              = X;
        _model.Y              = Y;
        _model.Width          = Width;
        _model.Height         = Height;
        _model.ZIndex         = ZIndex;
        _model.PrintCondition = PrintCondition;
        _model.LayerId        = LayerId;
        _model.BackgroundColor = BackgroundColor;
        _model.Rotation       = Rotation;
        _model.Columns    = Columns.Select(c => c.ToModel()).ToList();
        _model.StaticRows = Rows.Select(r => r.Cells.Select(c => c.Value).ToList()).ToList();
        return _model;
    }

    public override void FromModel(LabelElement element)
    {
        if (element is not TableElement te) return;
        _model = te;

        Id             = te.Id;
        X              = te.X;
        Y              = te.Y;
        Width          = te.Width;
        Height         = te.Height;
        ZIndex         = te.ZIndex;
        PrintCondition = te.PrintCondition;
        BackgroundColor = te.BackgroundColor;
        Rotation       = te.Rotation;

        LayerId = te.LayerId;

        Columns.Clear();
        foreach (var col in te.Columns)
            Columns.Add(new TableColumnViewModel(col, AvailableFields));

        Rows.Clear();
        foreach (var rowData in te.StaticRows)
            Rows.Add(new TableRowViewModel(rowData, te.Columns.Count));

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(HeaderBrush));
        OnPropertyChanged(nameof(CellBrush));
        OnPropertyChanged(nameof(BorderBrush));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static SolidColorBrush? TryBrush(string color)
    {
        try { return (SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(color)!; }
        catch { return null; }
    }
}

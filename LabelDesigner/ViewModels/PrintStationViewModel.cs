using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Services;
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace LabelDesigner.ViewModels;

/// <summary>One operator-entered field on the Print Station form (manual-entry mode).</summary>
public class FieldInputViewModel : ViewModelBase
{
    public string Name { get; }
    public string Prompt { get; }
    public bool Required { get; }
    public string PromptLabel => Required ? Prompt + " *" : Prompt;

    /// <summary>Allowed values → drives a dropdown when present (otherwise a free text box).</summary>
    public IReadOnlyList<string> AllowedValues { get; }
    public bool HasChoices => AllowedValues.Count > 0;

    private string _value;
    public string Value { get => _value; set { if (Set(ref _value, value)) ValueChanged?.Invoke(); } }
    public event Action? ValueChanged;

    public FieldInputViewModel(string name, string prompt, string value, bool required, IReadOnlyList<string> allowedValues)
    {
        Name = name; Prompt = prompt; _value = value; Required = required; AllowedValues = allowedValues;
    }
}

/// <summary>A data-file record shown in the Print Station's record list.</summary>
public class RowItem
{
    public int Index { get; }     // 0-based
    public string Label { get; }
    public int PrintQty { get; }
    public RowItem(int index, string label, int printQty) { Index = index; Label = label; PrintQty = printQty; }
}

/// <summary>
/// Shop-floor Print Station: a read-only operator surface. It HONORS the data connection the
/// template already carries (DefaultExcelPath + column mapping): it auto-loads that file (Excel or
/// CSV), shows the records, and lets the operator pick a row / print a range / print all — or load a
/// different file for today's run. Templates with no data file fall back to manual field entry.
/// No editing, no Save, so master templates can't be corrupted from here.
/// </summary>
public class PrintStationViewModel : ViewModelBase
{
    private readonly TemplateService _templateService;
    private string _search = "";
    private TemplateListItem? _selectedTemplate;
    private LabelTemplate? _template;
    private BitmapSource? _preview;
    private string _selectedPrinter = "";
    private int _qty = 1;
    private string _status = "Select a template to begin.";

    // Data-driven state
    private List<ExcelRow>? _rows;
    private string? _dataPath;
    private int _selectedRowIndex = -1;
    private int _rangeStart = 1;
    private int _rangeEnd = 1;

    public ObservableCollection<TemplateListItem> AllTemplates { get; } = new();
    public ObservableCollection<TemplateListItem> Templates { get; } = new();   // filtered view
    public ObservableCollection<FieldInputViewModel> Inputs { get; } = new();
    public ObservableCollection<RowItem> Rows { get; } = new();
    public ObservableCollection<PrintHistoryEntry> History { get; } = new();
    public List<string> AvailablePrinters { get; } = new();

    public string SearchText { get => _search; set { if (Set(ref _search, value)) ApplyFilter(); } }

    public TemplateListItem? SelectedTemplate
    {
        get => _selectedTemplate;
        set { if (Set(ref _selectedTemplate, value)) LoadSelected(); }
    }

    public BitmapSource? Preview { get => _preview; private set => Set(ref _preview, value); }
    public string SelectedPrinter { get => _selectedPrinter; set => Set(ref _selectedPrinter, value); }
    public int Qty { get => _qty; set => Set(ref _qty, Math.Max(1, value)); }
    public string Status { get => _status; set => Set(ref _status, value); }

    public bool HasTemplate => _template != null;

    // ── Data-driven view state ──
    public bool HasData => (_rows?.Count ?? 0) > 0;
    public bool ManualEntry => _template != null && !HasData;
    public int RowCount => _rows?.Count ?? 0;
    public string DataSummary => HasData
        ? $"{RowCount} record(s) — {System.IO.Path.GetFileName(_dataPath)}"
        : "No data file (manual entry)";

    public int SelectedRowIndex
    {
        get => _selectedRowIndex;
        set { if (Set(ref _selectedRowIndex, value)) RefreshPreview(); }
    }

    public int RangeStart { get => _rangeStart; set => Set(ref _rangeStart, Math.Max(1, value)); }
    public int RangeEnd { get => _rangeEnd; set => Set(ref _rangeEnd, Math.Max(1, value)); }

    public ICommand PrintCommand        => new RelayCommand(PrintNow, () => _template != null);           // manual mode
    public ICommand PrintSelectedCommand => new RelayCommand(PrintSelected, () => HasData && _selectedRowIndex >= 0);
    public ICommand PrintAllCommand      => new RelayCommand(PrintAll, () => HasData);
    public ICommand TestPrintCommand     => new RelayCommand(TestPrint, () => _template != null);
    public ICommand LoadDataFileCommand  => new RelayCommand(LoadDataFile, () => _template != null);
    public ICommand ClearDataCommand     => new RelayCommand(ClearData, () => HasData);
    public ICommand RefreshCommand       => new RelayCommand(LoadTemplates);
    public ICommand ReprintCommand       => new RelayCommand<PrintHistoryEntry>(Reprint);
    public ICommand ExportHistoryCommand => new RelayCommand(ExportHistory);
    public ICommand ExportZplCommand     => new RelayCommand(ExportZpl, () => _template != null);

    public PrintStationViewModel()
    {
        _templateService = new TemplateService(AppConfig.TemplatesDir);
        foreach (string p in PrinterSettings.InstalledPrinters) AvailablePrinters.Add(p);
        LoadTemplates();
        LoadHistory();
    }

    private void LoadTemplates()
    {
        AllTemplates.Clear();
        foreach (var path in _templateService.GetTemplatePaths())
        {
            var t = _templateService.Load(path);
            if (t != null) AllTemplates.Add(new TemplateListItem(t.Name, path));
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Templates.Clear();
        foreach (var t in AllTemplates)
            if (string.IsNullOrWhiteSpace(_search) ||
                t.Name.Contains(_search, StringComparison.OrdinalIgnoreCase))
                Templates.Add(t);
    }

    private void LoadSelected()
    {
        Inputs.Clear();
        Rows.Clear();
        _rows = null; _dataPath = null; _selectedRowIndex = -1;
        _template = _selectedTemplate != null ? _templateService.Load(_selectedTemplate.FilePath) : null;
        OnPropertyChanged(nameof(HasTemplate));

        if (_template == null) { Preview = null; NotifyDataState(); return; }

        // Default printer: template profile → a Zebra → first installed.
        SelectedPrinter =
            (_template.PrinterProfile.PrinterName is string pn && AvailablePrinters.Contains(pn) ? pn : null)
            ?? AvailablePrinters.FirstOrDefault(p => p.Contains("Zebra", StringComparison.OrdinalIgnoreCase))
            ?? AvailablePrinters.FirstOrDefault() ?? "";

        // Manual-entry inputs (fallback when there's no data file): one per declared field, skipping
        // computed data-source fields. A dropdown appears when the field has AllowedValues.
        var computed = new HashSet<string>(_template.DataSources.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var f in _template.FieldDefinitions)
        {
            if (string.IsNullOrWhiteSpace(f.Name) || computed.Contains(f.Name)) continue;
            var def = f.DefaultValue ?? (_template.TestData.TryGetValue(f.Name, out var v) ? v : "");
            var input = new FieldInputViewModel(f.Name,
                string.IsNullOrWhiteSpace(f.Prompt) ? f.Name : f.Prompt!, def, f.Required, f.AllowedValues);
            input.ValueChanged += RefreshPreview;
            Inputs.Add(input);
        }

        // Honor the template's PRESET data connection: auto-load its data file if present.
        if (!string.IsNullOrWhiteSpace(_template.DefaultExcelPath) && File.Exists(_template.DefaultExcelPath))
            LoadDataFrom(_template.DefaultExcelPath);

        NotifyDataState();
        Status = HasData ? $"Ready: {_template.Name} — {RowCount} record(s)" : $"Ready: {_template.Name}";
        RefreshPreview();
    }

    private void LoadDataFrom(string path)
    {
        if (_template == null) return;
        try
        {
            var rows = DataImporter.Load(path, _template);
            _rows = rows;
            _dataPath = path;
            _selectedRowIndex = rows.Count > 0 ? 0 : -1;
            Rows.Clear();
            for (int i = 0; i < rows.Count; i++)
                Rows.Add(new RowItem(i, RowLabel(rows[i], i), rows[i].PrintQty));
            _rangeStart = 1;
            _rangeEnd = Math.Max(1, rows.Count);
        }
        catch (Exception ex)
        {
            _rows = null; _dataPath = null; _selectedRowIndex = -1; Rows.Clear();
            LogService.Error("Print Station data load failed.", ex);
            System.Windows.MessageBox.Show($"Could not load data file:\n{ex.Message}", "Data load failed");
        }
    }

    private static string RowLabel(ExcelRow row, int i)
    {
        var first = row.Fields.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
        if (first.Length > 42) first = first[..42] + "…";
        return $"{i + 1}.  {first}";
    }

    private void LoadDataFile()
    {
        if (_template == null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Data files (*.xlsx;*.csv;*.tsv)|*.xlsx;*.csv;*.tsv|All files (*.*)|*.*",
            Title = "Load data file for this run",
            FileName = _template.DefaultExcelPath ?? ""
        };
        if (dlg.ShowDialog() != true) return;
        LoadDataFrom(dlg.FileName);
        NotifyDataState();
        RefreshPreview();
        Status = HasData ? $"Loaded {RowCount} record(s)." : "No records found in that file.";
    }

    private void ClearData()
    {
        _rows = null; _dataPath = null; _selectedRowIndex = -1; Rows.Clear();
        NotifyDataState();
        RefreshPreview();
        Status = "Switched to manual entry.";
    }

    private void NotifyDataState()
    {
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(ManualEntry));
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(DataSummary));
        OnPropertyChanged(nameof(SelectedRowIndex));
        OnPropertyChanged(nameof(RangeStart));
        OnPropertyChanged(nameof(RangeEnd));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>The fields for the CURRENT context: the selected data row, or the manual inputs.</summary>
    private Dictionary<string, string> CurrentFields()
    {
        if (HasData && _selectedRowIndex >= 0 && _selectedRowIndex < _rows!.Count)
            return new Dictionary<string, string>(_rows[_selectedRowIndex].Fields, StringComparer.OrdinalIgnoreCase);

        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in Inputs) d[i.Name] = i.Value ?? "";
        return d;
    }

    private void RefreshPreview()
    {
        if (_template == null) { Preview = null; return; }
        try { Preview = PrintService.RenderPreview(_template, CurrentFields(), dpi: _template.Dpi); }
        catch (Exception ex) { LogService.Error("Print Station preview failed.", ex); }
    }

    private string? Printer() => string.IsNullOrWhiteSpace(SelectedPrinter) ? null : SelectedPrinter;

    /// <summary>Shared print error handling so every print action reports failures the same way.</summary>
    private void RunPrint(Action act)
    {
        try { act(); LoadHistory(); }
        catch (LabelValidationException ex) { System.Windows.MessageBox.Show(ex.Message, "Cannot print"); }
        catch (PrinterNotFoundException ex) { System.Windows.MessageBox.Show(ex.Message, "Printer not found"); }
        catch (PrinterOfflineException ex)  { System.Windows.MessageBox.Show(ex.Message, "Printer not ready"); }
        catch (SerialStoreUnavailableException ex) { System.Windows.MessageBox.Show(ex.Message, "Serial store unavailable"); }
        catch (Exception ex)
        {
            LogService.Error("Print Station print failed.", ex);
            System.Windows.MessageBox.Show(ex.Message, "Print failed");
        }
    }

    private void PrintNow() // manual-entry mode
    {
        if (_template == null) return;

        var missing = Inputs.Where(i => i.Required && string.IsNullOrWhiteSpace(i.Value))
                            .Select(i => i.Prompt).ToList();
        if (missing.Count > 0)
        {
            System.Windows.MessageBox.Show(
                $"Please fill the required field(s): {string.Join(", ", missing)}.", "Missing data");
            return;
        }

        RunPrint(() =>
        {
            PrintService.Print(_template!, CurrentFields(), Printer(), Qty,
                allowFallbackPrinter: false, source: "PrintStation");
            Status = $"Printed {Qty} × '{_template!.Name}' at {DateTime.Now:HH:mm:ss}.";
        });
    }

    private void PrintSelected()
    {
        if (_template == null || !HasData || _selectedRowIndex < 0) return;
        RunPrint(() =>
        {
            var row = _rows![_selectedRowIndex];
            PrintService.Print(_template!, row.Fields, Printer(), Qty, allowFallbackPrinter: false, source: "PrintStation");
            Status = $"Printed {Qty} × record {_selectedRowIndex + 1} at {DateTime.Now:HH:mm:ss}.";
        });
    }

    private void PrintAll()
    {
        if (_template == null || !HasData) return;
        RunPrint(() =>
        {
            int lo = Math.Max(1, Math.Min(RangeStart, RangeEnd));
            int hi = Math.Min(RowCount, Math.Max(RangeStart, RangeEnd));
            var rows = new List<ExcelRow>();
            for (int i = lo - 1; i <= hi - 1 && i < _rows!.Count; i++)
                if (_rows[i].PrintQty > 0) rows.Add(_rows[i]);   // honour 0/blank PrintQty as "skip"

            if (rows.Count == 0) { Status = $"Nothing to print in records {lo}–{hi} (all PrintQty = 0)."; return; }

            // All-or-nothing: validate the whole range before printing any label.
            var errs = PrintService.ValidateBatch(_template!, rows.Select(r => r.Fields).ToList());
            if (errs.Count > 0) throw new LabelValidationException(errs);

            int total = 0;
            foreach (var row in rows)
            {
                PrintService.Print(_template!, row.Fields, Printer(), copies: row.PrintQty,
                    allowFallbackPrinter: false, validate: false, source: "PrintStation");
                total += row.PrintQty;
            }
            Status = $"Printed {total} label(s) from records {lo}–{hi} at {DateTime.Now:HH:mm:ss}.";
        });
    }

    private void TestPrint()
    {
        if (_template == null) return;
        RunPrint(() =>
        {
            PrintService.PrintTest(_template!, CurrentFields(), Printer());
            Status = $"Test label printed (no serial consumed) at {DateTime.Now:HH:mm:ss}.";
        });
    }

    private void LoadHistory()
    {
        History.Clear();
        foreach (var e in PrintHistoryService.Recent(100)) History.Add(e);
    }

    private void Reprint(PrintHistoryEntry? entry)
    {
        if (entry == null) return;
        var t = FindById(entry.TemplateId);
        if (t == null) { System.Windows.MessageBox.Show("Original template no longer exists.", "Reprint"); return; }
        RunPrint(() =>
        {
            int n = PrintService.Reprint(t, entry);   // exact original IDs, no counter advance
            Status = $"Reprinted {n} × '{entry.TemplateName}' (original IDs) at {DateTime.Now:HH:mm:ss}.";
        });
    }

    private void ExportHistory()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"label-history-{DateTime.Now:yyyyMMdd}.csv"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            int n = PrintHistoryService.ExportCsv(dlg.FileName);
            Status = $"Exported {n} history rows to {dlg.FileName}.";
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Export failed"); }
    }

    private void ExportZpl()
    {
        if (_template == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "ZPL (*.zpl)|*.zpl|Text (*.txt)|*.txt",
            FileName = $"{_template.Name}.zpl"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var zpl = PrintService.RenderZpl(_template, CurrentFields());
            File.WriteAllText(dlg.FileName, zpl);
            Status = $"Exported ZPL to {dlg.FileName} ({zpl.Length} chars).";
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "ZPL export failed"); }
    }

    private LabelTemplate? FindById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var path in _templateService.GetTemplatePaths())
        {
            var t = _templateService.Load(path);
            if (t != null && t.Id.ToString() == id) return t;
        }
        return null;
    }
}

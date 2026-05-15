using LabelDesigner.Core.Models;
using LabelDesigner.Services;
using System.Drawing.Printing;
using System.Windows.Input;

namespace LabelDesigner.ViewModels;

public class PrintPreviewViewModel : ViewModelBase
{
    private readonly LabelTemplate _template;
    private string _selectedPrinter;
    private bool _useDataQty;
    private int _manualQty = 1;

    public Dictionary<string, string> Fields { get; }
    public LabelTemplate Template => _template;
    public string Title => $"Print Preview — {_template.Name}";

    // ─── Printer selection ───────────────────────────────────────────────────
    public List<string> AvailablePrinters { get; }

    public string SelectedPrinter
    {
        get => _selectedPrinter;
        set => Set(ref _selectedPrinter, value);
    }

    // ─── Quantity ────────────────────────────────────────────────────────────
    /// <summary>Quantity supplied by the data source (LabelJob.Quantity). 0 means no data source.</summary>
    public int DataQty { get; }

    public bool HasDataQty => DataQty > 0;

    public bool UseDataQty
    {
        get => _useDataQty;
        set
        {
            Set(ref _useDataQty, value);
            OnPropertyChanged(nameof(UseManualQty));
            OnPropertyChanged(nameof(EffectiveQty));
        }
    }

    public bool UseManualQty
    {
        get => !_useDataQty;
        set => UseDataQty = !value;
    }

    public int ManualQty
    {
        get => _manualQty;
        set { Set(ref _manualQty, Math.Max(1, value)); OnPropertyChanged(nameof(EffectiveQty)); }
    }

    public int EffectiveQty => UseDataQty ? DataQty : ManualQty;

    // ─── Commands ────────────────────────────────────────────────────────────
    public ICommand PrintCommand => new RelayCommand(PrintDirect);
    public ICommand CloseCommand => new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));

    public event EventHandler? CloseRequested;

    /// <param name="template">Template to preview.</param>
    /// <param name="fieldsOverride">
    ///   Live field data (from Excel/IPC). When null the template's TestData is used,
    ///   falling back to empty strings for unmapped fields.
    /// </param>
    /// <param name="dataQty">Quantity from the data source (0 = no data source).</param>
    /// <param name="printerName">Pre-selected printer name, or null for system default.</param>
    /// <param name="autoprint">If true, print immediately without showing the window.</param>
    public PrintPreviewViewModel(LabelTemplate template,
        Dictionary<string, string>? fieldsOverride,
        int dataQty = 0,
        string? printerName = null,
        bool autoprint = false)
    {
        _template = template;
        Fields    = fieldsOverride ?? BuildTestFields(template);
        DataQty   = dataQty;

        // Default UseDataQty on when data was supplied
        _useDataQty = dataQty > 0;
        _manualQty  = 1;

        // Populate printer list
        AvailablePrinters = new List<string>();
        foreach (string p in PrinterSettings.InstalledPrinters)
            AvailablePrinters.Add(p);

        // Determine default selected printer
        if (!string.IsNullOrWhiteSpace(printerName) && AvailablePrinters.Contains(printerName))
            _selectedPrinter = printerName;
        else
        {
            _selectedPrinter = AvailablePrinters.FirstOrDefault(p =>
                p.Contains("Zebra", StringComparison.OrdinalIgnoreCase))
                ?? AvailablePrinters.FirstOrDefault()
                ?? "";
        }

        if (autoprint) PrintDirect();
    }

    public void PrintDirect()
    {
        var printer = string.IsNullOrWhiteSpace(SelectedPrinter) ? null : SelectedPrinter;
        PrintService.Print(_template, Fields, printer, EffectiveQty);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Builds the field dictionary used when no live data is available.
    /// Uses TestData from the template; falls back to empty string for unmapped fields.
    /// </summary>
    private static Dictionary<string, string> BuildTestFields(LabelTemplate t)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in t.Fields)
        {
            dict[field] = t.TestData.TryGetValue(field, out var v) ? v : "";
        }
        return dict;
    }
}

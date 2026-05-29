using LabelDesigner.Core.Models;
using LabelDesigner.Helpers;
using LabelDesigner.Services;
using System.Drawing.Printing;
using System.Windows.Input;

namespace LabelDesigner.ViewModels;

public class PrintPreviewViewModel : ViewModelBase
{
    private readonly LabelTemplate _template;
    private readonly bool _hasDataFields;
    private readonly IReadOnlyList<ExcelRow>? _allRows;
    private string _selectedPrinter;
    private bool _useDataQty;
    private bool _printAllRecords;
    private int _manualQty = 1;
    private double _previewZoom = 1.0;

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
    /// <summary>Quantity from the data source. Valid whenever HasDataQty is true (including 0).</summary>
    public int DataQty { get; }

    /// <summary>True when live data was loaded (data qty is valid, even if 0).</summary>
    public bool HasDataQty => _hasDataFields;

    public bool UseDataQty
    {
        get => _useDataQty;
        set
        {
            Set(ref _useDataQty, value);
            OnPropertyChanged(nameof(UseManualQty));
            OnPropertyChanged(nameof(EffectiveQty));
            OnPropertyChanged(nameof(PrintButtonLabel));
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
        set
        {
            Set(ref _manualQty, Math.Max(0, value));
            OnPropertyChanged(nameof(EffectiveQty));
            OnPropertyChanged(nameof(PrintButtonLabel));
        }
    }

    public int EffectiveQty => UseDataQty ? DataQty : ManualQty;

    // ─── Print-all-records mode ──────────────────────────────────────────────
    /// <summary>True when this preview was given the full row set and can print all records.</summary>
    public bool HasAllRecords => _allRows != null && _allRows.Count > 0;

    /// <summary>Total record count for "Print all records" label.</summary>
    public int AllRecordsCount => _allRows?.Count ?? 0;

    /// <summary>Sum of per-row PrintQty (with 0 meaning "skip"), shown next to the toggle.</summary>
    public int AllRecordsTotalLabels =>
        _allRows == null ? 0 : _allRows.Sum(r => Math.Max(0, r.PrintQty));

    /// <summary>When true, PrintCommand prints every loaded record using each row's PrintQty.</summary>
    public bool PrintAllRecords
    {
        get => _printAllRecords;
        set
        {
            Set(ref _printAllRecords, value);
            OnPropertyChanged(nameof(PrintButtonLabel));
        }
    }

    /// <summary>Label shown on the big print button — switches between "Print ×N" and "Print N records".</summary>
    public string PrintButtonLabel =>
        _printAllRecords
            ? $"Print {AllRecordsTotalLabels} label(s) — {AllRecordsCount} record(s)"
            : $"Print ×{EffectiveQty}";

    // ─── Preview zoom ─────────────────────────────────────────────────────────
    public double PreviewZoom
    {
        get => _previewZoom;
        set { Set(ref _previewZoom, Math.Clamp(Math.Round(value, 2), 0.25, 4.0)); OnPropertyChanged(nameof(PreviewZoomLabel)); }
    }

    public string PreviewZoomLabel => $"{(int)(_previewZoom * 100)}%";

    public ICommand ZoomInCommand    => new RelayCommand(() => PreviewZoom *= 1.25, () => _previewZoom < 4.0);
    public ICommand ZoomOutCommand   => new RelayCommand(() => PreviewZoom /= 1.25, () => _previewZoom > 0.26);
    public ICommand ZoomResetCommand => new RelayCommand(() => PreviewZoom = 1.0);

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
    /// <param name="allRows">
    ///   Full row set for "Print all records" mode. When supplied with &gt;0 rows the
    ///   preview window shows an "all records" toggle and each row's PrintQty is honoured.
    /// </param>
    public PrintPreviewViewModel(LabelTemplate template,
        Dictionary<string, string>? fieldsOverride,
        int dataQty = 0,
        string? printerName = null,
        bool autoprint = false,
        IReadOnlyList<ExcelRow>? allRows = null)
    {
        _template       = template;
        Fields          = fieldsOverride ?? BuildTestFields(template);
        DataQty         = dataQty;
        _hasDataFields  = fieldsOverride != null;
        _allRows        = allRows;
        // Default to "Print all records" mode whenever rows were supplied — that's the common case.
        _printAllRecords = allRows != null && allRows.Count > 0;

        // Default UseDataQty on whenever live field data was supplied (even when qty = 0)
        _useDataQty = _hasDataFields;
        _manualQty  = _hasDataFields ? 0 : 1;

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

        try
        {
            if (_printAllRecords && _allRows != null && _allRows.Count > 0)
            {
                var rows = _allRows.Where(r => r.PrintQty > 0).ToList(); // honour 0/blank PrintQty as "skip"

                // All-or-nothing: validate the WHOLE batch before printing ANY label, so a bad row
                // can't abort a half-printed run with serials already burned. Errors name the row.
                var batchErrors = PrintService.ValidateBatch(_template, rows.Select(r => r.Fields).ToList());
                if (batchErrors.Count > 0) throw new LabelValidationException(batchErrors);

                foreach (var row in rows)
                    PrintService.Print(_template, row.Fields, printer, copies: row.PrintQty,
                        allowFallbackPrinter: false, validate: false);
            }
            else
            {
                PrintService.Print(_template, Fields, printer, EffectiveQty, allowFallbackPrinter: false);
            }
        }
        catch (LabelValidationException ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Label Designer — label not printable");
            return; // keep the window open so the operator can fix the data
        }
        catch (PrinterNotFoundException ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Label Designer — printer not found");
            return;
        }
        catch (PrinterOfflineException ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Label Designer — printer not ready");
            return;
        }
        catch (SerialStoreUnavailableException ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Label Designer — serial store unavailable");
            return;
        }

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

using LabelDesigner.Core.Models;
using LabelDesigner.Helpers;
using LabelDesigner.Services;
using System.Drawing.Printing;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace LabelDesigner.ViewModels;

/// <summary>One row of the print-preview job list: thumbnail on the left, quantity on the right.</summary>
public class JobPreviewItem : ViewModelBase
{
    private BitmapSource? _thumbnail;

    /// <summary>Position within the JOB (rows that will actually print), 0-based.</summary>
    public int Index { get; init; }
    public int Qty { get; init; }
    public string Caption => $"{Index + 1}.";
    public string QtyText => $"×{Qty}";
    public BitmapSource? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }
}

public class PrintPreviewViewModel : ViewModelBase
{
    /// <summary>Render DPI for the preview bitmap — 2× screen so barcodes and text stay sharp.</summary>
    private const double PreviewDpi = 192;

    private readonly LabelTemplate _template;
    private readonly bool _hasDataFields;
    private readonly IReadOnlyList<ExcelRow>? _allRows;
    /// <summary>The rows that will ACTUALLY print in all-records mode (PrintQty &gt; 0).</summary>
    private readonly List<ExcelRow> _jobRows = new();
    private int _previewIndex;
    private BitmapSource? _previewImage;
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
            if (Set(ref _printAllRecords, value))
            {
                _previewIndex = 0;
                RefreshPreview();   // switch the preview between the JOB's labels and this record
            }
            OnPropertyChanged(nameof(PrintButtonLabel));
        }
    }

    // ─── Job preview (what will actually print) ──────────────────────────────
    /// <summary>The rendered label the operator is looking at — in all-records mode this walks the
    /// rows that will actually print (PrintQty &gt; 0), not whatever row the designer happened to show.</summary>
    public BitmapSource? PreviewImage { get => _previewImage; private set => Set(ref _previewImage, value); }

    public string PreviewInfo
    {
        get
        {
            if (!_printAllRecords || _allRows == null) return "This record";
            if (_jobRows.Count == 0) return "Nothing to print — every row has quantity 0";
            return $"Record {_previewIndex + 1} of {_jobRows.Count} in this job  (×{_jobRows[_previewIndex].PrintQty})";
        }
    }

    /// <summary>True when the preview can step through multiple job records.</summary>
    public bool CanNavigatePreview => _printAllRecords && _jobRows.Count > 1;

    /// <summary>The job list shown left of the preview — one entry per record that will print,
    /// with a thumbnail and its quantity. Clicking an entry shows it in the big preview.</summary>
    public System.Collections.ObjectModel.ObservableCollection<JobPreviewItem> JobItems { get; } = new();

    /// <summary>Show the job list only when printing all records (it IS the job).</summary>
    public bool ShowJobList => _printAllRecords && JobItems.Count > 0;

    private bool _syncingSelection;
    private JobPreviewItem? _selectedJobItem;
    public JobPreviewItem? SelectedJobItem
    {
        get => _selectedJobItem;
        set
        {
            if (!Set(ref _selectedJobItem, value) || _syncingSelection || value == null) return;
            _previewIndex = value.Index;
            RefreshPreview();
        }
    }

    /// <summary>Renders the job thumbnails one per dispatcher pass so the window opens instantly
    /// even for large jobs. Runs on the UI thread (label rendering needs the STA).</summary>
    private void RenderThumbnailsLazily()
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        int i = 0;
        void Step()
        {
            if (i >= JobItems.Count) return;
            var item = JobItems[i];
            try { item.Thumbnail = PrintService.RenderPreview(_template, _jobRows[item.Index].Fields, dpi: 48); }
            catch (Exception ex) { LogService.Warn($"Preview thumbnail {item.Index + 1} failed: {ex.Message}"); }
            i++;
            dispatcher.BeginInvoke(Step, System.Windows.Threading.DispatcherPriority.Background);
        }
        dispatcher.BeginInvoke(Step, System.Windows.Threading.DispatcherPriority.Background);
    }

    public ICommand PrevLabelCommand => new RelayCommand(
        () => { _previewIndex--; RefreshPreview(); },
        () => CanNavigatePreview && _previewIndex > 0);

    public ICommand NextLabelCommand => new RelayCommand(
        () => { _previewIndex++; RefreshPreview(); },
        () => CanNavigatePreview && _previewIndex < _jobRows.Count - 1);

    private void RefreshPreview()
    {
        try
        {
            var fields = _printAllRecords && _jobRows.Count > 0
                ? _jobRows[_previewIndex].Fields
                : Fields;
            PreviewImage = PrintService.RenderPreview(_template, fields, PreviewDpi);
        }
        catch (Exception ex)
        {
            // A render hiccup must not kill the preview window — the operator can still print/cancel.
            LogService.Error("Print preview render failed.", ex);
        }

        // Keep the job list's highlight in step with the big preview (arrows or list clicks alike).
        _syncingSelection = true;
        try
        {
            _selectedJobItem = _printAllRecords && _previewIndex < JobItems.Count ? JobItems[_previewIndex] : null;
            OnPropertyChanged(nameof(SelectedJobItem));
        }
        finally { _syncingSelection = false; }

        OnPropertyChanged(nameof(PreviewInfo));
        OnPropertyChanged(nameof(CanNavigatePreview));
        OnPropertyChanged(nameof(ShowJobList));
        CommandManager.InvalidateRequerySuggested();
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
        if (allRows != null) _jobRows.AddRange(allRows.Where(r => r.PrintQty > 0));
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

        for (int i = 0; i < _jobRows.Count; i++)
            JobItems.Add(new JobPreviewItem { Index = i, Qty = _jobRows[i].PrintQty });

        RefreshPreview();
        if (JobItems.Count > 0) RenderThumbnailsLazily();

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

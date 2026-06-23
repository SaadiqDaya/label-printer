using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Services;
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace LabelDesigner.ViewModels;

/// <summary>One operator-entered field on the Print Station form (manual-entry mode).
/// The control shown is chosen by precedence: allowed-values → dropdown, else the field's
/// <see cref="FieldDataType"/> → date picker / numeric box / plain text. Print-time formatting
/// and validation still apply (see PrintService.ApplyFieldDefs / FieldValidator); typed inputs
/// just stop bad data from being entered in the first place.</summary>
public class FieldInputViewModel : ViewModelBase
{
    public string Name { get; }
    public string Prompt { get; }
    public bool Required { get; }
    public string PromptLabel => Required ? Prompt + " *" : Prompt;
    public FieldDataType DataType { get; }

    /// <summary>Allowed values → drives a dropdown when present (it wins over DataType).</summary>
    public IReadOnlyList<string> AllowedValues { get; }
    public bool HasChoices => AllowedValues.Count > 0;

    // Mutually-exclusive control selectors (exactly one is true). Dropdown wins; then the typed
    // controls; otherwise a plain text box (covers Text and Barcode types).
    public bool IsDate   => !HasChoices && DataType == FieldDataType.Date;
    public bool IsNumber => !HasChoices && DataType == FieldDataType.Number;
    public bool IsText   => !HasChoices && !IsDate && !IsNumber;

    private string _value;
    public string Value
    {
        get => _value;
        set { if (Set(ref _value, value)) { OnPropertyChanged(nameof(DateValue)); ValueChanged?.Invoke(); } }
    }
    public event Action? ValueChanged;

    /// <summary>DatePicker binding (round-trips with <see cref="Value"/>). Emits the culture short-date
    /// string, which PrintService.ApplyFormat re-parses and reformats with the field's Format.</summary>
    public DateTime? DateValue
    {
        get => DateTime.TryParse(_value, out var d) ? d : null;
        set => Value = value.HasValue ? value.Value.ToString("d", CultureInfo.CurrentCulture) : "";
    }

    public FieldInputViewModel(string name, string prompt, string value, bool required,
                               IReadOnlyList<string> allowedValues, FieldDataType dataType)
    {
        Name = name; Prompt = prompt; _value = value; Required = required;
        AllowedValues = allowedValues; DataType = dataType;
    }
}

/// <summary>A data-file record in the Print Station's record list: tick to include, override the
/// quantity per row ("(was N)" shows when it differs from the file's PrintQty).</summary>
public class RowItem : ViewModelBase
{
    public int Index { get; }     // 0-based
    public string Label { get; }
    /// <summary>The PrintQty the data file asked for (0 = the file said skip).</summary>
    public int OriginalQty { get; }

    private bool _include;
    public bool Include { get => _include; set => Set(ref _include, value); }

    private int _qty;
    public int Qty
    {
        get => _qty;
        set { if (Set(ref _qty, Math.Max(0, value))) OnPropertyChanged(nameof(WasText)); }
    }

    public string WasText => Qty != OriginalQty ? $"(was {OriginalQty})" : "";

    public RowItem(int index, string label, int printQty)
    {
        Index = index; Label = label; OriginalQty = printQty;
        _qty = Math.Max(1, printQty);      // a 0-qty row gets a sensible value if the operator opts it back in
        _include = printQty > 0;           // the file's PrintQty=0 convention pre-unticks the row
    }
}

/// <summary>One row of a watch-folder job, selectable with a quantity override.</summary>
public class JobRowViewModel : ViewModelBase
{
    public PrintJobRow Row { get; }
    public JobGroupViewModel Group { get; }
    public string Label { get; }
    public int OriginalQty { get; }
    public string? Error => Row.Error;
    public bool HasError => !string.IsNullOrEmpty(Row.Error);

    private bool _include;
    public bool Include { get => _include; set => Set(ref _include, value); }

    private int _qty;
    public int Qty
    {
        get => _qty;
        set { if (Set(ref _qty, Math.Max(0, value))) OnPropertyChanged(nameof(WasText)); }
    }

    public string WasText => Qty != OriginalQty ? $"(was {OriginalQty})" : "";

    public JobRowViewModel(PrintJobRow row, JobGroupViewModel group)
    {
        Row = row; Group = group;
        OriginalQty = row.Qty;
        _qty = Math.Max(1, row.Qty);
        _include = row.Qty > 0;
        var first = row.Fields.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) ) ?? "";
        if (first.Length > 38) first = first[..38] + "…";
        Label = $"{row.RowNumber}.  {first}";
    }
}

/// <summary>A job's rows that print with one template (and that template's routed printer).</summary>
public class JobGroupViewModel
{
    public PrintJobGroup Group { get; }
    public string TemplateName => Group.Template.Name;
    public string PrinterText { get; }
    public ObservableCollection<JobRowViewModel> Rows { get; } = new();

    public JobGroupViewModel(PrintJobGroup group, string? stationPrinter)
    {
        Group = group;
        var p = string.IsNullOrWhiteSpace(group.Template.PrinterProfile.PrinterName)
            ? stationPrinter : group.Template.PrinterProfile.PrinterName;
        PrinterText = string.IsNullOrWhiteSpace(p) ? "station printer" : p!;
        foreach (var row in group.Rows) Rows.Add(new JobRowViewModel(row, this));
    }
}

/// <summary>A watch-folder job waiting for the operator.</summary>
public class JobViewModel : ViewModelBase
{
    public WatchJob Job { get; }
    public string FileName => Job.OriginalName;
    public string FolderName { get; }
    public string Info { get; }
    public ObservableCollection<JobGroupViewModel> Groups { get; } = new();
    /// <summary>Rows the parser could not route to any template — shown, never printed.</summary>
    public ObservableCollection<string> Problems { get; } = new();
    public bool HasProblems => Problems.Count > 0;

    public JobViewModel(WatchJob job, string? stationPrinter)
    {
        Job = job;
        FolderName = Path.GetFileName(job.Folder.Path.TrimEnd('\\', '/'));
        Info = $"{job.ReceivedAt:HH:mm:ss} — {job.Parsed.TotalRows} row(s), {job.Parsed.Groups.Count} template group(s)";
        foreach (var g in job.Parsed.Groups) Groups.Add(new JobGroupViewModel(g, stationPrinter));
        foreach (var r in job.Parsed.UnroutedRows) Problems.Add($"Row {r.RowNumber}: {r.Error}");
    }
}

/// <summary>
/// Shop-floor Print Station: a read-only operator surface. It HONORS the data connection the
/// template already carries (DefaultExcelPath + column mapping): it auto-loads that file (Excel or
/// CSV), shows the records, and lets the operator tick rows / override quantities / print — or load
/// a different file for today's run. Templates with no data file fall back to manual field entry.
/// It also runs the WATCH-FOLDER queue: job CSVs dropped by any ERP appear here for release
/// (or auto-print, per folder). No editing, no Save, so master templates can't be corrupted.
/// </summary>
public class PrintStationViewModel : ViewModelBase, IDisposable
{
    private readonly TemplateService _templateService;
    private readonly WatchFolderService _watchService;
    private readonly HttpPrintService? _httpService;
    private string _search = "";
    private TemplateListItem? _selectedTemplate;
    private LabelTemplate? _template;
    private BitmapSource? _preview;
    private string _selectedPrinter = "";
    private int _qty = 1;
    private string _status = "Select a template to begin.";
    private string _operatorName;
    private bool _skipInvalidRows;

    // Data-driven state
    private List<ExcelRow>? _rows;
    private string? _dataPath;
    private int _selectedRowIndex = -1;
    private int _rangeStart = 1;
    private int _rangeEnd = 1;

    // Job-queue state
    private JobViewModel? _selectedJob;
    private JobRowViewModel? _jobPreviewRow;
    private int _jobSheetIndex;
    private readonly List<JobRowViewModel> _subscribedJobRows = new();

    public ObservableCollection<TemplateListItem> AllTemplates { get; } = new();
    public ObservableCollection<TemplateListItem> Templates { get; } = new();   // filtered view
    public ObservableCollection<FieldInputViewModel> Inputs { get; } = new();
    public ObservableCollection<RowItem> Rows { get; } = new();
    public ObservableCollection<JobViewModel> Jobs { get; } = new();
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

    /// <summary>Stamped on every print-history entry (audit "who"). Persisted per station.</summary>
    public string OperatorName
    {
        get => _operatorName;
        set
        {
            if (Set(ref _operatorName, value))
            {
                var d = UserSettings.Current;
                d.OperatorName = value;
                UserSettings.Save(d);
            }
        }
    }

    /// <summary>Batch mode for "Print all in range": ON = print the valid rows and report the bad
    /// ones; OFF = any bad row blocks the whole batch (all-or-nothing).</summary>
    public bool SkipInvalidRows { get => _skipInvalidRows; set => Set(ref _skipInvalidRows, value); }

    public bool HasTemplate => _template != null;

    // ── Data-driven view state ──
    public bool HasData => (_rows?.Count ?? 0) > 0;
    public bool ManualEntry => _template != null && !HasData;
    public int RowCount => _rows?.Count ?? 0;
    public string DataSummary => HasData
        ? $"{RowCount} record(s) — {System.IO.Path.GetFileName(_dataPath)}"
        : "No data file (manual entry)";

    // ── Job-queue view state ──
    public bool JobMode => _selectedJob != null;
    public bool ShowData => HasData && !JobMode;
    public bool ShowManual => ManualEntry && !JobMode;
    public bool ShowTemplateActions => !JobMode;
    public string JobsHeader => $"Jobs ({Jobs.Count})";

    public JobViewModel? SelectedJob
    {
        get => _selectedJob;
        set
        {
            if (Set(ref _selectedJob, value))
            {
                SubscribeJobRows(value);   // live-update the sheet preview as rows are ticked
                _jobSheetIndex = 0;
                NotifyJobState();
                if (value != null)
                {
                    if (JobIsSheet)
                    {
                        RefreshJobSheetPreview();   // show the composed sheets, not a single label
                        Status = $"Job '{value.FileName}' — sheet preview. Review rows, then Print job.";
                    }
                    else
                    {
                        // Default the preview to the job's first row.
                        JobPreviewRow = value.Groups.FirstOrDefault()?.Rows.FirstOrDefault();
                        Status = $"Job '{value.FileName}' — review rows, then Print job.";
                    }
                }
                else RefreshPreview();
            }
        }
    }

    /// <summary>The job row currently being previewed (clicking any row in any group sets it).
    /// For sheet jobs this jumps the sheet preview to the page that row lands on.</summary>
    public JobRowViewModel? JobPreviewRow
    {
        get => _jobPreviewRow;
        set
        {
            if (Set(ref _jobPreviewRow, value) && value != null)
            {
                if (JobIsSheet) JumpToRowSheet(value);
                else RefreshJobPreview(value);
            }
        }
    }

    // ── Sheet-job preview (the selected job's templates have a page layout) ──

    /// <summary>True when the selected job prints on SHEETS (Avery/menus) — the preview then shows
    /// composed pages with ◄ ► navigation instead of one label at a time.</summary>
    public bool JobIsSheet =>
        _selectedJob is { Groups.Count: > 0 } job &&
        job.Groups[0].Group.Template.Page != null &&
        job.Groups.All(g => g.Group.Template.PrinterProfile.OutputMode == PrintBackend.Gdi);

    public bool ShowJobSheetNav => JobMode && JobIsSheet;

    public string JobSheetInfo
    {
        get
        {
            if (!JobIsSheet) return "";
            int count = JobSheetCount;
            return count == 0 ? "No rows ticked" : $"Sheet {_jobSheetIndex + 1} of {count}";
        }
    }

    public ICommand PrevJobSheetCommand => new RelayCommand(
        () => { _jobSheetIndex--; RefreshJobSheetPreview(); },
        () => JobIsSheet && _jobSheetIndex > 0);

    public ICommand NextJobSheetCommand => new RelayCommand(
        () => { _jobSheetIndex++; RefreshJobSheetPreview(); },
        () => JobIsSheet && _jobSheetIndex < JobSheetCount - 1);

    /// <summary>The ticked, non-zero job rows in file order, paired with their routed template.</summary>
    private List<(JobGroupViewModel Group, JobRowViewModel Row)> OrderedIncludedRows() =>
        _selectedJob == null
            ? new()
            : _selectedJob.Groups
                .SelectMany(g => g.Rows.Select(r => (Group: g, Row: r)))
                .Where(x => x.Row.Include && x.Row.Qty > 0)
                .OrderBy(x => x.Row.Row.RowNumber)
                .ToList();

    /// <summary>Every label that will print, expanded by quantity, in sheet-cell order.</summary>
    private List<(LabelTemplate Template, Dictionary<string, string> Fields)> IncludedJobCells()
    {
        var cells = new List<(LabelTemplate, Dictionary<string, string>)>();
        foreach (var (g, r) in OrderedIncludedRows())
            for (int i = 0; i < r.Qty; i++)
                cells.Add((g.Group.Template, r.Row.Fields));
        return cells;
    }

    private int JobSheetCount
    {
        get
        {
            if (!JobIsSheet) return 0;
            var page = _selectedJob!.Groups[0].Group.Template.Page!;
            int total = IncludedJobCells().Count;
            return total == 0 ? 0 : (int)Math.Ceiling(total / (double)page.CellsPerPage);
        }
    }

    private void RefreshJobSheetPreview()
    {
        try
        {
            if (!JobIsSheet) return;
            var sheetDef = _selectedJob!.Groups[0].Group.Template;
            var page = sheetDef.Page!;
            var cells = IncludedJobCells();
            if (cells.Count == 0) { Preview = null; }
            else
            {
                int count = (int)Math.Ceiling(cells.Count / (double)page.CellsPerPage);
                _jobSheetIndex = Math.Clamp(_jobSheetIndex, 0, count - 1);
                var pageCells = cells.Skip(_jobSheetIndex * page.CellsPerPage).Take(page.CellsPerPage).ToList();
                Preview = PrintService.RenderSheetPreview(sheetDef, pageCells, startCell: 0, dpi: 130);
            }
        }
        catch (Exception ex) { LogService.Error("Print Station job sheet preview failed.", ex); }
        OnPropertyChanged(nameof(JobSheetInfo));
        OnPropertyChanged(nameof(ShowJobSheetNav));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Pages the sheet preview to the sheet that holds the clicked row's first label.</summary>
    private void JumpToRowSheet(JobRowViewModel row)
    {
        var page = _selectedJob!.Groups[0].Group.Template.Page!;
        int pos = 0;
        foreach (var (_, r) in OrderedIncludedRows())
        {
            if (r == row) { _jobSheetIndex = pos / page.CellsPerPage; break; }
            pos += r.Qty;
        }
        RefreshJobSheetPreview();
    }

    private void SubscribeJobRows(JobViewModel? job)
    {
        foreach (var r in _subscribedJobRows) r.PropertyChanged -= OnJobRowChanged;
        _subscribedJobRows.Clear();
        if (job == null) return;
        foreach (var g in job.Groups)
            foreach (var r in g.Rows) { r.PropertyChanged += OnJobRowChanged; _subscribedJobRows.Add(r); }
    }

    private void OnJobRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!JobIsSheet) return;
        // Ticking a row in/out or changing its quantity reshapes the sheets — re-render.
        if (e.PropertyName is nameof(JobRowViewModel.Include) or nameof(JobRowViewModel.Qty))
            RefreshJobSheetPreview();
    }

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
    public ICommand PrintJobCommand      => new RelayCommand(PrintJob, () => SelectedJob != null);
    public ICommand RejectJobCommand     => new RelayCommand(RejectJob, () => SelectedJob != null);
    public ICommand CloseJobCommand      => new RelayCommand(() => SelectedJob = null, () => SelectedJob != null);
    public ICommand OpenTemplatesFolderCommand => new RelayCommand(OpenTemplatesFolder);

    /// <summary>Where this station reads its .lbl templates from (shown in the empty-list help).</summary>
    public string TemplatesDir => AppConfig.TemplatesDir;

    /// <summary>True when no .lbl files were found — drives the "where do templates come from" hint.</summary>
    public bool HasNoTemplates => AllTemplates.Count == 0;

    private void OpenTemplatesFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppConfig.TemplatesDir);
            System.Diagnostics.Process.Start("explorer.exe", AppConfig.TemplatesDir);
        }
        catch (Exception ex)
        {
            LogService.Error("Could not open templates folder.", ex);
            Status = $"Could not open templates folder: {ex.Message}";
        }
    }

    public int SelectedRowIndex
    {
        get => _selectedRowIndex;
        set { if (Set(ref _selectedRowIndex, value)) RefreshPreview(); }
    }

    public int RangeStart { get => _rangeStart; set => Set(ref _rangeStart, Math.Max(1, value)); }
    public int RangeEnd { get => _rangeEnd; set => Set(ref _rangeEnd, Math.Max(1, value)); }

    public PrintStationViewModel()
    {
        _templateService = new TemplateService(AppConfig.TemplatesDir);
        foreach (string p in PrinterSettings.InstalledPrinters) AvailablePrinters.Add(p);

        var settings = UserSettings.Current;
        _operatorName = string.IsNullOrWhiteSpace(settings.OperatorName)
            ? Environment.UserName : settings.OperatorName;

        LoadTemplates();
        LoadHistory();

        // Watch-folder queue: jobs dropped by the ERP appear in Jobs (or auto-print, per folder).
        _watchService = new WatchFolderService(System.Windows.Threading.Dispatcher.CurrentDispatcher)
        {
            FallbackPrinterProvider = GetSelectedPrinterOrNull,
            PrintedByProvider = GetOperatorName,
        };
        _watchService.JobArrived += OnJobArrived;
        _watchService.Status += OnWatchStatus;
        Jobs.CollectionChanged += OnJobsChanged;
        _watchService.Start();

        // Shim-compatible HTTP print API (opt-in via File ▸ Settings). A failed start is reported
        // loudly — an API that silently isn't there would strand the calling system.
        if (settings.HttpApiEnabled)
        {
            _httpService = new HttpPrintService(settings.HttpApiPort,
                System.Windows.Threading.Dispatcher.CurrentDispatcher)
            {
                FallbackPrinterProvider = GetSelectedPrinterOrNull,
                PrintedByProvider = GetOperatorName,
            };
            _httpService.Status += OnWatchStatus;   // same status-bar + history-refresh handling
            try { _httpService.Start(); }
            catch (Exception ex)
            {
                LogService.Error($"HTTP print API could not start on port {settings.HttpApiPort}.", ex);
                Status = $"HTTP print API FAILED to start on port {settings.HttpApiPort}: {ex.Message}";
            }
        }
    }

    public void Dispose()
    {
        SubscribeJobRows(null);   // detach job-row handlers
        _watchService.JobArrived -= OnJobArrived;
        _watchService.Status -= OnWatchStatus;
        Jobs.CollectionChanged -= OnJobsChanged;
        _watchService.Dispose();
        if (_httpService != null)
        {
            _httpService.Status -= OnWatchStatus;
            _httpService.Dispose();
        }
    }

    private string? GetSelectedPrinterOrNull() => string.IsNullOrWhiteSpace(SelectedPrinter) ? null : SelectedPrinter;
    private string GetOperatorName() => string.IsNullOrWhiteSpace(OperatorName) ? Environment.UserName : OperatorName;

    private void OnJobArrived(WatchJob job)
    {
        Jobs.Add(new JobViewModel(job, GetSelectedPrinterOrNull()));
        // Auto-print history is reloaded via OnWatchStatus; operator jobs reload after PrintJob.
    }

    private void OnWatchStatus(string message)
    {
        Status = message;
        LoadHistory();   // auto-printed jobs should appear in Recent prints immediately
    }

    private void OnJobsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(JobsHeader));

    private void NotifyJobState()
    {
        OnPropertyChanged(nameof(JobMode));
        OnPropertyChanged(nameof(ShowData));
        OnPropertyChanged(nameof(ShowManual));
        OnPropertyChanged(nameof(ShowTemplateActions));
        OnPropertyChanged(nameof(JobIsSheet));
        OnPropertyChanged(nameof(ShowJobSheetNav));
        OnPropertyChanged(nameof(JobSheetInfo));
        CommandManager.InvalidateRequerySuggested();
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
        OnPropertyChanged(nameof(HasNoTemplates));
        if (HasNoTemplates)
            Status = $"No templates found in {AppConfig.TemplatesDir} — save them there from the Designer.";
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
        if (_template != null) SelectedJob = null;   // picking a template leaves job mode

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
                string.IsNullOrWhiteSpace(f.Prompt) ? f.Name : f.Prompt!, def, f.Required, f.AllowedValues, f.DataType);
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
        if (first.Length > 38) first = first[..38] + "…";
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
        OnPropertyChanged(nameof(ShowData));
        OnPropertyChanged(nameof(ShowManual));
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
        if (JobMode) return;   // job preview is driven by JobPreviewRow
        if (_template == null) { Preview = null; return; }
        try { Preview = PrintService.RenderPreview(_template, CurrentFields(), dpi: _template.Dpi); }
        catch (Exception ex) { LogService.Error("Print Station preview failed.", ex); }
    }

    private void RefreshJobPreview(JobRowViewModel row)
    {
        try
        {
            var t = row.Group.Group.Template;
            Preview = PrintService.RenderPreview(t, row.Row.Fields, dpi: t.Dpi);
        }
        catch (Exception ex) { LogService.Error("Print Station job preview failed.", ex); }
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
                allowFallbackPrinter: false, source: "PrintStation", printedBy: GetOperatorName());
            Status = $"Printed {Qty} × '{_template!.Name}' at {DateTime.Now:HH:mm:ss}.";
        });
    }

    private void PrintSelected()
    {
        if (_template == null || !HasData || _selectedRowIndex < 0) return;
        RunPrint(() =>
        {
            var row = _rows![_selectedRowIndex];
            PrintService.Print(_template!, row.Fields, Printer(), Qty,
                allowFallbackPrinter: false, source: "PrintStation", printedBy: GetOperatorName());
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

            // Ticked rows in range, at their (possibly overridden) quantities.
            var picked = new List<(ExcelRow Row, RowItem Item)>();
            var skipped = new List<string>();
            for (int i = lo - 1; i <= hi - 1 && i < _rows!.Count; i++)
            {
                var item = Rows[i];
                if (!item.Include) { skipped.Add($"Row {i + 1}: unticked."); continue; }
                if (item.Qty <= 0) { skipped.Add($"Row {i + 1}: quantity is 0."); continue; }
                picked.Add((_rows[i], item));
            }

            if (picked.Count == 0) { Status = $"Nothing to print in records {lo}–{hi} (no rows ticked)."; return; }

            // Validate the whole range before printing any label.
            var perRow = PrintService.ValidateRows(_template!, picked.Select(p => p.Row.Fields).ToList());
            var valid = new List<(ExcelRow Row, RowItem Item)>();
            var invalid = new List<string>();
            for (int i = 0; i < picked.Count; i++)
            {
                if (perRow[i].Count == 0) valid.Add(picked[i]);
                else invalid.Add($"Row {picked[i].Item.Index + 1}: {string.Join("; ", perRow[i])}");
            }

            if (invalid.Count > 0 && !SkipInvalidRows)
                throw new LabelValidationException(invalid);   // all-or-nothing (default)
            skipped.AddRange(invalid);

            if (valid.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "No valid rows to print:\n\n" + string.Join("\n", skipped), "Nothing printed");
                return;
            }

            // PrintRows flows sheet templates across page cells (Avery etc.); for ordinary
            // label-media templates it behaves exactly like per-row Print calls.
            int total = PrintService.PrintRows(
                valid.Select(p => (_template!, p.Row.Fields, p.Item.Qty)).ToList(),
                Printer(), allowFallbackPrinter: false, source: "PrintStation",
                printedBy: GetOperatorName());

            Status = $"Printed {total} label(s) from records {lo}–{hi}" +
                     (invalid.Count > 0 ? $" — {invalid.Count} invalid row(s) skipped." : ".");
            if (invalid.Count > 0)
                System.Windows.MessageBox.Show(
                    $"Printed {total} label(s). These rows were SKIPPED:\n\n" + string.Join("\n", invalid),
                    "Printed with skipped rows");
        });
    }

    // ── Watch-folder job actions ──────────────────────────────────────────────

    private void PrintJob()
    {
        var jobVm = SelectedJob;
        if (jobVm == null) return;

        RunPrint(() =>
        {
            // Push the operator's per-row choices into the parsed rows, then print via the shared engine.
            var include = new HashSet<PrintJobRow>();
            foreach (var g in jobVm.Groups)
                foreach (var r in g.Rows)
                {
                    r.Row.Qty = r.Qty;
                    if (r.Include) include.Add(r.Row);
                }

            var printedBy = GetOperatorName();
            var result = JobPrinter.Print(jobVm.Job.Parsed, Printer(),
                jobVm.Job.Folder.SkipInvalidRows, printedBy, source: "WatchFolder",
                rowFilter: include.Contains);

            _watchService.CompleteJob(jobVm.Job, result, printedBy);
            Jobs.Remove(jobVm);
            SelectedJob = null;
            Status = $"Job '{jobVm.FileName}' printed: {result.Summary}.";
            if (result.Skipped.Count > 0)
                System.Windows.MessageBox.Show(
                    $"Job '{jobVm.FileName}' printed: {result.Summary}.\n\nSkipped rows:\n" +
                    string.Join("\n", result.Skipped), "Printed with skipped rows");
        });
    }

    private void RejectJob()
    {
        var jobVm = SelectedJob;
        if (jobVm == null) return;
        var confirm = System.Windows.MessageBox.Show(
            $"Reject job '{jobVm.FileName}'?\n\nThe file moves to the failed folder (nothing prints).",
            "Reject job", System.Windows.MessageBoxButton.YesNo);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            _watchService.FailJob(jobVm.Job, $"Rejected by operator {GetOperatorName()}.");
            Jobs.Remove(jobVm);
            SelectedJob = null;
            Status = $"Job '{jobVm.FileName}' rejected.";
        }
        catch (Exception ex)
        {
            LogService.Error("Job reject failed.", ex);
            System.Windows.MessageBox.Show(ex.Message, "Reject failed");
        }
    }

    private void TestPrint()
    {
        if (_template == null) return;
        RunPrint(() =>
        {
            PrintService.PrintTest(_template!, CurrentFields(), Printer(), printedBy: GetOperatorName());
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
            int n = PrintService.Reprint(t, entry, printedBy: GetOperatorName());   // exact original IDs, no counter advance
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

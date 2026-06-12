using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace LabelDesigner.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly TemplateService _templateService;
    private string _statusMessage = "Ready";
    private string _ipcStatus = "";

    public DesignerViewModel Designer { get; } = new();

    public ObservableCollection<TemplateListItem> Templates { get; } = new();
    public ObservableCollection<string> RecentFiles { get; } = new();
    public ObservableCollection<RecentFileItem> RecentFileItems { get; } = new();

    public string StatusMessage { get => _statusMessage; set => Set(ref _statusMessage, value); }
    public string IpcStatus    { get => _ipcStatus;    set => Set(ref _ipcStatus, value); }

    public MainViewModel()
    {
        _templateService = new TemplateService(Services.AppConfig.TemplatesDir);

        LoadTemplateList();
        LoadRecentFiles();

        Designer.ElementAdded   += (_, _) => StatusMessage = $"Canvas: {Designer.Elements.Count} element(s)";
        Designer.ElementRemoved += (_, _) => StatusMessage = $"Canvas: {Designer.Elements.Count} element(s)";
    }

    // ─── File / Template commands ────────────────────────────────────────────────
    public ICommand NewCommand                 => new RelayCommand(NewTemplate);
    public ICommand OpenCommand                => new RelayCommand(OpenTemplate);
    public ICommand SaveCommand                => new RelayCommand(SaveTemplate);
    public ICommand SaveAsCommand              => new RelayCommand(SaveTemplateAs);
    public ICommand PrintCommand               => new RelayCommand(PrintLabel, () => Designer.Elements.Any());
    public ICommand ImportFromBarTenderCommand => new RelayCommand(ImportFromBarTender);
    public ICommand ManageFieldsCommand        => new RelayCommand(ManageFields);
    public ICommand ResizeCanvasCommand        => new RelayCommand(ResizeCanvas);
    public ICommand OpenPrintStationCommand    => new RelayCommand(OpenPrintStation);

    private void OpenPrintStation()
    {
        var wnd = new Views.PrintStationWindow { Owner = Application.Current.MainWindow };
        wnd.Show();
    }

    public ICommand OpenSettingsCommand => new RelayCommand(OpenSettings);

    private void OpenSettings()
    {
        var dlg = new Views.SettingsWindow { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
    }

    public ICommand PageSetupCommand => new RelayCommand(PageSetup);

    private void PageSetup()
    {
        var t = Designer.Template;
        var dlg = new Views.PageSetupDialog(t.Page, t.WidthMm, t.HeightMm)
        {
            Owner = Application.Current.MainWindow
        };
        if (dlg.ShowDialog() != true) return;

        t.Page = dlg.Result;
        Designer.IsDirty = true;
        StatusMessage = t.Page == null
            ? "Sheet mode OFF — prints directly on label media."
            : $"Sheet mode: {t.Page.Columns} × {t.Page.Rows} = {t.Page.CellsPerPage} per page" +
              (string.IsNullOrWhiteSpace(t.Page.BackTemplateName) ? "." : $", duplex back '{t.Page.BackTemplateName}'.");
        if (Designer.CurrentFilePath != null)
        {
            _templateService.Save(Designer.ToModel(), Designer.CurrentFilePath);
            StatusMessage += " (Saved)";
        }
    }

    public ICommand OpenTemplateRoutingCommand => new RelayCommand(OpenTemplateRouting);

    private void OpenTemplateRouting()
    {
        var dlg = new Views.TemplateRoutingDialog { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
    }

    // ─── Export (PNG / PDF / ZPL) ────────────────────────────────────────────────
    public ICommand ExportPngCommand => new RelayCommand(ExportPng, () => Designer.Elements.Any());
    public ICommand ExportPdfCommand => new RelayCommand(ExportPdf, () => Designer.Elements.Any());
    public ICommand ExportZplCommand => new RelayCommand(ExportZpl, () => Designer.Elements.Any());

    /// <summary>Fields used for export renders: the live data row when one is loaded, else the
    /// template's test data — the same values the canvas preview shows.</summary>
    private Dictionary<string, string> ExportFields() =>
        Designer.CurrentRowFields != null
            ? new Dictionary<string, string>(Designer.CurrentRowFields, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(Designer.Template.TestData, StringComparer.OrdinalIgnoreCase);

    private void ExportPng()
    {
        var dlg = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = Designer.TemplateName + ".png" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var template = Designer.ToModel();
            var bmp = Services.PrintService.RenderPreview(template, ExportFields(), dpi: template.Dpi);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = File.Create(dlg.FileName);
            encoder.Save(fs);
            StatusMessage = $"Exported PNG ({bmp.PixelWidth}×{bmp.PixelHeight} px @ {template.Dpi} dpi): {dlg.FileName}";
        }
        catch (Exception ex)
        {
            Services.LogService.Error("PNG export failed.", ex);
            MessageBox.Show(ex.Message, "PNG export failed");
        }
    }

    private void ExportPdf()
    {
        var dlg = new SaveFileDialog { Filter = "PDF document (*.pdf)|*.pdf", FileName = Designer.TemplateName + ".pdf" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var template = Designer.ToModel();
            var bmp = Services.PrintService.RenderPreview(template, ExportFields(), dpi: template.Dpi);
            Services.PdfExporter.Write(dlg.FileName, bmp, template.WidthMm, template.HeightMm);
            StatusMessage = $"Exported PDF ({template.WidthMm:F1}×{template.HeightMm:F1} mm): {dlg.FileName}";
        }
        catch (Exception ex)
        {
            Services.LogService.Error("PDF export failed.", ex);
            MessageBox.Show(ex.Message, "PDF export failed");
        }
    }

    private void ExportZpl()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "ZPL (*.zpl)|*.zpl|Text (*.txt)|*.txt",
            FileName = Designer.TemplateName + ".zpl"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var zpl = Services.PrintService.RenderZpl(Designer.ToModel(), ExportFields());
            File.WriteAllText(dlg.FileName, zpl);
            StatusMessage = $"Exported ZPL ({zpl.Length} chars): {dlg.FileName}";
        }
        catch (Exception ex)
        {
            Services.LogService.Error("ZPL export failed.", ex);
            MessageBox.Show(ex.Message, "ZPL export failed");
        }
    }

    // ─── Element commands (delegate to Designer) ─────────────────────────────────
    public ICommand AddTextCommand        => Designer.AddTextCommand;
    public ICommand AddBarcodeCommand     => Designer.AddBarcodeCommand;
    public ICommand AddImageCommand       => Designer.AddImageCommand;
    public ICommand AddRectangleCommand   => Designer.AddRectangleCommand;
    public ICommand AddLineCommand        => Designer.AddLineCommand;
    public ICommand AddTableCommand       => Designer.AddTableCommand;
    public ICommand DeleteSelectedCommand => Designer.DeleteSelectedCommand;

    // ─── Template list ────────────────────────────────────────────────────────────
    public ICommand OpenTemplateByItemCommand => new RelayCommand<TemplateListItem>(
        item => { if (item != null) OpenTemplateItem(item); });

    // ─── Recent files ─────────────────────────────────────────────────────────────
    public ICommand OpenRecentCommand => new RelayCommand<string>(path =>
    {
        if (!string.IsNullOrEmpty(path)) OpenRecentFile(path);
    });

    private void OpenRecentFile(string path)
    {
        var template = _templateService.Load(path);
        if (template == null) { MessageBox.Show($"Could not load:\n{path}", "Error"); return; }
        Designer.LoadTemplate(template, path);
        StatusMessage = $"Loaded: {template.Name}";
        Services.RecentFilesService.Push(path);
        LoadTemplateList();
        LoadRecentFiles();
    }

    // ─── New / Open / Save ───────────────────────────────────────────────────────
    private void NewTemplate()
    {
        var dlg = new Views.NewTemplateDlg();
        if (dlg.ShowDialog() == true)
        {
            Designer.NewTemplate(dlg.WidthMm, dlg.HeightMm, dlg.TemplateName, dlg.Fields);
            StatusMessage = $"New template '{dlg.TemplateName}' created.";
        }
    }

    private void ManageFields()
    {
        var mvm = new ManageFieldsViewModel(Designer.Template);
        var dlg = new Views.ManageFieldsDialog(mvm);
        if (dlg.ShowDialog() == true)
        {
            mvm.Apply();
            Designer.SyncAvailableFields();
            Designer.TryLoadExcelData();
            StatusMessage = $"Fields updated — {Designer.Template.Fields.Count} field(s).";
            if (Designer.CurrentFilePath != null)
            {
                _templateService.Save(Designer.ToModel(), Designer.CurrentFilePath);
                StatusMessage += " (Saved)";
            }
        }
    }

    private void OpenTemplate()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Label templates (*.lbl)|*.lbl|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        OpenRecentFile(dlg.FileName);
    }

    public void SaveTemplate()
    {
        if (Designer.CurrentFilePath == null) { SaveTemplateAs(); return; }
        _templateService.Save(Designer.ToModel(), Designer.CurrentFilePath);
        Designer.IsDirty = false;
        StatusMessage = $"Saved: {Designer.TemplateName}";
        Services.RecentFilesService.Push(Designer.CurrentFilePath);
        LoadTemplateList();
        LoadRecentFiles();
    }

    private void SaveTemplateAs()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Label templates (*.lbl)|*.lbl",
            FileName = Designer.TemplateName,
            InitialDirectory = Path.GetDirectoryName(_templateService.GetDefaultPath(Designer.Template))
        };
        if (dlg.ShowDialog() != true) return;
        Designer.CurrentFilePath = dlg.FileName;
        SaveTemplate();
    }

    // ─── Resize canvas ────────────────────────────────────────────────────────────
    private void ResizeCanvas()
    {
        var dlg = new Views.ResizeCanvasDlg(Designer.WidthMm, Designer.HeightMm, Designer.Template.Dpi, Designer.Template.PrinterProfile);
        if (dlg.ShowDialog() == true)
        {
            Designer.ResizeCanvas(dlg.WidthMm, dlg.HeightMm, dlg.Dpi);
            var p = Designer.Template.PrinterProfile;
            p.OutputMode  = dlg.OutputZpl ? PrintBackend.Zpl : PrintBackend.Gdi;
            p.Darkness    = dlg.Darkness;
            p.SpeedIps    = dlg.SpeedIps;
            p.NetworkHost = dlg.ZplHost;
            StatusMessage = $"Label set to {dlg.WidthMm:F1} × {dlg.HeightMm:F1} mm @ {dlg.Dpi} dpi ({(dlg.OutputZpl ? "ZPL" : "GDI")}).";
            if (Designer.CurrentFilePath != null)
                _templateService.Save(Designer.ToModel(), Designer.CurrentFilePath);
        }
    }

    // ─── Print ────────────────────────────────────────────────────────────────────
    private void PrintLabel()
    {
        var template  = Designer.ToModel();
        var rawFields = Designer.CurrentRowFields;
        int qty       = Designer.CurrentRowPrintQty;   // already computed by ExcelImportService
        var allRows   = Designer.AllRows;              // full row set enables Print-All mode

        var printVm = new PrintPreviewViewModel(template,
            fieldsOverride: rawFields,
            dataQty: qty,
            allRows: allRows);
        var wnd = new Views.PrintPreviewWindow { DataContext = printVm };
        wnd.ShowDialog();
    }

    private void ImportFromBarTender()
    {
        var fileDlg = new OpenFileDialog
        {
            Title  = "Select BarTender File",
            Filter = "BarTender files (*.btw)|*.btw|All files (*.*)|*.*"
        };
        if (fileDlg.ShowDialog() != true) return;

        var meta      = Services.BtwImportService.ReadHeader(fileDlg.FileName);
        var importDlg = new Views.BtwImportDialog(fileDlg.FileName, meta) { Owner = Application.Current.MainWindow };
        if (importDlg.ShowDialog() != true) return;

        Designer.NewTemplate(importDlg.WidthMm, importDlg.HeightMm, importDlg.TemplateName, new List<string>());
        SaveTemplate();
        StatusMessage = $"Imported '{importDlg.TemplateName}' — canvas ready, add your elements manually.";
    }

    /// <summary>Called by IpcServer when a LabelJob arrives from JaneERP.</summary>
    /// <summary>
    /// Called by IpcServer when a LabelJob arrives from JaneERP. Returns a structured outcome
    /// (logged by JobId and written back to duplex callers). Never throws.
    /// </summary>
    public LabelJobResponse HandlePrintJob(LabelJob job)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            LabelJobResponse Resp(string status, string? msg) =>
                new() { JobId = job.JobId, Status = status, Message = msg };

            var template = FindTemplate(job.TemplateName, job.TemplateId);
            if (template == null)
                return Resp("rejected", $"Template '{job.TemplateName}' not found.");

            // A job that omits a required field must NOT print a blank label — reject it loudly.
            var missing = template.FieldDefinitions
                .Where(f => f.Required &&
                            (!job.Fields.TryGetValue(f.Name, out var v) || string.IsNullOrWhiteSpace(v)))
                .Select(f => f.Name)
                .ToList();
            if (missing.Count > 0)
                return Resp("rejected", $"Missing required field(s): {string.Join(", ", missing)}.");

            if (job.ShowPreview)
            {
                // Show NON-modally — a modal ShowDialog here would block the single-threaded IPC
                // listener until a human closed it, stalling every queued JaneERP job.
                var pvm = new PrintPreviewViewModel(template, job.Fields,
                    dataQty: job.Quantity, printerName: job.PrinterName);
                var wnd = new Views.PrintPreviewWindow { DataContext = pvm };
                wnd.Show();
                return Resp("accepted", "Opened in print preview.");
            }

            try
            {
                // Unattended job: do NOT silently fall back to the default printer.
                Services.PrintService.Print(template, job.Fields, job.PrinterName, job.Quantity,
                    allowFallbackPrinter: false, source: "IPC", printedBy: "JaneERP");
                return Resp("printed", $"Printed {job.Quantity} label(s).");
            }
            catch (Services.PrinterNotFoundException ex) { return Resp("error", ex.Message); }
            catch (Services.LabelValidationException ex) { return Resp("error", ex.Message); }
            catch (Exception ex)
            {
                Services.LogService.Error("IPC print error.", ex);
                return Resp("error", ex.Message);
            }
        });
    }

    // ─── Template list helpers ────────────────────────────────────────────────────
    private void OpenTemplateItem(TemplateListItem item)
    {
        OpenRecentFile(item.FilePath);
    }

    private void LoadRecentFiles()
    {
        RecentFiles.Clear();
        RecentFileItems.Clear();
        foreach (var f in Services.RecentFilesService.Load())
        {
            RecentFiles.Add(f);
            var path = f;
            RecentFileItems.Add(new RecentFileItem(
                System.IO.Path.GetFileNameWithoutExtension(path),
                path,
                new RelayCommand(() => OpenRecentFile(path))));
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────
    private LabelTemplate? FindTemplate(string name, string? id)
    {
        foreach (var path in _templateService.GetTemplatePaths())
        {
            var t = _templateService.Load(path);
            if (t == null) continue;
            if ((!string.IsNullOrEmpty(id) && t.Id.ToString() == id) ||
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                return t;
        }
        return null;
    }

    private void LoadTemplateList()
    {
        Templates.Clear();
        foreach (var path in _templateService.GetTemplatePaths())
        {
            var t = _templateService.Load(path);
            if (t != null)
            {
                var item = new TemplateListItem(t.Name, path);
                item.OpenCommand = new RelayCommand(() => OpenTemplateItem(item));
                Templates.Add(item);
            }
        }
    }
}

public class TemplateListItem
{
    public string Name     { get; }
    public string FilePath { get; }
    public ICommand OpenCommand { get; set; } = new RelayCommand(() => { });

    public TemplateListItem(string name, string filePath)
    {
        Name     = name;
        FilePath = filePath;
    }
}

public class RecentFileItem
{
    public string   Name        { get; }
    public string   FilePath    { get; }
    public ICommand OpenCommand { get; }

    public RecentFileItem(string name, string filePath, ICommand openCommand)
    {
        Name        = name;
        FilePath    = filePath;
        OpenCommand = openCommand;
    }
}

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
    private TemplateListItem? _selectedTemplateItem;

    public DesignerViewModel Designer { get; } = new();

    public ObservableCollection<TemplateListItem> Templates { get; } = new();

    public string StatusMessage { get => _statusMessage; set => Set(ref _statusMessage, value); }
    public string IpcStatus    { get => _ipcStatus;    set => Set(ref _ipcStatus, value); }

    public TemplateListItem? SelectedTemplateItem
    {
        get => _selectedTemplateItem;
        set => Set(ref _selectedTemplateItem, value);
    }

    public MainViewModel()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LabelDesigner", "Templates");
        _templateService = new TemplateService(dir);

        LoadTemplateList();

        Designer.ElementAdded   += (_, _) => StatusMessage = $"Canvas: {Designer.Elements.Count} element(s)";
        Designer.ElementRemoved += (_, _) => StatusMessage = $"Canvas: {Designer.Elements.Count} element(s)";
    }

    // ─── File / Template commands ────────────────────────────────────────────
    public ICommand NewCommand                  => new RelayCommand(NewTemplate);
    public ICommand OpenCommand                 => new RelayCommand(OpenTemplate);
    public ICommand SaveCommand                 => new RelayCommand(SaveTemplate);
    public ICommand SaveAsCommand               => new RelayCommand(SaveTemplateAs);
    public ICommand PrintCommand                => new RelayCommand(PrintLabel, () => Designer.Elements.Any());
    public ICommand ImportExcelCommand          => new RelayCommand(ImportExcel);
    public ICommand ImportFromBarTenderCommand  => new RelayCommand(ImportFromBarTender);
    public ICommand ManageFieldsCommand         => new RelayCommand(ManageFields);
    public ICommand ResizeCanvasCommand         => new RelayCommand(ResizeCanvas);

    // ─── Element commands (delegate to Designer) ─────────────────────────────
    public ICommand AddTextCommand        => Designer.AddTextCommand;
    public ICommand AddBarcodeCommand     => Designer.AddBarcodeCommand;
    public ICommand AddImageCommand       => Designer.AddImageCommand;
    public ICommand AddRectangleCommand   => Designer.AddRectangleCommand;
    public ICommand DeleteSelectedCommand => Designer.DeleteSelectedCommand;

    // ─── Template list context menu ──────────────────────────────────────────
    public ICommand OpenSelectedTemplateCommand     => new RelayCommand(OpenSelectedTemplate, () => _selectedTemplateItem != null);
    public ICommand RenameSelectedTemplateCommand   => new RelayCommand(RenameSelectedTemplate, () => _selectedTemplateItem != null);
    public ICommand DuplicateSelectedTemplateCommand => new RelayCommand(DuplicateSelectedTemplate, () => _selectedTemplateItem != null);
    public ICommand DeleteSelectedTemplateCommand   => new RelayCommand(DeleteSelectedTemplate, () => _selectedTemplateItem != null);

    // ─── New / Open / Save ───────────────────────────────────────────────────
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
            Filter = "Label templates (*.lbl)|*.lbl|All files (*.*)|*.*",
            InitialDirectory = _templateService.GetTemplatePaths().FirstOrDefault() is { } p
                ? Path.GetDirectoryName(p) : null
        };
        if (dlg.ShowDialog() != true) return;

        var template = _templateService.Load(dlg.FileName);
        if (template == null) { MessageBox.Show("Failed to load template.", "Error"); return; }

        Designer.LoadTemplate(template, dlg.FileName);
        StatusMessage = $"Loaded: {template.Name}";
        LoadTemplateList();
    }

    public void SaveTemplate()
    {
        if (Designer.CurrentFilePath == null) { SaveTemplateAs(); return; }
        _templateService.Save(Designer.ToModel(), Designer.CurrentFilePath);
        StatusMessage = $"Saved: {Designer.TemplateName}";
        LoadTemplateList();
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

    // ─── Resize canvas ────────────────────────────────────────────────────────
    private void ResizeCanvas()
    {
        var dlg = new Views.ResizeCanvasDlg(Designer.WidthMm, Designer.HeightMm);
        if (dlg.ShowDialog() == true)
        {
            Designer.ResizeCanvas(dlg.WidthMm, dlg.HeightMm);
            StatusMessage = $"Canvas resized to {dlg.WidthMm:F1} × {dlg.HeightMm:F1} mm.";
            if (Designer.CurrentFilePath != null)
                _templateService.Save(Designer.ToModel(), Designer.CurrentFilePath);
        }
    }

    // ─── Print ────────────────────────────────────────────────────────────────
    private void PrintLabel()
    {
        var printVm = new PrintPreviewViewModel(Designer.ToModel(), fieldsOverride: null);
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

    private void ImportExcel()
    {
        var importVm = new ExcelImportViewModel(_templateService);
        var wnd = new Views.ExcelImportWindow(importVm, ExecutePrintJobs);
        wnd.ShowDialog();
    }

    private void ExecutePrintJobs(List<LabelJob> jobs)
    {
        foreach (var job in jobs)
        {
            var template = FindTemplate(job.TemplateName, job.TemplateId);
            if (template == null) continue;

            if (job.ShowPreview)
            {
                var pvm = new PrintPreviewViewModel(template, job.Fields,
                    dataQty: job.Quantity, printerName: job.PrinterName);
                var pwnd = new Views.PrintPreviewWindow { DataContext = pvm };
                pwnd.ShowDialog();
            }
            else
            {
                Services.PrintService.Print(template, job.Fields, job.PrinterName, job.Quantity);
            }
        }
        StatusMessage = "Print jobs complete.";
    }

    /// <summary>Called by IpcServer when a LabelJob arrives from JaneERP.</summary>
    public void HandlePrintJob(LabelJob job)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var template = FindTemplate(job.TemplateName, job.TemplateId);
            if (template == null)
            {
                MessageBox.Show($"Template '{job.TemplateName}' not found.", "Label Designer");
                return;
            }

            if (job.ShowPreview)
            {
                var pvm = new PrintPreviewViewModel(template, job.Fields,
                    dataQty: job.Quantity, printerName: job.PrinterName);
                var wnd = new Views.PrintPreviewWindow { DataContext = pvm };
                wnd.ShowDialog();
            }
            else
            {
                Services.PrintService.Print(template, job.Fields, job.PrinterName, job.Quantity);
            }
        });
    }

    // ─── Template list context menu actions ──────────────────────────────────
    private void OpenSelectedTemplate()
    {
        if (_selectedTemplateItem == null) return;
        var template = _templateService.Load(_selectedTemplateItem.FilePath);
        if (template == null) { MessageBox.Show("Failed to load template.", "Error"); return; }
        Designer.LoadTemplate(template, _selectedTemplateItem.FilePath);
        StatusMessage = $"Loaded: {template.Name}";
    }

    private void RenameSelectedTemplate()
    {
        if (_selectedTemplateItem == null) return;
        var dlg = new Views.InputDialog("Rename Template", "New name:", _selectedTemplateItem.Name);
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;

        var newName = dlg.Value.Trim();
        var template = _templateService.Load(_selectedTemplateItem.FilePath);
        if (template == null) return;

        var oldPath = _selectedTemplateItem.FilePath;
        template.Name = newName;
        var newPath = _templateService.GetDefaultPath(template);
        _templateService.Save(template, newPath);

        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            TryDelete(oldPath);

        // Update designer if this template is currently loaded
        if (Designer.CurrentFilePath != null &&
            string.Equals(Designer.CurrentFilePath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            Designer.TemplateName = newName;
            Designer.CurrentFilePath = newPath;
        }

        LoadTemplateList();
        StatusMessage = $"Renamed to '{newName}'.";
    }

    private void DuplicateSelectedTemplate()
    {
        if (_selectedTemplateItem == null) return;
        var template = _templateService.Load(_selectedTemplateItem.FilePath);
        if (template == null) return;

        template.Id   = Guid.NewGuid();
        template.Name = template.Name + " Copy";
        var newPath = _templateService.GetDefaultPath(template);
        _templateService.Save(template, newPath);
        LoadTemplateList();
        StatusMessage = $"Duplicated as '{template.Name}'.";
    }

    private void DeleteSelectedTemplate()
    {
        if (_selectedTemplateItem == null) return;
        var result = MessageBox.Show(
            $"Delete template '{_selectedTemplateItem.Name}'?\nThis cannot be undone.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        TryDelete(_selectedTemplateItem.FilePath);

        // Clear designer if it had this template open
        if (Designer.CurrentFilePath != null &&
            string.Equals(Designer.CurrentFilePath, _selectedTemplateItem.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            Designer.NewTemplate();
        }

        LoadTemplateList();
        StatusMessage = "Template deleted.";
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────
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
            if (t != null) Templates.Add(new TemplateListItem(t.Name, path));
        }
    }
}

public record TemplateListItem(string Name, string FilePath);

using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;

namespace LabelDesigner.ViewModels;

public class ImportRowViewModel : ViewModelBase
{
    private bool _isChecked = true;

    public Dictionary<string, string> Fields { get; }
    public int PrintQty { get; }
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    /// <summary>First two field values, shown in the list's primary column.</summary>
    public string Display => string.Join("   ", Fields.Values.Take(2).Where(v => !string.IsNullOrEmpty(v)));

    /// <summary>Remaining field values as a compact summary.</summary>
    public string Summary => string.Join("  |  ",
        Fields.Select(kv => $"{kv.Key}: {kv.Value}").Skip(2).Take(6));

    public ImportRowViewModel(ExcelRow row)
    {
        Fields   = row.Fields;
        PrintQty = row.PrintQty > 0 ? row.PrintQty : 1;
    }
}

public class ExcelImportViewModel : ViewModelBase
{
    private readonly TemplateService _templates;
    private string _status = "Select a template, browse an Excel file, then click Reload.";
    private string? _filePath;
    private TemplateListItem? _selectedTemplate;
    private bool _showPreview = true;

    public ObservableCollection<ImportRowViewModel> Rows { get; } = new();
    public ObservableCollection<TemplateListItem> Templates { get; } = new();

    public string Status          { get => _status;           set => Set(ref _status, value); }
    public string? FilePath       { get => _filePath;         set => Set(ref _filePath, value); }
    public bool ShowPreview       { get => _showPreview;      set => Set(ref _showPreview, value); }

    public TemplateListItem? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            Set(ref _selectedTemplate, value);
            // Pre-fill Excel path from template's last-used file
            if (value != null)
            {
                var t = _templates.Load(value.FilePath);
                if (t?.DefaultExcelPath != null && File.Exists(t.DefaultExcelPath))
                    FilePath = t.DefaultExcelPath;
            }
        }
    }

    public ICommand BrowseFileCommand   => new RelayCommand(BrowseFile);
    public ICommand LoadFileCommand     => new RelayCommand(LoadFile, () => !string.IsNullOrEmpty(FilePath) && SelectedTemplate != null);
    public ICommand SelectAllCommand    => new RelayCommand(() => { foreach (var r in Rows) r.IsChecked = true; });
    public ICommand SelectNoneCommand   => new RelayCommand(() => { foreach (var r in Rows) r.IsChecked = false; });
    public ICommand PrintSelectedCommand => new RelayCommand(PrintSelected,
        () => Rows.Any(r => r.IsChecked) && SelectedTemplate != null);

    public event EventHandler<List<LabelJob>>? PrintJobsReady;

    public ExcelImportViewModel(TemplateService templates)
    {
        _templates = templates;
        RefreshTemplateList();
    }

    private void RefreshTemplateList()
    {
        Templates.Clear();
        foreach (var path in _templates.GetTemplatePaths())
        {
            var t = _templates.Load(path);
            if (t != null) Templates.Add(new TemplateListItem(t.Name, path));
        }
        SelectedTemplate = Templates.FirstOrDefault();
    }

    private void BrowseFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Title  = "Open Excel Data File"
        };
        if (dlg.ShowDialog() != true) return;
        FilePath = dlg.FileName;
        LoadFile();
    }

    private void LoadFile()
    {
        if (string.IsNullOrEmpty(FilePath) || SelectedTemplate == null) return;

        var template = _templates.Load(SelectedTemplate.FilePath);
        if (template == null) { Status = "Could not load template."; return; }

        if (template.ExcelColumnMapping.Count == 0)
        {
            Status = "Template has no field mappings. Use Template → Manage Fields to configure them first.";
            return;
        }

        try
        {
            var rows = ExcelImportService.Load(FilePath, template);
            Rows.Clear();
            foreach (var row in rows)
                Rows.Add(new ImportRowViewModel(row));

            Status = $"Loaded {Rows.Count} row(s) from {Path.GetFileName(FilePath)}.";

            // Auto-check rows with PrintQty > 0
            foreach (var r in Rows)
                r.IsChecked = r.PrintQty > 0;
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private void PrintSelected()
    {
        if (SelectedTemplate == null) return;
        var template = _templates.Load(SelectedTemplate.FilePath);
        if (template == null) { Status = "Could not load template."; return; }

        var jobs = new List<LabelJob>();
        foreach (var row in Rows.Where(r => r.IsChecked))
        {
            jobs.Add(new LabelJob
            {
                TemplateName = template.Name,
                Fields       = row.Fields,
                Quantity     = row.PrintQty,
                ShowPreview  = ShowPreview
            });
        }

        Status = $"Sending {jobs.Count} job(s) to print…";
        PrintJobsReady?.Invoke(this, jobs);
    }
}

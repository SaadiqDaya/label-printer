using LabelDesigner.Core.Models;
using LabelDesigner.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;

namespace LabelDesigner.ViewModels;

/// <summary>One row in the field-to-column mapping grid.</summary>
public class FieldMappingItem : ViewModelBase
{
    private string _fieldName;
    private string _excelColumn;
    private string _testValue;

    public string FieldName   { get => _fieldName;   set => Set(ref _fieldName, value); }
    public string ExcelColumn { get => _excelColumn; set => Set(ref _excelColumn, value); }

    /// <summary>Sample value shown in Print Preview when no live data source is active.</summary>
    public string TestValue   { get => _testValue;   set => Set(ref _testValue, value); }

    public FieldMappingItem(string fieldName, string excelColumn, string testValue = "")
    {
        _fieldName   = fieldName;
        _excelColumn = excelColumn;
        _testValue   = testValue;
    }
}

/// <summary>
/// ViewModel for the Manage Fields dialog.
/// Edits a LabelTemplate's Fields list, ExcelColumnMapping, PrintQtyColumn and DefaultExcelPath.
/// Call <see cref="Apply"/> after the dialog is confirmed to write changes back to the template.
/// </summary>
public class ManageFieldsViewModel : ViewModelBase
{
    private readonly LabelTemplate _template;
    private string _excelPath;
    private string _printQtyColumn;
    private string _newFieldName = "";
    private FieldMappingItem? _selectedMapping;
    private string _headerHint = "";

    public string TemplateName => _template.Name;

    public ObservableCollection<FieldMappingItem> Mappings { get; } = new();

    public string ExcelPath
    {
        get => _excelPath;
        set { Set(ref _excelPath, value); LoadHeaders(); }
    }

    public string PrintQtyColumn
    {
        get => _printQtyColumn;
        set => Set(ref _printQtyColumn, value);
    }

    public string NewFieldName
    {
        get => _newFieldName;
        set => Set(ref _newFieldName, value);
    }

    public FieldMappingItem? SelectedMapping
    {
        get => _selectedMapping;
        set => Set(ref _selectedMapping, value);
    }

    /// <summary>Human-readable list of Excel column headers, shown as a hint below the grid.</summary>
    public string HeaderHint
    {
        get => _headerHint;
        private set => Set(ref _headerHint, value);
    }

    public bool HasHeaderHint => !string.IsNullOrEmpty(_headerHint);

    public ICommand BrowseExcelCommand  => new RelayCommand(BrowseExcel);
    public ICommand AddFieldCommand     => new RelayCommand(AddField, () => !string.IsNullOrWhiteSpace(NewFieldName));
    public ICommand RemoveFieldCommand  => new RelayCommand(RemoveSelected, () => SelectedMapping != null);

    public ManageFieldsViewModel(LabelTemplate template)
    {
        _template        = template;
        _excelPath       = template.DefaultExcelPath ?? "";
        _printQtyColumn  = template.PrintQtyColumn   ?? "";

        // Seed grid from existing fields (maintain declaration order)
        foreach (var field in template.Fields)
        {
            var col  = template.ExcelColumnMapping.TryGetValue(field, out var c) ? c : "";
            var test = template.TestData.TryGetValue(field, out var t) ? t : "";
            Mappings.Add(new FieldMappingItem(field, col, test));
        }

        // Add any mapped fields not yet in the Fields list
        foreach (var kv in template.ExcelColumnMapping)
        {
            if (!Mappings.Any(m => m.FieldName == kv.Key))
            {
                var test = template.TestData.TryGetValue(kv.Key, out var t) ? t : "";
                Mappings.Add(new FieldMappingItem(kv.Key, kv.Value, test));
            }
        }

        if (!string.IsNullOrEmpty(_excelPath))
            LoadHeaders();
    }

    private void BrowseExcel()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Title  = "Select Excel Data File for this Template"
        };
        if (dlg.ShowDialog() != true) return;
        ExcelPath = dlg.FileName;
    }

    private void LoadHeaders()
    {
        if (!File.Exists(_excelPath)) { HeaderHint = ""; OnPropertyChanged(nameof(HasHeaderHint)); return; }
        try
        {
            var headers = ExcelImportService.ReadHeaders(_excelPath);
            HeaderHint = string.Join("   ", headers.Select(h => $"{h.Letter}={h.Header}"));
        }
        catch
        {
            HeaderHint = "(Could not read headers)";
        }
        OnPropertyChanged(nameof(HasHeaderHint));
    }

    private void AddField()
    {
        var name = new string(NewFieldName.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (string.IsNullOrEmpty(name)) return;
        if (Mappings.Any(m => m.FieldName == name)) return;
        Mappings.Add(new FieldMappingItem(name, ""));
        NewFieldName = "";
    }

    private void RemoveSelected()
    {
        if (SelectedMapping != null)
            Mappings.Remove(SelectedMapping);
    }

    /// <summary>
    /// Write the edited mappings back to the template.
    /// Call this only when the dialog is confirmed (DialogResult = true).
    /// </summary>
    public void Apply()
    {
        _template.Fields = Mappings.Select(m => m.FieldName).ToList();

        _template.ExcelColumnMapping = Mappings
            .Where(m => !string.IsNullOrWhiteSpace(m.ExcelColumn))
            .ToDictionary(
                m => m.FieldName,
                m => m.ExcelColumn.Trim().ToUpper());

        _template.TestData = Mappings
            .Where(m => !string.IsNullOrWhiteSpace(m.TestValue))
            .ToDictionary(
                m => m.FieldName,
                m => m.TestValue.Trim());

        _template.PrintQtyColumn =
            string.IsNullOrWhiteSpace(PrintQtyColumn) ? null : PrintQtyColumn.Trim().ToUpper();

        _template.DefaultExcelPath =
            string.IsNullOrWhiteSpace(ExcelPath) ? null : ExcelPath.Trim();
    }
}

using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace LabelDesigner.ViewModels;

/// <summary>
/// Drives the BarTender-library migration dialog: scan a .btw folder, track per-file status in
/// the shared BtwMigration.json (templates folder), create .lbl skeletons (size + optional
/// backdrop + optional data-file field seeding). The tracker is saved after every change.
/// </summary>
public class BtwMigrationViewModel : ViewModelBase
{
    private readonly Core.Services.TemplateService _templates = new(Services.AppConfig.TemplatesDir);
    private BtwMigrationStore _store;
    private string _folderPath = "";
    private string _summary = "";
    private BtwMigrationEntryViewModel? _selected;

    public ObservableCollection<BtwMigrationEntryViewModel> Entries { get; } = new();

    public string FolderPath { get => _folderPath; set => Set(ref _folderPath, value); }
    public string Summary    { get => _summary;    set => Set(ref _summary, value); }

    public BtwMigrationEntryViewModel? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    /// <summary>Set when the user clicks "Open in Designer" — the owner opens it after the dialog closes.</summary>
    public string? TemplateToOpen { get; private set; }

    public event EventHandler? RequestClose;

    public BtwMigrationViewModel()
    {
        _store = LoadStoreSafe();
        FolderPath = !string.IsNullOrWhiteSpace(_store.LastFolder)
            ? _store.LastFolder
            : Services.UserSettings.Current.BtwMigrationFolder;
        RefreshEntries();
    }

    private static BtwMigrationStore LoadStoreSafe()
    {
        try { return BtwMigrationStore.Load(Services.AppConfig.TemplatesDir); }
        catch (Exception ex)
        {
            Services.LogService.Error("Failed to read the migration tracker — starting empty.", ex);
            MessageBox.Show("The migration tracker (BtwMigration.json) could not be read:\n" + ex.Message +
                            "\n\nStarting with an empty tracker. Fix or delete the file if this persists.",
                "Migration tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return new BtwMigrationStore();
        }
    }

    public ICommand BrowseCommand           => new RelayCommand(Browse);
    public ICommand ScanCommand             => new RelayCommand(Scan, () => !string.IsNullOrWhiteSpace(FolderPath));
    public ICommand CreateSkeletonCommand   => new RelayCommand(CreateSkeletonForSelected, () => Selected != null && !Selected.IsUnreadable);
    public ICommand CreateAllPendingCommand => new RelayCommand(CreateAllPending, () => Entries.Any(e => e.Model.Status == BtwMigrationStatus.Pending));
    public ICommand OpenTargetCommand       => new RelayCommand(OpenTarget, () => Selected != null && !string.IsNullOrWhiteSpace(Selected.TargetTemplateName));

    private void Browse()
    {
        var dlg = new OpenFolderDialog { Title = "Select your BarTender template folder" };
        if (!string.IsNullOrWhiteSpace(FolderPath) && Directory.Exists(FolderPath))
            dlg.InitialDirectory = FolderPath;
        if (dlg.ShowDialog() == true) FolderPath = dlg.FolderName;
    }

    private void Scan()
    {
        if (!Directory.Exists(FolderPath))
        {
            MessageBox.Show($"Folder not found:\n{FolderPath}", "Scan",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var scanned = BtwMigrationService.ScanFolder(FolderPath);
            _store.MergeScan(FolderPath, scanned);
            _store.LastFolder = FolderPath;
            SaveStore();

            var settings = Services.UserSettings.Current;
            settings.BtwMigrationFolder = FolderPath;
            Services.UserSettings.Save(settings);

            RefreshEntries();
        }
        catch (Exception ex)
        {
            Services.LogService.Error("BarTender folder scan failed.", ex);
            MessageBox.Show("Scan failed:\n" + ex.Message, "Scan",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CreateSkeletonForSelected()
    {
        var vm = Selected;
        if (vm == null) return;

        // Optional backdrop image (scan/photo/screenshot of the old label) to trace over.
        string? backdrop = null;
        var pick = MessageBox.Show(
            "Add a backdrop image to trace over?\n\n" +
            "Yes — pick an image of the old label (scan, photo or screenshot). It is placed " +
            "semi-transparent and locked, and never prints.\nNo — start from a blank canvas.",
            "Create skeleton", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (pick == MessageBoxResult.Cancel) return;
        if (pick == MessageBoxResult.Yes)
        {
            var imgDlg = new OpenFileDialog
            {
                Title  = "Backdrop image of the old label",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
            };
            if (imgDlg.ShowDialog() != true) return;
            backdrop = imgDlg.FileName;
        }

        // Optional field seeding from the data file the old label printed from.
        List<(string Letter, string Header)>? columns = null;
        string? dataFile = null;
        var seed = MessageBox.Show(
            "Seed the template's fields from a data file?\n\n" +
            "Yes — pick the CSV/Excel file this label printed from; its column headers become " +
            "the template's fields with the column mapping already set.\nNo — add fields later.",
            "Create skeleton", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (seed == MessageBoxResult.Cancel) return;
        if (seed == MessageBoxResult.Yes)
        {
            var dataDlg = new OpenFileDialog
            {
                Title  = "Data file (headers become fields)",
                Filter = "Data files (*.csv;*.tsv;*.xlsx;*.xlsm)|*.csv;*.tsv;*.xlsx;*.xlsm|All files (*.*)|*.*"
            };
            if (dataDlg.ShowDialog() != true) return;
            try
            {
                columns  = Services.DataImporter.ReadHeaders(dataDlg.FileName);
                dataFile = dataDlg.FileName;
            }
            catch (Exception ex)
            {
                Services.LogService.Error("Reading data-file headers failed.", ex);
                MessageBox.Show("Could not read headers from that file:\n" + ex.Message,
                    "Create skeleton", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        if (CreateSkeleton(vm, backdrop, columns, dataFile))
            MessageBox.Show(
                $"Skeleton created: '{vm.TargetTemplateName}'.\n\n" +
                "Select it and press 'Open in Designer' to rebuild the label. When it prints " +
                "correctly, set its status to Done.",
                "Create skeleton", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CreateAllPending()
    {
        var pending = Entries.Where(e => e.Model.Status == BtwMigrationStatus.Pending).ToList();
        if (pending.Count == 0) return;
        if (MessageBox.Show(
                $"Create blank skeletons (correct size + name, no backdrop) for all {pending.Count} pending file(s)?",
                "Create all skeletons", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        int ok = 0;
        foreach (var vm in pending)
            if (CreateSkeleton(vm, backdropImagePath: null, columns: null, dataFilePath: null)) ok++;

        UpdateSummary();
        MessageBox.Show($"{ok} of {pending.Count} skeleton(s) created.", "Create all skeletons",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool CreateSkeleton(BtwMigrationEntryViewModel vm, string? backdropImagePath,
        List<(string Letter, string Header)>? columns, string? dataFilePath)
    {
        try
        {
            var baseName = string.IsNullOrWhiteSpace(vm.Model.Title)
                ? Path.GetFileNameWithoutExtension(vm.Model.SourcePath)
                : vm.Model.Title;
            var name = BtwMigrationService.UniqueTemplateName(baseName,
                n => File.Exists(_templates.GetDefaultPath(new LabelTemplate { Name = n })));

            var t = BtwMigrationService.BuildSkeleton(vm.Model, backdropImagePath, columns,
                dataFilePath, templateName: name);
            _templates.Save(t, _templates.GetDefaultPath(t));

            vm.Model.TargetTemplateName = name;
            vm.Model.Status = BtwMigrationStatus.SkeletonCreated;
            vm.Model.UpdatedUtc = DateTime.UtcNow;
            vm.RefreshFromModel();
            SaveStore();
            UpdateSummary();
            return true;
        }
        catch (Exception ex)
        {
            Services.LogService.Error($"Skeleton creation failed for {vm.Model.SourcePath}.", ex);
            MessageBox.Show($"Skeleton creation failed for {vm.FileName}:\n{ex.Message}",
                "Create skeleton", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void OpenTarget()
    {
        var name = Selected?.TargetTemplateName;
        if (string.IsNullOrWhiteSpace(name)) return;
        var path = _templates.GetDefaultPath(new LabelTemplate { Name = name });
        if (!File.Exists(path))
        {
            MessageBox.Show($"Template file not found:\n{path}\n\nWas it renamed or deleted?",
                "Open in Designer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        TemplateToOpen = path;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    internal void SaveStore()
    {
        try { _store.Save(Services.AppConfig.TemplatesDir); }
        catch (Exception ex)
        {
            Services.LogService.Error("Failed to save the migration tracker.", ex);
            MessageBox.Show("Could not save the migration tracker:\n" + ex.Message,
                "Migration tracker", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    internal void UpdateSummary()
    {
        var (migrated, total) = _store.Progress();
        int pending  = _store.Entries.Count(e => e.Status == BtwMigrationStatus.Pending);
        int problems = _store.Entries.Count(e => e.Status == BtwMigrationStatus.Unreadable || e.SourceMissing);
        Summary = total == 0
            ? "No BarTender files tracked yet — pick the folder and press Scan."
            : $"{migrated} of {total} migrated · {pending} pending" +
              (problems > 0 ? $" · {problems} problem file(s)" : "");
    }

    private void RefreshEntries()
    {
        Entries.Clear();
        foreach (var e in _store.Entries
                     .OrderBy(x => x.Status == BtwMigrationStatus.Done)     // active work first
                     .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase))
            Entries.Add(new BtwMigrationEntryViewModel(e, this));
        UpdateSummary();
    }
}

/// <summary>Row wrapper: editable Status/Notes write straight back to the tracker.</summary>
public class BtwMigrationEntryViewModel : ViewModelBase
{
    private readonly BtwMigrationViewModel _owner;
    public BtwMigrationEntry Model { get; }

    public BtwMigrationEntryViewModel(BtwMigrationEntry model, BtwMigrationViewModel owner)
    {
        Model  = model;
        _owner = owner;
    }

    private static readonly string[] _statusOptions = Enum.GetNames<BtwMigrationStatus>();
    // Instance property: WPF {Binding} can't resolve statics on the row's DataContext.
    public string[] StatusOptions => _statusOptions;

    public string FileName   => Model.FileName;
    public string SourcePath => Model.SourcePath;
    public string SizeText   => Model.SizeText;
    public string Printer    => Model.Printer;
    public bool   IsUnreadable => Model.Status == BtwMigrationStatus.Unreadable;
    public bool   IsProblem  => IsUnreadable || Model.SourceMissing;
    public string ProblemText => Model.SourceMissing ? "file missing" : IsUnreadable ? "unreadable" : "";

    public string Status
    {
        get => Model.Status.ToString();
        set
        {
            if (!Enum.TryParse<BtwMigrationStatus>(value, out var s) || s == Model.Status) return;
            Model.Status = s;
            Model.UpdatedUtc = DateTime.UtcNow;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUnreadable));
            OnPropertyChanged(nameof(IsProblem));
            _owner.SaveStore();
            _owner.UpdateSummary();
        }
    }

    public string TargetTemplateName => Model.TargetTemplateName;

    public string Notes
    {
        get => Model.Notes;
        set
        {
            if (Model.Notes == value) return;
            Model.Notes = value;
            Model.UpdatedUtc = DateTime.UtcNow;
            OnPropertyChanged();
            _owner.SaveStore();
        }
    }

    public void RefreshFromModel()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(TargetTemplateName));
        OnPropertyChanged(nameof(IsUnreadable));
        OnPropertyChanged(nameof(IsProblem));
        OnPropertyChanged(nameof(ProblemText));
    }
}

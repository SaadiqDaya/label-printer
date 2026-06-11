using LabelDesigner.Core.Services;
using LabelDesigner.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;

namespace LabelDesigner.ViewModels;

/// <summary>One editable watch-folder row in the Settings dialog.</summary>
public class WatchFolderRowViewModel : ViewModelBase
{
    private string _path = "";
    private bool _enabled = true;
    private bool _autoPrint;
    private bool _skipInvalidRows;
    private string _defaultTemplate = "";

    public string Path { get => _path; set => Set(ref _path, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool AutoPrint { get => _autoPrint; set => Set(ref _autoPrint, value); }
    public bool SkipInvalidRows { get => _skipInvalidRows; set => Set(ref _skipInvalidRows, value); }
    public string DefaultTemplate { get => _defaultTemplate; set => Set(ref _defaultTemplate, value); }

    public WatchFolderRowViewModel() { }

    public WatchFolderRowViewModel(WatchFolderConfig c)
    {
        _path = c.Path; _enabled = c.Enabled; _autoPrint = c.AutoPrint;
        _skipInvalidRows = c.SkipInvalidRows; _defaultTemplate = c.DefaultTemplate;
    }

    public WatchFolderConfig ToConfig() => new()
    {
        Path = Path.Trim(), Enabled = Enabled, AutoPrint = AutoPrint,
        SkipInvalidRows = SkipInvalidRows, DefaultTemplate = DefaultTemplate.Trim()
    };
}

/// <summary>
/// Backs the Settings dialog: (1) where CONTINUOUS serial counters and print history live —
/// Local (this PC only) or a Shared network folder; (2) the station's watch folders, where any
/// ERP can drop batch CSVs to print (Print Station picks them up on its next start).
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private bool _useShared;
    private string _sharedDir;
    private string _status = "";
    private WatchFolderRowViewModel? _selectedWatchFolder;
    private bool _httpApiEnabled;
    private string _httpApiPort = "3100";

    /// <summary>True when env/appsettings pinned the data dir — the choice is then read-only.</summary>
    public bool IsAdminLocked { get; } = AppConfig.IsDataDirAdminLocked;
    public bool CanEdit => !IsAdminLocked;

    public string EffectiveDataDir => AppConfig.DataDir;

    public bool UseShared
    {
        get => _useShared;
        set { if (Set(ref _useShared, value)) OnPropertyChanged(nameof(CanEditPath)); }
    }

    public bool CanEditPath => UseShared && !IsAdminLocked;

    public string SharedDir { get => _sharedDir; set => Set(ref _sharedDir, value); }
    public string Status { get => _status; set => Set(ref _status, value); }

    /// <summary>Shim-compatible HTTP print API, served by the Print Station on localhost.</summary>
    public bool HttpApiEnabled { get => _httpApiEnabled; set => Set(ref _httpApiEnabled, value); }
    public string HttpApiPort { get => _httpApiPort; set => Set(ref _httpApiPort, value); }

    public ObservableCollection<WatchFolderRowViewModel> WatchFolders { get; } = new();

    public WatchFolderRowViewModel? SelectedWatchFolder
    {
        get => _selectedWatchFolder;
        set => Set(ref _selectedWatchFolder, value);
    }

    /// <summary>Template names for the watch-folder "default template" dropdown.</summary>
    public List<string> TemplateNames { get; } = new();

    /// <summary>Raised to close the window; bool = whether settings were saved.</summary>
    public event EventHandler<bool>? CloseRequested;

    public ICommand BrowseCommand            => new RelayCommand(Browse, () => CanEditPath);
    public ICommand AddWatchFolderCommand    => new RelayCommand(AddWatchFolder);
    public ICommand RemoveWatchFolderCommand => new RelayCommand(RemoveWatchFolder, () => SelectedWatchFolder != null);
    public ICommand SaveCommand              => new RelayCommand(Save);
    public ICommand CancelCommand            => new RelayCommand(() => CloseRequested?.Invoke(this, false));

    public SettingsViewModel()
    {
        var s = UserSettings.Current;
        _useShared = string.Equals(s.SerialStorageMode, "Shared", StringComparison.OrdinalIgnoreCase);
        _sharedDir = s.SharedDataDir;
        _httpApiEnabled = s.HttpApiEnabled;
        _httpApiPort = s.HttpApiPort.ToString();
        foreach (var wf in s.WatchFolders) WatchFolders.Add(new WatchFolderRowViewModel(wf));

        try
        {
            var templates = new TemplateService(AppConfig.TemplatesDir);
            foreach (var path in templates.GetTemplatePaths())
            {
                var t = templates.Load(path);
                if (t != null) TemplateNames.Add(t.Name);
            }
            TemplateNames.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) { LogService.Warn($"Could not list templates for settings: {ex.Message}"); }
    }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the shared folder for serial counters & print history"
        };
        if (dlg.ShowDialog() == true)
        {
            SharedDir = dlg.FolderName;
            UseShared = true;
        }
    }

    private void AddWatchFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a watch-folder ROOT (inbox/processing/printed/failed are created inside it)"
        };
        if (dlg.ShowDialog() != true) return;
        var row = new WatchFolderRowViewModel { Path = dlg.FolderName };
        WatchFolders.Add(row);
        SelectedWatchFolder = row;
    }

    private void RemoveWatchFolder()
    {
        if (SelectedWatchFolder != null) WatchFolders.Remove(SelectedWatchFolder);
    }

    private void Save()
    {
        if (UseShared && !IsAdminLocked)
        {
            if (string.IsNullOrWhiteSpace(SharedDir)) { Status = "Choose a shared folder first."; return; }
            if (!TryWriteTest(SharedDir, out var err)) { Status = "Can't write to that folder: " + err; return; }
        }

        // Watch folders must be writable now — failing at 6 AM on the shop floor is worse than failing here.
        foreach (var wf in WatchFolders.Where(w => w.Enabled && !string.IsNullOrWhiteSpace(w.Path)))
        {
            if (!TryWriteTest(wf.Path, out var err))
            {
                Status = $"Can't write to watch folder '{wf.Path}': {err}";
                return;
            }
        }

        if (!int.TryParse(HttpApiPort?.Trim(), out var port) || port < 1 || port > 65535)
        {
            Status = "HTTP API port must be a number between 1 and 65535.";
            return;
        }

        var current = UserSettings.Current;
        UserSettings.Save(new UserSettings.Data
        {
            SerialStorageMode = IsAdminLocked ? current.SerialStorageMode : (UseShared ? "Shared" : "Local"),
            SharedDataDir     = IsAdminLocked ? current.SharedDataDir : (SharedDir ?? ""),
            OperatorName      = current.OperatorName,
            WatchFolders      = WatchFolders
                .Where(w => !string.IsNullOrWhiteSpace(w.Path))
                .Select(w => w.ToConfig()).ToList(),
            HttpApiEnabled    = HttpApiEnabled,
            HttpApiPort       = port,
        });
        LogService.Info($"Settings saved: storage={(UseShared ? "Shared: " + SharedDir : "Local")}, " +
                        $"{WatchFolders.Count} watch folder(s), HTTP API {(HttpApiEnabled ? $"ON :{port}" : "off")}.");
        CloseRequested?.Invoke(this, true);
    }

    private static bool TryWriteTest(string dir, out string error)
    {
        error = "";
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".labeldesigner_write_test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }
}

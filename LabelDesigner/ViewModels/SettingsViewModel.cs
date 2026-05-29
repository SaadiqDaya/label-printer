using LabelDesigner.Services;
using System.IO;
using System.Windows.Input;

namespace LabelDesigner.ViewModels;

/// <summary>
/// Backs the Settings dialog: choose where CONTINUOUS serial counters and print history live —
/// Local (this PC only) or a Shared network folder (so multiple stations share one sequence/audit).
/// Reset-per-batch serials ignore this entirely.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private bool _useShared;
    private string _sharedDir;
    private string _status = "";

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

    /// <summary>Raised to close the window; bool = whether settings were saved.</summary>
    public event EventHandler<bool>? CloseRequested;

    public ICommand BrowseCommand => new RelayCommand(Browse, () => CanEditPath);
    public ICommand SaveCommand   => new RelayCommand(Save);
    public ICommand CancelCommand => new RelayCommand(() => CloseRequested?.Invoke(this, false));

    public SettingsViewModel()
    {
        var s = UserSettings.Current;
        _useShared = string.Equals(s.SerialStorageMode, "Shared", StringComparison.OrdinalIgnoreCase);
        _sharedDir = s.SharedDataDir;
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

    private void Save()
    {
        if (IsAdminLocked) { CloseRequested?.Invoke(this, false); return; } // pinned by admin

        if (UseShared)
        {
            if (string.IsNullOrWhiteSpace(SharedDir)) { Status = "Choose a shared folder first."; return; }
            if (!TryWriteTest(SharedDir, out var err)) { Status = "Can't write to that folder: " + err; return; }
        }

        UserSettings.Save(new UserSettings.Data
        {
            SerialStorageMode = UseShared ? "Shared" : "Local",
            SharedDataDir     = SharedDir ?? ""
        });
        LogService.Info($"Serial/history storage set to {(UseShared ? "Shared: " + SharedDir : "Local")}.");
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

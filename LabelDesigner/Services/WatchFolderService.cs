using LabelDesigner.Core.Services;
using System.IO;
using System.Windows.Threading;

namespace LabelDesigner.Services;

/// <summary>A job file this station has claimed (moved into its processing folder) and parsed.</summary>
public class WatchJob
{
    public WatchFolderConfig Folder { get; init; } = null!;
    /// <summary>Where the claimed file currently lives (processing\MACHINE\...).</summary>
    public string ClaimedPath { get; init; } = "";
    public string OriginalName { get; init; } = "";
    public DateTime ReceivedAt { get; init; } = DateTime.Now;
    public ParsedPrintJob Parsed { get; init; } = null!;
}

/// <summary>
/// Watches the configured job-drop folders. Each root grows four subfolders:
///   inbox\      — the ERP (any ERP) drops batch CSV/TSV files here
///   processing\MACHINE\ — this station claims a file by ATOMICALLY moving it here (the move IS the
///                 lock: two stations watching the same inbox can never both print one job)
///   printed\    — completed jobs, with a .result.txt audit sidecar (moved, never deleted)
///   failed\     — rejected/failed jobs, with a .error.txt naming the rows and reasons
/// Detection is FileSystemWatcher + a poll timer (the watcher alone is unreliable on network shares).
/// Everything runs on the UI dispatcher — printing WPF visuals requires the STA thread anyway.
/// </summary>
public class WatchFolderService : IDisposable
{
    private const int PollSeconds = 5;
    /// <summary>A file must be this old (last write) before we claim it, so we never grab a CSV mid-write.</summary>
    private static readonly TimeSpan MinAge = TimeSpan.FromSeconds(2);
    private static readonly string[] JobExtensions = { ".csv", ".tsv" };

    private readonly Dispatcher _dispatcher;
    private readonly TemplateService _templates;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly DispatcherTimer _timer;
    private bool _scanning;

    /// <summary>An operator-release job was claimed and parsed (raised on the dispatcher).</summary>
    public event Action<WatchJob>? JobArrived;
    /// <summary>An auto-print job finished (summary) or a job failed (reason) — narration for the status bar.</summary>
    public event Action<string>? Status;

    /// <summary>Station printer used for groups whose template has no profile printer.</summary>
    public Func<string?>? FallbackPrinterProvider { get; set; }
    /// <summary>Operator name stamped on history entries for auto-printed jobs.</summary>
    public Func<string>? PrintedByProvider { get; set; }

    public WatchFolderService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _templates = new TemplateService(AppConfig.TemplatesDir);
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(PollSeconds)
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start()
    {
        foreach (var folder in UserSettings.Current.WatchFolders.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Path)))
        {
            try
            {
                var dirs = EnsureDirs(folder.Path);
                int recovered = RecoverOrphans(dirs.Processing, dirs.Inbox);
                if (recovered > 0)
                    Notify($"Recovered {recovered} unfinished job(s) from a previous session in {folder.Path}.");

                var fsw = new FileSystemWatcher(dirs.Inbox) { EnableRaisingEvents = true };
                fsw.Created += OnInboxChanged;
                fsw.Renamed += OnInboxRenamed;
                _watchers.Add(fsw);
            }
            catch (Exception ex)
            {
                LogService.Error($"Watch folder '{folder.Path}' could not be started.", ex);
                Notify($"Watch folder unavailable: {folder.Path} — {ex.Message}");
            }
        }
        _timer.Start();
        ScanAll();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        foreach (var fsw in _watchers)
        {
            fsw.EnableRaisingEvents = false;
            fsw.Created -= OnInboxChanged;
            fsw.Renamed -= OnInboxRenamed;
            fsw.Dispose();
        }
        _watchers.Clear();
    }

    private void OnTimerTick(object? sender, EventArgs e) => ScanAll();
    private void OnInboxChanged(object sender, FileSystemEventArgs e) => _dispatcher.BeginInvoke(ScanAll);
    private void OnInboxRenamed(object sender, RenamedEventArgs e) => _dispatcher.BeginInvoke(ScanAll);

    /// <summary>Scans every enabled folder's inbox and claims any settled job file.</summary>
    public void ScanAll()
    {
        if (_scanning) return;   // re-entrancy guard (FSW bursts + timer)
        _scanning = true;
        try
        {
            var routes = TemplateRouteStore.Load();
            foreach (var folder in UserSettings.Current.WatchFolders.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Path)))
                ScanFolder(folder, routes);
        }
        catch (Exception ex) { LogService.Error("Watch-folder scan failed.", ex); }
        finally { _scanning = false; }
    }

    private void ScanFolder(WatchFolderConfig folder, IReadOnlyList<Core.Models.TemplateRoute> routes)
    {
        WatchDirs dirs;
        try { dirs = EnsureDirs(folder.Path); }
        catch (Exception ex)
        {
            LogService.Warn($"Watch folder '{folder.Path}' unreachable: {ex.Message}");
            return;
        }

        List<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(dirs.Inbox)
                .Where(p => JobExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .Where(p => DateTime.UtcNow - File.GetLastWriteTimeUtc(p) >= MinAge)
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Warn($"Could not list inbox '{dirs.Inbox}': {ex.Message}");
            return;
        }

        foreach (var file in candidates)
        {
            if (!TryClaim(file, dirs.Processing, out var claimed)) continue;   // writer still busy or another station won

            var name = Path.GetFileName(file);
            ParsedPrintJob parsed;
            try
            {
                var rows = CsvImportService.LoadGeneric(claimed);
                parsed = PrintJobParser.Parse(rows, LookupTemplate, routes, folder.DefaultTemplate);
                if (parsed.TotalRows == 0)
                    throw new InvalidOperationException("The file contains no data rows (a header row plus at least one data row is required).");
            }
            catch (Exception ex)
            {
                FinishFile(claimed, dirs.Failed, $"{name}: could not be read.\n\n{ex.Message}", ".error.txt");
                Notify($"Job '{name}' failed: {ex.Message}");
                continue;
            }

            var job = new WatchJob
            {
                Folder = folder, ClaimedPath = claimed, OriginalName = name,
                ReceivedAt = DateTime.Now, Parsed = parsed
            };

            if (folder.AutoPrint) AutoPrint(job);
            else
            {
                Notify($"Job '{name}' received — {parsed.TotalRows} row(s), {parsed.Groups.Count} template group(s). Waiting for operator.");
                JobArrived?.Invoke(job);
            }
        }
    }

    private void AutoPrint(WatchJob job)
    {
        var printedBy = PrintedByProvider?.Invoke() ?? Environment.UserName;
        try
        {
            var result = JobPrinter.Print(job.Parsed, FallbackPrinterProvider?.Invoke(),
                job.Folder.SkipInvalidRows, printedBy, source: "WatchFolder");
            CompleteJob(job, result, printedBy);
            Notify($"Auto-printed '{job.OriginalName}': {result.Summary}.");
        }
        catch (Exception ex)
        {
            FailJob(job, ex.Message);
            Notify($"Auto-print of '{job.OriginalName}' FAILED: {ex.Message}");
        }
    }

    /// <summary>Moves a finished job to printed\ and writes its .result.txt audit sidecar.</summary>
    public void CompleteJob(WatchJob job, JobPrintResult result, string printedBy)
    {
        var dirs = EnsureDirs(job.Folder.Path);
        FinishFile(job.ClaimedPath, dirs.Printed, result.BuildReport(job.OriginalName, printedBy), ".result.txt");
    }

    /// <summary>Moves a failed/rejected job to failed\ and writes its .error.txt sidecar.</summary>
    public void FailJob(WatchJob job, string reason)
    {
        var dirs = EnsureDirs(job.Folder.Path);
        FinishFile(job.ClaimedPath, dirs.Failed,
            $"Job:     {job.OriginalName}\nWhen:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nStation: {Environment.MachineName}\n\n{reason}\n",
            ".error.txt");
    }

    private void FinishFile(string claimedPath, string destDir, string sidecarText, string sidecarExtension)
    {
        try
        {
            var final = MoveToUnique(claimedPath, destDir);
            File.WriteAllText(final + sidecarExtension, sidecarText);
        }
        catch (Exception ex)
        {
            // The job file must never be lost: if the move fails it simply stays in processing\
            // and is recovered into the inbox on the next start.
            LogService.Error($"Could not archive job file '{claimedPath}' to '{destDir}'.", ex);
        }
    }

    private LabelDesigner.Core.Models.LabelTemplate? LookupTemplate(string name)
    {
        foreach (var path in _templates.GetTemplatePaths())
        {
            var t = _templates.Load(path);
            if (t != null && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
        }
        return null;
    }

    private void Notify(string message)
    {
        LogService.Info("[WatchFolder] " + message);
        Status?.Invoke(message);
    }

    // ── File mechanics (static + path-based so they're unit-testable) ──────────────

    public record WatchDirs(string Inbox, string Processing, string Printed, string Failed);

    /// <summary>Creates the standard subfolders under a watch root. Processing is PER MACHINE so one
    /// station's crash leftovers are never confused with another station's live work.</summary>
    public static WatchDirs EnsureDirs(string root)
    {
        var dirs = new WatchDirs(
            Path.Combine(root, "inbox"),
            Path.Combine(root, "processing", Environment.MachineName),
            Path.Combine(root, "printed"),
            Path.Combine(root, "failed"));
        Directory.CreateDirectory(dirs.Inbox);
        Directory.CreateDirectory(dirs.Processing);
        Directory.CreateDirectory(dirs.Printed);
        Directory.CreateDirectory(dirs.Failed);
        return dirs;
    }

    /// <summary>
    /// Claims a job by moving it into the processing folder. The rename is atomic on a volume, so if
    /// the ERP is still writing the file, or another station moves it first, this fails cleanly and
    /// returns false — the file is untouched and will be retried (or was claimed elsewhere).
    /// </summary>
    public static bool TryClaim(string inboxFile, string processingDir, out string claimedPath)
    {
        claimedPath = UniqueDestination(processingDir, Path.GetFileName(inboxFile));
        try
        {
            File.Move(inboxFile, claimedPath);
            return true;
        }
        catch (IOException) { claimedPath = ""; return false; }
        catch (UnauthorizedAccessException) { claimedPath = ""; return false; }
    }

    /// <summary>Moves a file into a folder, suffixing " (n)" on name collisions. Returns the final path.</summary>
    public static string MoveToUnique(string sourceFile, string destDir)
    {
        Directory.CreateDirectory(destDir);
        var dest = UniqueDestination(destDir, Path.GetFileName(sourceFile));
        File.Move(sourceFile, dest);
        return dest;
    }

    private static string UniqueDestination(string dir, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var dest = Path.Combine(dir, fileName);
        for (int n = 1; File.Exists(dest); n++)
            dest = Path.Combine(dir, $"{stem} ({n}){ext}");
        return dest;
    }

    /// <summary>Returns THIS station's crashed-mid-job leftovers to the inbox so they print again.
    /// Other stations' processing folders are left alone. Returns how many files were recovered.</summary>
    public static int RecoverOrphans(string processingDir, string inboxDir)
    {
        if (!Directory.Exists(processingDir)) return 0;
        int recovered = 0;
        foreach (var file in Directory.EnumerateFiles(processingDir).ToList())
        {
            try { MoveToUnique(file, inboxDir); recovered++; }
            catch (Exception ex) { LogService.Warn($"Could not recover orphaned job '{file}': {ex.Message}"); }
        }
        return recovered;
    }
}

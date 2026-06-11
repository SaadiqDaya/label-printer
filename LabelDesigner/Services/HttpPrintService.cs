using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace LabelDesigner.Services;

/// <summary>A BarTender-shim-style print request: one dict per PHYSICAL label (quantity = repetition).
/// <c>templatePath</c> may be a bare template name, a .lbl file name, or a full .btw path — only the
/// file-name stem is used to find the LabelDesigner template with that name.</summary>
public class ShimPrintRequest
{
    public string TemplatePath { get; set; } = "";
    public string? PrinterName { get; set; }
    public string? JobName { get; set; }
    public List<Dictionary<string, string>> Labels { get; set; } = new();
}

/// <summary>Wire response, camelCased to match the BarTender shim: {success, labelsRendered, printer, error}.</summary>
public class ShimPrintResponse
{
    public bool Success { get; set; }
    public int LabelsRendered { get; set; }
    public string? Printer { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// HTTP print API compatible with the BarTender shim contract, so any system that drives the shim
/// (e.g. an ops dashboard) can switch to LabelDesigner by changing one base URL:
///   GET  /health            → {"status":"ok", ...}
///   GET  /printers          → {"success":true,"printers":[...]}
///   POST /api/print         → {"success":true,"labelsRendered":N,"printer":"..."}
/// Listens on http://localhost:{port}/ only (no remote exposure; localhost prefixes need no admin
/// URL ACL). Printing is marshalled to the UI dispatcher (WPF visuals demand the STA thread) and is
/// all-or-nothing per request: every label row is validated before the first one prints.
/// Runs in the Print Station when enabled in File ▸ Settings.
/// </summary>
public class HttpPrintService : IDisposable
{
    private const int MaxBodyBytes = 4 * 1024 * 1024;   // a 4 MB request is already thousands of labels

    private static readonly JsonSerializerOptions WireJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpListener _listener = new();
    private readonly Dispatcher _dispatcher;
    private readonly TemplateService _templates;
    private CancellationTokenSource? _cts;

    public int Port { get; }

    /// <summary>Station printer used when neither the request nor the template names one.</summary>
    public Func<string?>? FallbackPrinterProvider { get; set; }
    /// <summary>Operator name stamped on history entries for HTTP-printed jobs.</summary>
    public Func<string>? PrintedByProvider { get; set; }
    /// <summary>Narration for the Print Station status bar (raised on the dispatcher).</summary>
    public event Action<string>? Status;

    public HttpPrintService(int port, Dispatcher dispatcher)
    {
        Port = port;
        _dispatcher = dispatcher;
        _templates = new TemplateService(AppConfig.TemplatesDir);
    }

    /// <summary>Starts listening. Throws (caller reports loudly) when the port can't be bound —
    /// a print API that silently isn't there would strand the calling system.</summary>
    public void Start()
    {
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoop(_cts.Token));
        LogService.Info($"HTTP print API listening on http://localhost:{Port}/.");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { /* already down */ }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) when (ct.IsCancellationRequested || !_listener.IsListening) { break; }
            catch (Exception ex)
            {
                LogService.Error("HTTP print API accept failed (continuing to listen).", ex);
                continue;
            }

            try { Handle(ctx); }
            catch (Exception ex)
            {
                // One bad request must never take the API down.
                LogService.Error("HTTP print API request failed.", ex);
                TryWriteJson(ctx, 500, new ShimPrintResponse { Success = false, Error = ex.Message });
            }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        var method = ctx.Request.HttpMethod.ToUpperInvariant();
        var path = (ctx.Request.Url?.AbsolutePath ?? "/").TrimEnd('/');
        if (path.Length == 0) path = "/";

        switch (method, path)
        {
            case ("GET", "/health"):
                TryWriteJson(ctx, 200, new { status = "ok", service = "LabelDesigner", version = AppConfig.AppVersion });
                return;

            case ("GET", "/printers"):
            {
                var printers = new List<string>();
                foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters) printers.Add(p);
                TryWriteJson(ctx, 200, new { success = true, printers });
                return;
            }

            case ("POST", "/api/print"):
            {
                ShimPrintRequest? req;
                try { req = ReadRequest(ctx.Request); }
                catch (Exception ex)
                {
                    TryWriteJson(ctx, 400, new ShimPrintResponse { Success = false, Error = "Bad request: " + ex.Message });
                    return;
                }
                if (req == null || string.IsNullOrWhiteSpace(req.TemplatePath))
                {
                    TryWriteJson(ctx, 400, new ShimPrintResponse { Success = false, Error = "Body must include templatePath and labels." });
                    return;
                }

                // Printing builds WPF visuals → must run on the UI thread.
                var response = _dispatcher.Invoke(() => PrintJob(req));
                TryWriteJson(ctx, response.Success ? 200 : 422, response);
                return;
            }

            default:
                TryWriteJson(ctx, 404, new ShimPrintResponse { Success = false, Error = $"Unknown endpoint: {method} {path}" });
                return;
        }
    }

    private static ShimPrintRequest? ReadRequest(HttpListenerRequest request)
    {
        if (request.ContentLength64 > MaxBodyBytes)
            throw new InvalidOperationException($"Body exceeds {MaxBodyBytes / 1024 / 1024} MB.");
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        var json = reader.ReadToEnd();
        if (json.Length > MaxBodyBytes) throw new InvalidOperationException("Body too large.");
        return JsonSerializer.Deserialize<ShimPrintRequest>(json, WireJson);
    }

    /// <summary>Runs on the dispatcher. All-or-nothing: every row validates before anything prints.</summary>
    private ShimPrintResponse PrintJob(ShimPrintRequest req)
    {
        var jobName = string.IsNullOrWhiteSpace(req.JobName) ? "(unnamed)" : req.JobName!;
        try
        {
            if (req.Labels.Count == 0)
                return new ShimPrintResponse { Success = false, Error = "labels is empty — nothing to print." };

            var stem = TemplateStem(req.TemplatePath);
            var template = FindTemplate(stem);
            if (template == null)
                return new ShimPrintResponse
                {
                    Success = false,
                    Error = $"Template \"{stem}\" not found in {AppConfig.TemplatesDir} (from templatePath \"{req.TemplatePath}\")."
                };

            var errors = PrintService.ValidateBatch(template, req.Labels);
            if (errors.Count > 0)
                return new ShimPrintResponse { Success = false, Error = string.Join(" | ", errors) };

            var printer = FirstNonBlank(req.PrinterName, template.PrinterProfile.PrinterName,
                                        FallbackPrinterProvider?.Invoke());
            var printedBy = PrintedByProvider?.Invoke() ?? Environment.UserName;

            int rendered = 0;
            foreach (var (fields, copies) in GroupConsecutive(req.Labels))
            {
                PrintService.Print(template, fields, printer, copies,
                    allowFallbackPrinter: false, validate: false, source: "HTTP", printedBy: printedBy);
                rendered += copies;
            }

            Notify($"HTTP job '{jobName}': printed {rendered} × '{template.Name}' to '{printer ?? "(default)"}'.");
            return new ShimPrintResponse { Success = true, LabelsRendered = rendered, Printer = printer ?? "(default)" };
        }
        catch (Exception ex)
        {
            LogService.Error($"HTTP job '{jobName}' failed.", ex);
            Notify($"HTTP job '{jobName}' FAILED: {ex.Message}");
            return new ShimPrintResponse { Success = false, Error = ex.Message };
        }
    }

    private LabelTemplate? FindTemplate(string name)
    {
        foreach (var path in _templates.GetTemplatePaths())
        {
            var t = _templates.Load(path);
            if (t == null) continue;
            if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase))
                return t;
        }
        return null;
    }

    private void Notify(string message)
    {
        LogService.Info("[HTTP] " + message);
        Status?.Invoke(message);
    }

    private static void TryWriteJson(HttpListenerContext ctx, int status, object body)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), WireJson);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
        catch (Exception ex) { LogService.Warn($"HTTP response write failed: {ex.Message}"); }
    }

    // ── Pure helpers (static so they're unit-testable without a listener) ─────────

    /// <summary>"C:\apps\templates\DoorTreats-50ml.btw" → "DoorTreats-50ml" (also handles bare names and .lbl).</summary>
    public static string TemplateStem(string templatePath)
    {
        var name = templatePath.Replace('/', '\\');
        var slash = name.LastIndexOf('\\');
        if (slash >= 0) name = name[(slash + 1)..];
        return Path.GetFileNameWithoutExtension(name);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>Collapses consecutive identical label dicts into (fields, copies) so the shim's
    /// one-dict-per-physical-label contract maps onto PrintService's copies parameter (one history
    /// entry and one serial reservation per run of identical labels).</summary>
    public static List<(Dictionary<string, string> Fields, int Copies)> GroupConsecutive(
        IReadOnlyList<Dictionary<string, string>> labels)
    {
        var groups = new List<(Dictionary<string, string> Fields, int Copies)>();
        foreach (var label in labels)
        {
            if (groups.Count > 0 && SameFields(groups[^1].Fields, label))
                groups[^1] = (groups[^1].Fields, groups[^1].Copies + 1);
            else
                groups.Add((new Dictionary<string, string>(label, StringComparer.OrdinalIgnoreCase), 1));
        }
        return groups;
    }

    private static bool SameFields(Dictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in b)
            if (!a.TryGetValue(k, out var av) || !string.Equals(av, v, StringComparison.Ordinal))
                return false;
        return true;
    }
}

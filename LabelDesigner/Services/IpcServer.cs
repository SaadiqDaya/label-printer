using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Windows.Threading;

namespace LabelDesigner.Services;

/// <summary>
/// Named-pipe server that listens for LabelJob requests from JaneERP (or any caller).
/// Pipe name: LabelDesigner
///
/// From JaneERP, send a JSON-serialized LabelJob to \\.\pipe\LabelDesigner.
/// The server deserializes it and raises JobReceived on the UI dispatcher (if available).
///
/// Hardened: ACL grants access only to the current Windows user, and the read is
/// bounded so a malicious or buggy client cannot exhaust memory by sending GBs of data.
/// </summary>
public class IpcServer : IDisposable
{
    public static string PipeName => AppConfig.PipeName;

    /// <summary>Max bytes accepted from a single client connection (64 KB is huge for a LabelJob).</summary>
    private static int MaxPayloadBytes => AppConfig.MaxPayloadBytes;

    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private readonly Dispatcher? _uiDispatcher;

    public event EventHandler<LabelJob>? JobReceived;

    /// <summary>
    /// Job handler that returns a structured outcome. When set, this is preferred over
    /// <see cref="JobReceived"/> and its result is logged (by JobId) and written back to the
    /// caller best-effort. Invoked on the UI dispatcher.
    /// </summary>
    public Func<LabelJob, LabelJobResponse>? JobHandler { get; set; }

    public IpcServer()
    {
        // Capture the UI dispatcher when constructed on the UI thread so we can marshal events.
        _uiDispatcher = Dispatcher.FromThread(Thread.CurrentThread);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listenTask = null;
    }

    public void Dispose() => Stop();

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreateRestrictedPipe();
                await pipe.WaitForConnectionAsync(ct);

                var json = await ReadBoundedAsync(pipe, MaxPayloadBytes, ct);
                if (string.IsNullOrWhiteSpace(json)) continue;

                var job = JsonSerializer.Deserialize<LabelJob>(json, TemplateService.JsonOptions);
                if (job == null) continue;

                // Process the job and obtain a structured outcome (handler runs on the UI thread).
                var response = InvokeHandler(job);
                LogService.Info($"IPC job '{response.JobId}' → {response.Status}: {response.Message}");

                // Best-effort ack back to a duplex caller. Legacy one-way clients have already
                // disconnected (IsConnected == false), so this is a no-op for them — the print
                // still happened above. This keeps the existing JaneERP integration working.
                await WriteResponseAsync(pipe, response, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Pipe disconnect, oversize payload, or bad JSON — log it (so failures are traceable)
                // and KEEP LISTENING. One bad client must never take the line down.
                LogService.Error("IPC listen-loop error (continuing to listen).", ex);
            }
        }
    }

    /// <summary>
    /// Creates the pipe with an ACL restricted to the current user, so other local users
    /// cannot connect and trigger arbitrary print jobs on this machine.
    /// </summary>
    private static NamedPipeServerStream CreateRestrictedPipe()
    {
        var security = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User!;
        security.AddAccessRule(new PipeAccessRule(
            currentUser,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        // InOut so we can write a best-effort acknowledgement back to duplex callers. The read path
        // is unchanged (read-until-disconnect), so legacy one-way clients are unaffected.
        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    /// <summary>Runs the job handler on the UI dispatcher and returns its outcome.</summary>
    private LabelJobResponse InvokeHandler(LabelJob job)
    {
        try
        {
            if (JobHandler != null)
            {
                if (_uiDispatcher != null && !_uiDispatcher.CheckAccess())
                    return _uiDispatcher.Invoke(() => JobHandler(job));
                return JobHandler(job);
            }

            // Legacy event path (no structured response available).
            RaiseJobReceived(job);
            return new LabelJobResponse { JobId = job.JobId, Status = "accepted" };
        }
        catch (Exception ex)
        {
            LogService.Error("IPC job handler threw.", ex);
            return new LabelJobResponse { JobId = job.JobId, Status = "error", Message = ex.Message };
        }
    }

    /// <summary>Writes the JSON response back if the caller kept the pipe open; never throws.</summary>
    private static async Task WriteResponseAsync(NamedPipeServerStream pipe, LabelJobResponse response, CancellationToken ct)
    {
        try
        {
            if (!pipe.IsConnected) return;
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(response, TemplateService.JsonOptions));
            await pipe.WriteAsync(bytes.AsMemory(0, bytes.Length), ct);
            await pipe.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to write IPC response (one-way caller, expected).", ex);
        }
    }

    /// <summary>Reads up to <paramref name="maxBytes"/> from the pipe. Returns null if the client exceeds it.</summary>
    private static async Task<string?> ReadBoundedAsync(NamedPipeServerStream pipe, int maxBytes, CancellationToken ct)
    {
        var buffer = new byte[4096];
        using var ms = new MemoryStream();
        int read;
        while ((read = await pipe.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            if (ms.Length + read > maxBytes) return null; // reject oversized payloads
            ms.Write(buffer, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private void RaiseJobReceived(LabelJob job)
    {
        var handler = JobReceived;
        if (handler == null) return;

        if (_uiDispatcher != null && !_uiDispatcher.CheckAccess())
            _uiDispatcher.BeginInvoke(new Action(() => handler(this, job)));
        else
            handler(this, job);
    }
}

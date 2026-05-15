using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace LabelDesigner.Services;

/// <summary>
/// Named-pipe server that listens for LabelJob requests from JaneERP (or any caller).
/// Pipe name: LabelDesigner
///
/// From JaneERP, send a JSON-serialized LabelJob to \\.\pipe\LabelDesigner.
/// The server deserializes it and raises the JobReceived event on a thread-pool thread;
/// the handler (MainViewModel.HandlePrintJob) dispatches to the UI thread.
/// </summary>
public class IpcServer : IDisposable
{
    public const string PipeName = "LabelDesigner";

    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public event EventHandler<LabelJob>? JobReceived;

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
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(pipe);
                var json = await reader.ReadToEndAsync(ct);

                if (string.IsNullOrWhiteSpace(json)) continue;

                var job = JsonSerializer.Deserialize<LabelJob>(json, TemplateService.JsonOptions);
                if (job != null) JobReceived?.Invoke(this, job);
            }
            catch (OperationCanceledException) { break; }
            catch { /* pipe disconnected or bad JSON — keep listening */ }
        }
    }
}

namespace LabelDesigner.Services;

/// <summary>Thrown when a label fails pre-print validation (e.g. a barcode value can't encode).
/// Carries every problem so the operator sees all of them at once instead of one-at-a-time.</summary>
public class LabelValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public LabelValidationException(IReadOnlyList<string> errors)
        : base("This label cannot be printed:" + Environment.NewLine +
               string.Join(Environment.NewLine, errors.Select(e => "  • " + e)))
    {
        Errors = errors;
    }
}

/// <summary>Thrown when a specifically-requested printer is missing and silent fallback was disallowed
/// (e.g. an unattended JaneERP job must NOT divert to the default printer / PDF).</summary>
public class PrinterNotFoundException : Exception
{
    public string PrinterName { get; }

    public PrinterNotFoundException(string printerName)
        : base($"Printer '{printerName}' was not found and fallback to the default printer is disabled.")
    {
        PrinterName = printerName;
    }
}

/// <summary>Thrown when the target printer reports a hard problem (offline, paper out/jam, door open,
/// error) before/at print time, so a job isn't reported "printed" when no label came out.</summary>
public class PrinterOfflineException : Exception
{
    public string PrinterName { get; }
    public string Status { get; }

    public PrinterOfflineException(string printerName, string status)
        : base($"Printer '{printerName}' is not ready to print (status: {status}). Fix the printer and try again.")
    {
        PrinterName = printerName;
        Status = status;
    }
}

/// <summary>Thrown when a CONTINUOUS serial source's counter store can't be reached (e.g. the shared
/// folder is offline). We refuse to print rather than silently fall back to the start value, which
/// would mint duplicate serial numbers.</summary>
public class SerialStoreUnavailableException : Exception
{
    public string StorePath { get; }

    public SerialStoreUnavailableException(string storePath)
        : base($"The serial-counter store could not be reached:\n\n    {storePath}\n\n" +
               "No labels were printed (to avoid duplicate serial numbers). Check that the shared " +
               "folder is online and writable, or switch to Local storage in File ▸ Settings.")
    {
        StorePath = storePath;
    }
}

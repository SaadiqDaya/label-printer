namespace LabelDesigner.Core.Models;

public enum ThermalMediaType { GapLabel, BlackMark, Continuous }

/// <summary>How the label is sent to the printer. Gdi = rasterise the WPF visual through the Windows
/// driver (default, works everywhere). Zpl = emit native ZPL II for Zebra printers (printer-engine
/// barcodes, tiny jobs, no driver dependence) — sent raw to the queue or over TCP 9100.</summary>
public enum PrintBackend { Gdi, Zpl }

/// <summary>
/// Thermal/Zebra print settings stored with the template so quality is reproducible across
/// shifts and workstations. Null numeric values mean "leave the printer/driver default alone".
/// Darkness and speed are the knobs that decide whether bars are crisp or bleed on a given stock.
/// </summary>
public class PrinterProfile
{
    /// <summary>Preferred printer (queue) name. Operators can still override at print time.</summary>
    public string? PrinterName { get; set; }

    /// <summary>Darkness / burn temperature (Zebra ~0..30). Null = leave driver default.</summary>
    public int? Darkness { get; set; }

    /// <summary>Print speed in inches/sec (Zebra ~2..14). Null = leave driver default.</summary>
    public double? SpeedIps { get; set; }

    public ThermalMediaType MediaType { get; set; } = ThermalMediaType.GapLabel;

    /// <summary>Label home/registration offset in millimetres applied to the whole label.</summary>
    public double LabelOffsetXMm { get; set; }
    public double LabelOffsetYMm { get; set; }

    /// <summary>Output backend. Default Gdi (raster). Zpl emits native ZPL II for Zebra printers.</summary>
    public PrintBackend OutputMode { get; set; } = PrintBackend.Gdi;

    /// <summary>For ZPL: when set, send raw to this TCP host (printer IP); empty = send raw to the Windows queue.</summary>
    public string? NetworkHost { get; set; }
    public int NetworkPort { get; set; } = 9100;
}

using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace LabelDesigner.Services;

/// <summary>
/// Sends raw bytes (e.g. ZPL) to a printer — either to a Windows print queue via winspool using the
/// RAW datatype (bypasses GDI rendering), or directly to a network printer over TCP (port 9100).
/// </summary>
public static class RawPrinter
{
    public static void SendToTcp(string host, int port, byte[] data)
    {
        using var client = new TcpClient();
        client.Connect(host, port);
        using var ns = client.GetStream();
        ns.Write(data, 0, data.Length);
        ns.Flush();
    }

    public static void SendToQueue(string printerName, string docName, byte[] data)
    {
        if (!OpenPrinter(printerName, out var h, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"OpenPrinter('{printerName}') failed");
        try
        {
            var di = new DOCINFOA { pDocName = docName, pDataType = "RAW" };
            if (StartDocPrinter(h, 1, ref di) == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "StartDocPrinter failed");
            try
            {
                if (!StartPagePrinter(h))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "StartPagePrinter failed");

                var ptr = Marshal.AllocHGlobal(data.Length);
                try
                {
                    Marshal.Copy(data, 0, ptr, data.Length);
                    if (!WritePrinter(h, ptr, data.Length, out _))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "WritePrinter failed");
                }
                finally { Marshal.FreeHGlobal(ptr); }
                EndPagePrinter(h);
            }
            finally { EndDocPrinter(h); }
        }
        finally { ClosePrinter(h); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool OpenPrinter(string src, out IntPtr hPrinter, IntPtr pd);
    [DllImport("winspool.Drv", SetLastError = true)] private static extern bool ClosePrinter(IntPtr hPrinter);
    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int StartDocPrinter(IntPtr hPrinter, int level, ref DOCINFOA di);
    [DllImport("winspool.Drv", SetLastError = true)] private static extern bool EndDocPrinter(IntPtr hPrinter);
    [DllImport("winspool.Drv", SetLastError = true)] private static extern bool StartPagePrinter(IntPtr hPrinter);
    [DllImport("winspool.Drv", SetLastError = true)] private static extern bool EndPagePrinter(IntPtr hPrinter);
    [DllImport("winspool.Drv", SetLastError = true)] private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);
}

using System.Windows;
using System.Windows.Threading;
using LabelDesigner.Services;

namespace LabelDesigner;

/// <summary>
/// Interaction logic for App.xaml.
/// Registers global exception handlers so a single bad job / driver hiccup cannot crash the
/// always-on print listener — it logs, tells the operator, and keeps running.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Launch mode: "--operator" / "--printstation" opens the read-only shop-floor Print Station
        // (no designer, no Save → master templates can't be corrupted). Default opens the designer.
        bool operatorMode = e.Args.Any(a =>
            a.Equals("--operator", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--printstation", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/operator", StringComparison.OrdinalIgnoreCase));

        LogService.Info($"LabelDesigner started ({(operatorMode ? "Print Station" : "Designer")} mode).");

        Window main = operatorMode ? new Views.PrintStationWindow() : new Views.MainWindow();
        MainWindow = main;
        main.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.Error("Unhandled UI exception.", e.Exception);
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\n" +
            $"The application will keep running. Details were logged to:\n{LogService.Directory}",
            "Label Designer", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // keep the listener alive
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) LogService.Error("Unhandled AppDomain exception.", ex);
        else LogService.Error("Unhandled AppDomain exception (non-CLR object).");
    }

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        LogService.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }
}

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace _HoldSense;

internal static class Program
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogException("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogException("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        WinRT.ComWrappersSupport.InitializeComWrappers();

        try
        {
            Application.Start(_ =>
            {
                var syncContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(syncContext);

                try
                {
                    new WinUIApplication();
                }
                catch (Exception ex)
                {
                    LogException("Program.ApplicationStart.NewWinUIApplication", ex);
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            LogException("Program.ApplicationStart", ex);
            throw;
        }
    }

    internal static void LogException(string source, Exception? ex)
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var logPath = Path.Combine(baseDir, "startup-error.log");
            var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(logPath, message);
        }
        catch
        {
        }
    }
}

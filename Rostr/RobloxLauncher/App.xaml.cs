using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Threading;
using RobloxLauncher.Core;
using RobloxLauncher.Settings;

namespace RobloxLauncher;

public partial class App : Application
{
    private const string MutexName = "MyRobloxLauncher_SingleInstance";
    private const string PipeName = "MyRobloxLauncher_Pipe";
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Started by the update window (Rostr-update.cmd): no window, no single-instance check.
        // Do the job (--uninstall or --install) and exit.
        if (e.Args.Contains("--uninstall") || e.Args.Contains("--install"))
        {
            var code = Updater.RunSetupCommand(e.Args);
            Environment.ExitCode = code;
            Shutdown(code);
            return;
        }

        bool isFirst;
        if (e.Args.Contains("--updated"))
        {
            // Started by the updater: the old copy is still shutting down and holds the mutex.
            _mutex = new Mutex(false, MutexName);
            try { isFirst = _mutex.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { isFirst = true; }
        }
        else
        {
            _mutex = new Mutex(true, MutexName, out isFirst);
        }

        if (!isFirst)
        {
            var link = e.Args.FirstOrDefault(a =>
                a.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase));
            SendToRunningInstance(link ?? "show:" + Updater.CurrentVersion);

            // This exe is newer than the Rostr that ran last (for example a fresh download while the old
            // one sits in the tray): that one closes itself, so wait for it and take over.
            if (link == null && !e.Args.Contains("--updated") &&
                Version.TryParse(AppSettings.Current.LastRunVersion, out var running) &&
                running < Updater.CurrentVersion)
            {
                try { isFirst = _mutex.WaitOne(TimeSpan.FromSeconds(6)); }
                catch (AbandonedMutexException) { isFirst = true; }
            }

            if (!isFirst)
            {
                Shutdown();
                return;
            }
        }

        // Before anything touches the data folder: bring over data saved under the old folder name.
        DataMigration.Run();

        ThemeManager.Apply(AppSettings.Current.Theme);
        Loc.Set(Loc.Resolve(AppSettings.Current.Language));

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI error", args.Exception);
            MessageBox.Show(
                "Something went wrong:\n\n" + args.Exception.Message +
                "\n\nDetails were saved to:\n" + Log.FilePath,
                "Rostr", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Fatal error", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Background task error", args.Exception);
            args.SetObserved();
        };

        Log.Info($"Launcher {Updater.CurrentVersion} started");
        Updater.CleanupAfterUpdate();
        CleanupStrayLoginProfiles();
        _ = ListenAsync();
    }

    // The login window uses a throwaway browser profile in %TEMP%. If the launcher was killed while
    // it was open, that folder (which holds a fresh login cookie) is left behind. Only the first
    // instance gets here, and the login window cannot be open yet, so anything found is stale.
    private static void CleanupStrayLoginProfiles()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var leftovers = Directory.GetDirectories(Path.GetTempPath(), "Rostr_login_*")
                    .Concat(Directory.GetDirectories(Path.GetTempPath(), "MyRobloxLauncher_login_*"));
                foreach (var dir in leftovers)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        Log.Info("Removed leftover login profile " + Path.GetFileName(dir));
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Could not remove " + dir + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Login profile cleanup failed", ex);
            }
        });
    }

    private static void SendToRunningInstance(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(message);
        }
        catch { }
    }

    private async Task ListenAsync()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync();

                if (!string.IsNullOrEmpty(line))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (Current.MainWindow is RobloxLauncher.MainWindow w) w.HandleMessage(line);
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Pipe listener: " + ex.Message);
                await Task.Delay(200);
            }
        }
    }
}
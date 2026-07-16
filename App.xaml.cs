using GpxManager.ViewModels;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Windows;

namespace GpxManager;

public partial class App : Application
{
    private Mutex? _mutex;
    private const string AppId     = "GpxManager-1F4A7E2B";
    private const string MutexName = "Local\\" + AppId;
    private const string PipeName  = AppId;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool isFirst);

        if (!isFirst)
        {
            if (e.Args.Length > 0)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    pipe.Connect(2000);
                    using var writer = new StreamWriter(pipe) { AutoFlush = true };
                    foreach (var arg in e.Args)
                        writer.WriteLine(arg);
                }
                catch { }
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);

        StartPipeServer();

        if (e.Args.Length > 0 && MainWindow?.DataContext is MainViewModel vm)
            vm.LoadFiles(e.Args);
    }

    private void StartPipeServer()
    {
        new Thread(PipeServerLoop) { IsBackground = true }.Start();
    }

    private void PipeServerLoop()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                server.WaitForConnection();
                using var reader = new StreamReader(server);
                var paths = new List<string>();
                while (reader.ReadLine() is { } line)
                    paths.Add(line);

                if (paths.Count > 0)
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (MainWindow?.DataContext is MainViewModel vm)
                            vm.LoadFiles(paths);
                        if (MainWindow != null)
                        {
                            if (MainWindow.WindowState == WindowState.Minimized)
                                MainWindow.WindowState = WindowState.Normal;
                            MainWindow.Activate();
                        }
                    });
            }
            catch { }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        if (MainWindow?.DataContext is not MainViewModel vm) return;
        var dirty = vm.Tabs.Where(t => t.IsDirty).ToList();
        if (dirty.Count == 0) return;

        var names = string.Join("\n", dirty.Select(t => $"  • {t.File.FileName}"));
        var result = MessageBox.Show(
            $"Les fichiers suivants ont des modifications non sauvegardées :\n\n{names}\n\nQuitter quand même ?",
            "Modifications non sauvegardées",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
            e.Cancel = true;
    }
}

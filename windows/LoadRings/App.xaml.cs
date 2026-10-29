using System.Threading;
using System.Windows;

namespace LoadRings;

public partial class App : Application
{
    // Held for the app's lifetime so a second launch can see the first one.
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "LoadRings.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }
        new MainWindow().Show();
    }
}

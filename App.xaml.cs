using System.Windows;
using MovieMaker.Services;

namespace MovieMaker;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SettingsService.Load();
    }
}

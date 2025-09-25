using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ValheimLauncher2.ViewModels;
using ValheimLauncher2.Views;

namespace ValheimLauncher2;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 1. Erstelle das Hauptfenster
            var mainWindow = new MainWindow();

            // 2. Erstelle das ViewModel und übergib ihm das Fenster,
            //    damit es weiß, wo es z.B. Dialoge öffnen soll.
            var viewModel = new MainViewModel(mainWindow);

            // 3. Setze das ViewModel als Datenkontext für das Fenster.
            mainWindow.DataContext = viewModel;

            // 4. Weise das fertig konfigurierte Fenster der Anwendung zu.
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}


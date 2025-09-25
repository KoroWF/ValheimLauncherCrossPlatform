using Avalonia.Controls;
using Avalonia.Input;
using ValheimLauncher2.ViewModels;

namespace ValheimLauncher2.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel(this);

            // Hier wird der Event-Handler im Code zugewiesen
            this.PointerPressed += Window_PointerPressed;
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Startet den Drag-Vorgang für das Fenster, wenn die Maustaste gedrückt wird.
            this.BeginMoveDrag(e);
        }
    }
}
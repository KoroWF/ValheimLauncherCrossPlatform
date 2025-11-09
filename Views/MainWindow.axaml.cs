using Avalonia.Controls;
using Avalonia.Input;
using ValheimLauncher2.ViewModels;

namespace ValheimLauncher2.Views
{
    /// <summary>
    /// Represents the main application window.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel(this);
            this.PointerPressed += Window_PointerPressed;
        }

        /// <summary>
        /// Handles the pointer pressed event to enable window dragging.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The pointer event arguments.</param>
        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            this.BeginMoveDrag(e);
        }
    }
}   
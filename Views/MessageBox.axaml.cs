using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ValheimCrossPlatformLauncher
{
    public partial class MessageBox : Window
    {
        public MessageBox()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public static async void Show(Window parent, string message)
        {
            var dialog = new MessageBox();
            dialog.MessageBlock.Text = message;
            await dialog.ShowDialog(parent);
        }

        private void OnOKClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
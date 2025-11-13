using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ValheimCrossPlatformLauncher
{
    /// <summary>
    /// Represents a simple message box dialog window.
    /// </summary>
    public partial class MessageBox : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MessageBox"/> class.
        /// </summary>
        public MessageBox()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Displays the message box dialog with the specified message.
        /// </summary>
        /// <param name="parent">The parent window for the dialog.</param>
        /// <param name="message">The message to display in the dialog.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task Show(Window parent, string message)
        {
            var dialog = new MessageBox();
            dialog.MessageBlock.Text = message;
            await dialog.ShowDialog(parent);
        }

        /// <summary>
        /// Handles the click event for the OK button and closes the dialog.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnOKClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
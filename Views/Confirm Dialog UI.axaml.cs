using System.Threading.Tasks;
using Avalonia.Controls;

namespace ValheimCrossPlatformLauncher
{
    public partial class ConfirmDialog : Window
    {
        public enum DialogResult
        {
            Yes,
            No
        }

        public DialogResult Result { get; private set; } = DialogResult.No;

        public ConfirmDialog()
        {
            InitializeComponent();
            var yesButton = this.FindControl<Button>("YesButton");
            var noButton = this.FindControl<Button>("NoButton");

            yesButton.Click += (_, __) => { Result = DialogResult.Yes; Close(); };
            noButton.Click += (_, __) => { Result = DialogResult.No; Close(); };
        }

        public static async Task<DialogResult> Show(Window parent, string title, string message)
        {
            var dialog = new ConfirmDialog
            {
                Title = title
            };
            dialog.FindControl<TextBlock>("MessageBlock").Text = message;

            // ShowDialog returns a Task that completes when the dialog is closed.
            await dialog.ShowDialog(parent);

            // After it's closed, the Result property will be set.
            return dialog.Result;
        }
    }
}

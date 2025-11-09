using System.Threading.Tasks;
using Avalonia.Controls;

namespace ValheimCrossPlatformLauncher
{
 /// <summary>
 /// Represents a confirmation dialog window with Yes and No options.
 /// </summary>
 public partial class ConfirmDialog : Window
 {
 /// <summary>
 /// Specifies the possible results of the confirmation dialog.
 /// </summary>
 public enum DialogResult
 {
 Yes,
 No
 }

 /// <summary>
 /// Gets the result of the dialog after it is closed.
 /// </summary>
 public DialogResult Result { get; private set; } = DialogResult.No;

 /// <summary>
 /// Initializes a new instance of the <see cref="ConfirmDialog"/> class.
 /// </summary>
 public ConfirmDialog()
 {
 InitializeComponent();
 var yesButton = this.FindControl<Button>("YesButton");
 var noButton = this.FindControl<Button>("NoButton");

 yesButton.Click += (_, __) => { Result = DialogResult.Yes; Close(); };
 noButton.Click += (_, __) => { Result = DialogResult.No; Close(); };
 }

 /// <summary>
 /// Displays the confirmation dialog with the specified title and message.
 /// </summary>
 /// <param name="parent">The parent window for the dialog.</param>
 /// <param name="title">The title of the dialog window.</param>
 /// <param name="message">The message to display in the dialog.</param>
 /// <returns>A task that represents the asynchronous operation. The result indicates the user's choice.</returns>
 public static async Task<DialogResult> Show(Window parent, string title, string message)
 {
 var dialog = new ConfirmDialog
 {
 Title = title
 };
 dialog.FindControl<TextBlock>("MessageBlock").Text = message;
 await dialog.ShowDialog(parent);
 return dialog.Result;
 }
 }
}

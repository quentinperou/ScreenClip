using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenClip;

/// <summary>
/// A question or a message in the editor itself, in the look of Windows 11's dialogs (asked by the author: the classic
/// message box did not belong): a card over the greyed window, its title, its text, and its buttons, the first one in
/// the accent color. Enter takes the first button, Escape the last one.
/// </summary>
public partial class EditorWindow
{
    private TaskCompletionSource<int>? _dialog;
    private int _dialogCancel;

    /// <summary>
    /// Shows the dialog and gives the index of the button pressed (Escape: the last one). A title may be empty.
    /// </summary>
    private Task<int> AskAsync(string title, string text, params string[] buttons)
    {
        _dialog?.TrySetResult(_dialogCancel);
        var dialog = new TaskCompletionSource<int>();
        _dialog = dialog;
        _dialogCancel = buttons.Length - 1;
        DialogTitle.Text = title;
        DialogTitle.Visibility = title.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DialogText.Text = text;
        DialogButtons.Children.Clear();
        DialogButtons.Columns = buttons.Length;
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var button = new Button
            {
                Content = buttons[i],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(i == 0 ? 0 : 4, 0, i == buttons.Length - 1 ? 0 : 4, 0),
                Padding = new Thickness(12, 6, 12, 6),
            };
            if (i == 0)
                button.Style = (Style)FindResource("AccentButtonStyle");
            button.Click += (_, _) => CloseDialog(index);
            DialogButtons.Children.Add(button);
        }
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        DialogOverlay.Visibility = Visibility.Visible;
        Activate();
        Dispatcher.BeginInvoke(() => (DialogButtons.Children[0] as Button)?.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        return dialog.Task;
    }

    /// <summary>A message with one button, OK.</summary>
    private Task ShowMessageAsync(string text) => AskAsync("", text, Strings.Get("Ok"));

    private void CloseDialog(int answer)
    {
        DialogOverlay.Visibility = Visibility.Collapsed;
        var dialog = _dialog;
        _dialog = null;
        dialog?.TrySetResult(answer);
    }

    /// <summary>While a dialog is shown, its keys only: Enter, the first button; Escape, the last.</summary>
    private bool DialogKey(KeyEventArgs e)
    {
        if (_dialog is null)
            return false;
        if (e.Key == Key.Escape)
            CloseDialog(_dialogCancel);
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button)
            CloseDialog(0);
        else if (e.Key is Key.Tab or Key.Left or Key.Right or Key.Space or Key.Enter)
            return false; // moving between the buttons, pressing the one focused
        return true;
    }
}

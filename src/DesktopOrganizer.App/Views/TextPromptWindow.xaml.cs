namespace DesktopOrganizer.Views;

public partial class TextPromptWindow : System.Windows.Window
{
    public TextPromptWindow(string title, string prompt, string initialValue)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueTextBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueTextBox.Focus();
            var extensionStart = initialValue.LastIndexOf('.');
            ValueTextBox.Select(0, extensionStart > 0 ? extensionStart : initialValue.Length);
        };
    }

    public string Value => ValueTextBox.Text.Trim();

    private void OnConfirmClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Value.Length == 0)
        {
            return;
        }

        DialogResult = true;
    }
}

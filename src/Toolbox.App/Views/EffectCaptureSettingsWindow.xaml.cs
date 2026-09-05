using System.Windows;
using Toolbox.Core;

namespace PersonalToolbox.Views;

public partial class EffectCaptureSettingsWindow : Window
{
    private readonly EffectCaptureSettings _settings;

    public EffectCaptureSettingsWindow(EffectCaptureSettings settings, string defaultOutputRoot)
    {
        InitializeComponent();
        _settings = settings;
        DurationBox.SelectedIndex = settings.DefaultDurationSeconds switch { 5 => 1, 7 => 2, _ => 0 };
        CountdownBox.IsChecked = settings.CountdownEnabled;
        OutputRootBox.Text = settings.OutputRoot ?? defaultOutputRoot;
        HotkeyBox.SelectedIndex = settings.GlobalHotkey switch
        {
            "Ctrl+Alt+R" => 1,
            "Ctrl+Shift+R" => 2,
            _ => 0,
        };
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (DurationBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var duration))
        {
            _settings.DefaultDurationSeconds = duration;
        }

        _settings.CountdownEnabled = CountdownBox.IsChecked == true;
        _settings.OutputRoot = string.IsNullOrWhiteSpace(OutputRootBox.Text) ? null : OutputRootBox.Text.Trim();
        _settings.GlobalHotkey = HotkeyBox.SelectedItem is System.Windows.Controls.ComboBoxItem hotkeyItem
            ? hotkeyItem.Tag?.ToString()
            : null;
        _settings.GlobalHotkeyConfigured = true;
        _settings.Normalize();
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}

using System.Windows;
using MouseRing.Core;
using MouseRing.Services;

namespace MouseRing.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings, bool isHosted = false)
    {
        InitializeComponent();
        _settings = settings;

        var options = ActionCatalog.OptionsFor(isHosted);
        UpActionBox.ItemsSource = options;
        RightActionBox.ItemsSource = options;
        DownActionBox.ItemsSource = options;
        LeftActionBox.ItemsSource = options;

        UpActionBox.SelectedValue = settings.UpAction;
        RightActionBox.SelectedValue = settings.RightAction;
        DownActionBox.SelectedValue = settings.DownAction;
        LeftActionBox.SelectedValue = settings.LeftAction;
        DelaySlider.Value = settings.TriggerDelayMs;
        SizeSlider.Value = settings.MenuDiameter;
        StartupCheckBox.IsChecked = settings.RunAtStartup;
        FullScreenCheckBox.IsChecked = settings.DisableInFullScreen;
        if (isHosted)
        {
            StartupCheckBox.Visibility = Visibility.Collapsed;
        }
    }

    public AppSettings Result => _settings;

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.UpAction = SelectedAction(UpActionBox);
        _settings.RightAction = SelectedAction(RightActionBox);
        _settings.DownAction = SelectedAction(DownActionBox);
        _settings.LeftAction = SelectedAction(LeftActionBox);
        _settings.TriggerDelayMs = (int)Math.Round(DelaySlider.Value);
        _settings.MenuDiameter = SizeSlider.Value;
        _settings.RunAtStartup = StartupCheckBox.IsChecked == true;
        _settings.DisableInFullScreen = FullScreenCheckBox.IsChecked == true;
        _settings.Normalize();
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void DelaySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DelayValueText is not null)
        {
            DelayValueText.Text = $"{Math.Round(e.NewValue):0} ms";
        }
    }

    private void SizeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SizeValueText is not null)
        {
            SizeValueText.Text = $"{Math.Round(e.NewValue):0} px";
        }
    }

    private static ActionKind SelectedAction(System.Windows.Controls.ComboBox comboBox) =>
        comboBox.SelectedValue is ActionKind action ? action : ActionKind.None;
}

using System.Windows;
using Toolbox.Core;

namespace PersonalToolbox.Views;

public partial class CueSettingsWindow : Window
{
    private readonly CueSettings _settings;
    private readonly Action _apply;

    public CueSettingsWindow(CueSettings settings, Action apply)
    {
        InitializeComponent();
        _settings = settings;
        _apply = apply;
        FocusEnabled.IsChecked = settings.FocusHoldEnabled;
        ZoomSlider.Value = settings.ZoomFactor;
        HoldSlider.Value = settings.HoldThresholdMilliseconds;
        AnimationSlider.Value = settings.AnimationMilliseconds;
        FollowCursor.IsChecked = settings.FollowCursor;
        SpotlightRadius.Value = settings.SpotlightRadius;
        MagnifierZoom.Value = settings.MagnifierZoom;
        MagnifierRadius.Value = settings.MagnifierRadius;
        RectSpotlight.IsChecked = settings.SpotlightShape == "RoundedRectangle";
        RectMagnifier.IsChecked = settings.MagnifierShape == "RoundedRectangle";
        ZoomSlider.ValueChanged += (_, _) => RefreshLabels();
        HoldSlider.ValueChanged += (_, _) => RefreshLabels();
        AnimationSlider.ValueChanged += (_, _) => RefreshLabels();
        MagnifierRadius.ValueChanged += (_, _) => RefreshLabels();
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        ZoomValue.Text = $"{ZoomSlider.Value:0.00}×";
        HoldValue.Text = $"{HoldSlider.Value:0} ms";
        AnimationValue.Text = $"{AnimationSlider.Value:0} ms";
        MagnifierSizeValue.Text = $"基础直径 {MagnifierRadius.Value * 2:0}（随屏幕缩放）";
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.FocusHoldEnabled = FocusEnabled.IsChecked == true;
        _settings.ZoomFactor = ZoomSlider.Value;
        _settings.HoldThresholdMilliseconds = (int)Math.Round(HoldSlider.Value);
        _settings.AnimationMilliseconds = (int)Math.Round(AnimationSlider.Value);
        _settings.FollowCursor = FollowCursor.IsChecked == true;
        _settings.SpotlightRadius = SpotlightRadius.Value;
        _settings.MagnifierZoom = MagnifierZoom.Value;
        _settings.MagnifierRadius = MagnifierRadius.Value;
        _settings.SpotlightShape = RectSpotlight.IsChecked == true ? "RoundedRectangle" : "Circle";
        _settings.MagnifierShape = RectMagnifier.IsChecked == true ? "RoundedRectangle" : "Circle";
        _apply();
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => Close();
}

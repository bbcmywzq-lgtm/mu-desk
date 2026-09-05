using System.Windows;
using PersonalTools.Core;

namespace PersonalTools.App.Views;

public enum ReminderAlertAction
{
    Dismiss,
    Complete,
    SnoozeTenMinutes,
    SnoozeOneHour,
    Open,
}

public partial class ReminderAlertWindow : Window
{
    public ReminderAlertWindow(EntryItem entry)
    {
        Entry = entry;
        InitializeComponent();
        ContentText.Text = entry.Content;
        Loaded += (_, _) => PlaceAtWorkAreaCorner();
    }

    public EntryItem Entry { get; }

    public event Action<ReminderAlertAction>? ActionSelected;

    private void Select(ReminderAlertAction action)
    {
        ActionSelected?.Invoke(action);
        Close();
    }

    private void OnDismissClick(object sender, RoutedEventArgs e) => Select(ReminderAlertAction.Dismiss);
    private void OnCompleteClick(object sender, RoutedEventArgs e) => Select(ReminderAlertAction.Complete);
    private void OnSnoozeTenClick(object sender, RoutedEventArgs e) => Select(ReminderAlertAction.SnoozeTenMinutes);
    private void OnSnoozeHourClick(object sender, RoutedEventArgs e) => Select(ReminderAlertAction.SnoozeOneHour);
    private void OnOpenClick(object sender, RoutedEventArgs e) => Select(ReminderAlertAction.Open);

    private void PlaceAtWorkAreaCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = area.Bottom - ActualHeight - 16;
    }
}

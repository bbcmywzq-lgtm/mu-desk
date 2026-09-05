using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using DesktopOrganizer.Core.Models;

namespace DesktopOrganizer.Views;

public partial class SettingsWindow : System.Windows.Window
{
    private readonly ObservableCollection<OrganizationRule> _rules;
    private readonly IReadOnlyList<ZoneLayout> _targetZones;
    private readonly Action _exportBackup;
    private readonly Func<bool> _importBackup;

    public SettingsWindow(
        AppSettings settings,
        IReadOnlyCollection<ZoneLayout> zones,
        IReadOnlyCollection<OrganizationRule> rules,
        Action exportBackup,
        Func<bool> importBackup,
        bool isHosted = false)
    {
        InitializeComponent();

        Settings = settings.Clone();
        _exportBackup = exportBackup;
        _importBackup = importBackup;
        _targetZones = zones
            .Where(zone => zone.Kind == ZoneKind.Desktop && !zone.IsInbox)
            .Select(zone => zone.Clone())
            .ToArray();
        _rules = new ObservableCollection<OrganizationRule>(rules.Select(rule => rule.Clone()));

        AutoApplyRulesCheckBox.IsChecked = Settings.AutoApplyRules;
        ShowToolbarCheckBox.IsChecked = Settings.ShowCommandToolbar;
        HideNativeIconsCheckBox.IsChecked = Settings.HideNativeDesktopIcons;
        LaunchAtStartupCheckBox.IsChecked = Settings.LaunchAtStartup;
        if (isHosted)
        {
            LaunchAtStartupCheckBox.Visibility = System.Windows.Visibility.Collapsed;
        }

        MatchKindComboBox.ItemsSource = new[]
        {
            new RuleMatchOption(RuleMatchKind.Extension, "文件扩展名"),
            new RuleMatchOption(RuleMatchKind.NameContains, "名称包含"),
        };
        TargetZoneComboBox.ItemsSource = _targetZones;
        RulesList.ItemsSource = _rules;
        RulesList.SelectedIndex = _rules.Count > 0 ? 0 : -1;
        UpdateEditorState();
    }

    public AppSettings Settings { get; private set; }

    public IReadOnlyList<OrganizationRule> Rules => _rules.Select(rule => rule.Clone()).ToArray();

    private void OnAddRuleClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_targetZones.Count == 0)
        {
            System.Windows.MessageBox.Show(
                this,
                "请先在桌面上新建一个目标分区。",
                "无法新增规则",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var rule = new OrganizationRule
        {
            Name = $"新规则 {_rules.Count + 1}",
            MatchKind = RuleMatchKind.Extension,
            Pattern = ".pdf",
            TargetZoneId = _targetZones[0].Id,
            Priority = _rules.Count,
        };
        _rules.Add(rule);
        RulesList.SelectedItem = rule;
        RuleNameTextBox.Focus();
        RuleNameTextBox.SelectAll();
    }

    private void OnDeleteRuleClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not OrganizationRule rule)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            $"删除规则“{rule.Name}”？",
            "删除规则",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.No);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var selectedIndex = RulesList.SelectedIndex;
        _rules.Remove(rule);
        RulesList.SelectedIndex = Math.Min(selectedIndex, _rules.Count - 1);
        UpdateEditorState();
    }

    private void OnRuleSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RulesList.SelectedItem is OrganizationRule rule)
        {
            RuleNameTextBox.Text = rule.Name;
            RuleEnabledCheckBox.IsChecked = rule.IsEnabled;
            MatchKindComboBox.SelectedItem = MatchKindComboBox.Items
                .Cast<RuleMatchOption>()
                .First(option => option.Kind == rule.MatchKind);
            PatternTextBox.Text = rule.Pattern;
            TargetZoneComboBox.SelectedItem = _targetZones.FirstOrDefault(zone => zone.Id == rule.TargetZoneId);
        }

        UpdateEditorState();
    }

    private void OnUpdateRuleClick(object sender, System.Windows.RoutedEventArgs e)
    {
        CommitSelectedRule(showValidation: true);
    }

    private void OnOpenDataDirectoryClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Grid · 桌面整理 · MU Desk");
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private void OnExportBackupClick(object sender, System.Windows.RoutedEventArgs e) => _exportBackup();

    private void OnImportBackupClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_importBackup())
        {
            Close();
        }
    }

    private void OnSaveClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not null && !CommitSelectedRule(showValidation: true))
        {
            return;
        }

        Settings = new AppSettings
        {
            AutoApplyRules = AutoApplyRulesCheckBox.IsChecked == true,
            ShowCommandToolbar = ShowToolbarCheckBox.IsChecked == true,
            HideNativeDesktopIcons = HideNativeIconsCheckBox.IsChecked == true,
            LaunchAtStartup = LaunchAtStartupCheckBox.IsChecked == true,
            HasConfirmedRealFileMoves = Settings.HasConfirmedRealFileMoves,
        };
        DialogResult = true;
    }

    private bool CommitSelectedRule(bool showValidation)
    {
        if (RulesList.SelectedItem is not OrganizationRule rule)
        {
            return true;
        }

        var name = RuleNameTextBox.Text.Trim();
        var pattern = PatternTextBox.Text.Trim();
        var target = TargetZoneComboBox.SelectedItem as ZoneLayout;
        var matchOption = MatchKindComboBox.SelectedItem as RuleMatchOption;
        if (name.Length == 0 || pattern.Length == 0 || target is null || matchOption is null)
        {
            if (showValidation)
            {
                System.Windows.MessageBox.Show(
                    this,
                    "请填写规则名称、匹配内容并选择目标分区。",
                    "规则不完整",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }

            return false;
        }

        rule.Name = name;
        rule.Pattern = pattern;
        rule.TargetZoneId = target.Id;
        rule.MatchKind = matchOption.Kind;
        rule.IsEnabled = RuleEnabledCheckBox.IsChecked == true;
        RulesList.Items.Refresh();
        return true;
    }

    private void UpdateEditorState()
    {
        RuleEditor.IsEnabled = RulesList.SelectedItem is not null;
    }

    private sealed record RuleMatchOption(RuleMatchKind Kind, string DisplayName);
}

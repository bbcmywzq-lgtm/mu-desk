using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Toolbox.Core;
using WpfButton = System.Windows.Controls.Button;
using WpfBrush = System.Windows.Media.Brush;
using WpfImage = System.Windows.Controls.Image;
using WpfMessageBox = System.Windows.MessageBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace PersonalToolbox.Views;

public partial class CispQuestionBankWindow : Window
{
    private readonly string _cachePath;
    private readonly CispProgressStore _progressStore;
    private readonly string _bundledBankPath;
    private readonly string _imageRoot;
    private readonly string _pdfRoot;
    private readonly string _questionSetsPath;
    private readonly Random _random = new();
    private readonly Dictionary<int, Border> _optionBorders = [];
    private CispStudyProgress _progress;
    private IReadOnlyList<CispQuestion> _allQuestions = [];
    private IReadOnlyList<CispQuestionSet> _questionSets = [];
    private CispQuestionSet? _activeSet;
    private List<CispQuestion> _filteredQuestions = [];
    private List<CispQuestion> _sessionQuestions = [];
    private int _sessionIndex;
    private int _sessionAttemptCount;
    private int _sessionCorrectCount;
    private CispQuestion? _currentQuestion;
    private int? _selectedIndex;
    private bool _loaded;
    private bool _requestedSetMode;
    private string _generalSourceState = "使用随应用附带的离线题源";

    public CispQuestionBankWindow(
        string cachePath,
        CispProgressStore progressStore,
        string bundledBankPath,
        string imageRoot,
        string pdfRoot,
        string questionSetsPath,
        bool startInSetMode = false)
    {
        InitializeComponent();
        _cachePath = cachePath;
        _progressStore = progressStore;
        _bundledBankPath = bundledBankPath;
        _imageRoot = imageRoot;
        _pdfRoot = pdfRoot;
        _questionSetsPath = questionSetsPath;
        _requestedSetMode = startInSetMode;
        _progress = _progressStore.Load();
    }

    public void SelectPracticeMode(bool latestSets)
    {
        _requestedSetMode = latestSets;
        if (_loaded && PracticeModePicker.Items.Count >= 2)
        {
            PracticeModePicker.SelectedIndex = latestSets ? 1 : 0;
        }
    }

    private bool IsSetMode => PracticeModePicker.SelectedIndex == 1;

    private IReadOnlyList<CispQuestion> GetActiveQuestions()
    {
        _activeSet = IsSetMode ? SetPicker.SelectedItem as CispQuestionSet : null;
        return _activeSet?.Questions ?? _allQuestions;
    }

    private IReadOnlyList<CispQuestion> GetAllKnownQuestions() =>
        _allQuestions.Concat(_questionSets.SelectMany(set => set.Questions)).ToList();

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        PracticeModePicker.Items.Add("综合题库（907题）");
        PracticeModePicker.Items.Add("2026 最新八套题");
        DomainPicker.Items.Add("全部类别");
        foreach (var domain in CispQuestionBank.Domains)
        {
            DomainPicker.Items.Add(domain);
        }

        DomainPicker.SelectedIndex = 0;
        SessionSizeBox.Text = _progress.SessionSize.ToString();
        _loaded = true;
        await LoadBankAsync();
    }

    private async Task LoadBankAsync()
    {
        BusyOverlay.Visibility = Visibility.Visible;
        ErrorBanner.Visibility = Visibility.Collapsed;
        try
        {
            var result = await CispQuestionBank.LoadAsync(_cachePath, _bundledBankPath);
            _allQuestions = result.Questions;
            _generalSourceState = result.RefreshedFromNetwork ? "已从公开题源更新" : "使用随应用附带的离线题源";
            _questionSets = CispQuestionSetCatalog.Load(_questionSetsPath).Sets;
            SetPicker.ItemsSource = _questionSets;
            SetPicker.DisplayMemberPath = nameof(CispQuestionSet.Name);
            SetPicker.SelectedIndex = 0;
            PracticeModePicker.SelectedIndex = _requestedSetMode ? 1 : 0;
            StatusText.Text = result.Warning ?? "题目、图片与练习进度均保存在本机。";
            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                ShowWarning(result.Warning);
            }

            ApplyFilter();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _allQuestions = [];
            ApplyFilter();
            ShowWarning(exception.Message);
        }
        finally
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyFilter()
    {
        if (!_loaded)
        {
            return;
        }

        var selectedDomain = DomainPicker.SelectedItem as string;
        var sourceQuestions = GetActiveQuestions();
        IEnumerable<CispQuestion> query = sourceQuestions;
        if (!string.IsNullOrWhiteSpace(selectedDomain) && selectedDomain != "全部类别")
        {
            query = query.Where(question => question.Domain == selectedDomain);
        }

        if (WrongOnlyToggle.IsChecked == true)
        {
            query = query.Where(question =>
                _progress.Questions.TryGetValue(question.Id, out var item) &&
                item.IsInWrongBook == true);
        }

        if (MarkedOnlyToggle.IsChecked == true)
        {
            query = query.Where(question => IsMarked(question));
        }

        query = ExcludedOnlyToggle.IsChecked == true
            ? query.Where(question => IsExcludedFromDraw(question))
            : query.Where(question => !IsExcludedFromDraw(question));

        _filteredQuestions = query.ToList();
        QuestionCountText.Text = $"{sourceQuestions.Count} 道";
        SessionSizeLabel.Text = $"每组题数（1–{Math.Max(sourceQuestions.Count, 1)}）";
        SetPickerPanel.Visibility = IsSetMode ? Visibility.Visible : Visibility.Collapsed;
        SourceStateText.Text = IsSetMode
            ? $"独立套题 · {_activeSet?.Name} · 不与其他套题混抽 · 正确率按本组计算"
            : _generalSourceState;
        UpdateProgressSummary();

        if (_filteredQuestions.Count == 0)
        {
            _currentQuestion = null;
            QuestionPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Visible;
            EmptyText.Text = sourceQuestions.Count == 0
                ? "题库读取失败，请检查网络后刷新。"
                : ExcludedOnlyToggle.IsChecked == true
                    ? "还没有设置不再抽取的题目。答对后可在题目下方进行设置。"
                    : "当前筛选下没有题目。答错会自动进错题本，也可随时手动标记题目。";
            return;
        }

        EmptyPanel.Visibility = Visibility.Collapsed;
        QuestionPanel.Visibility = Visibility.Visible;
        StartNewSession();
    }

    private void StartNewSession()
    {
        _sessionQuestions = _filteredQuestions
            .OrderBy(_ => _random.Next())
            .Take(_progress.SessionSize)
            .ToList();
        _sessionIndex = 0;
        _sessionAttemptCount = 0;
        _sessionCorrectCount = 0;
        ShowQuestion(_sessionQuestions[_sessionIndex]);
        UpdateProgressSummary();
    }

    private void ShowQuestion(CispQuestion question)
    {
        _currentQuestion = question;
        _selectedIndex = null;
        DomainText.Text = question.Domain;
        QuestionSourceText.Text = IsSetMode && _activeSet is not null
            ? $"{_activeSet.Name} #{question.SourceNumber}"
            : $"公开题源 #{question.SourceNumber}";
        PositionText.Text = $"本组 {_sessionIndex + 1} / {_sessionQuestions.Count}";
        StemText.Text = question.Stem;
        OptionsPanel.Children.Clear();
        _optionBorders.Clear();

        foreach (var imageName in question.ImageNames)
        {
            var fullPath = Path.GetFullPath(Path.Combine(_imageRoot, imageName));
            if (!fullPath.StartsWith(Path.GetFullPath(_imageRoot), StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                continue;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            OptionsPanel.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 0, 14),
                Padding = new Thickness(10),
                BorderBrush = (WpfBrush)FindResource("MuLineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = new WpfImage { Source = bitmap, MaxHeight = 300, Stretch = Stretch.Uniform },
            });
        }

        for (var index = 0; index < question.Options.Count; index++)
        {
            var selector = new WpfButton
            {
                Tag = index,
                Content = ((char)('A' + index)).ToString(),
                Width = 42,
                Height = 34,
                Margin = new Thickness(10, 8, 10, 8),
                FontWeight = FontWeights.Bold,
                Background = (WpfBrush)FindResource("MuAccentSoftBrush"),
                Foreground = (WpfBrush)FindResource("MuAccentBrush"),
                BorderBrush = (WpfBrush)FindResource("MuLineBrush"),
            };
            selector.Click += Option_OnClick;

            var optionText = new WpfTextBox
            {
                Text = question.Options[index],
                IsReadOnly = true,
                IsReadOnlyCaretVisible = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 10, 12, 10),
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = (WpfBrush)FindResource("MuInkBrush"),
            };
            Grid.SetColumn(optionText, 1);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(selector);
            row.Children.Add(optionText);

            var container = new Border
            {
                Margin = new Thickness(0, 0, 0, 9),
                Background = (WpfBrush)FindResource("MuSurfaceQuietBrush"),
                BorderBrush = (WpfBrush)FindResource("MuLineBrush"),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(9),
                Child = row,
            };
            _optionBorders[index] = container;
            OptionsPanel.Children.Add(container);
        }

        AnswerPanel.Visibility = Visibility.Collapsed;
        SubmitButton.IsEnabled = true;
        NextButton.IsEnabled = false;
        NextButton.Content = _sessionIndex + 1 >= _sessionQuestions.Count ? "再来一组" : "下一题";
        RemoveWrongButton.Visibility = IsInWrongBook(question) ? Visibility.Visible : Visibility.Collapsed;
        UpdateExcludeDrawButton();
        UpdateMarkButton();
        ChooseHint.Text = "文字可拖选复制；点 A–D 选答案。";
    }

    private void Option_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentQuestion is null || !SubmitButton.IsEnabled || sender is not WpfButton selected)
        {
            return;
        }

        _selectedIndex = (int)selected.Tag;
        foreach (var (optionIndex, container) in _optionBorders)
        {
            var isSelected = optionIndex == _selectedIndex;
            container.Background = (WpfBrush)FindResource(isSelected ? "MuAccentSoftBrush" : "MuSurfaceQuietBrush");
            container.BorderBrush = (WpfBrush)FindResource(isSelected ? "MuAccentBrush" : "MuLineBrush");
        }

        ChooseHint.Text = $"已选择 {(char)('A' + _selectedIndex.Value)}，可以提交。";
    }

    private void Submit_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentQuestion is null || _selectedIndex is null)
        {
            ChooseHint.Text = "请先选择一个答案。";
            return;
        }

        var correct = _selectedIndex.Value == _currentQuestion.CorrectIndex;
        _sessionAttemptCount++;
        if (correct)
        {
            _sessionCorrectCount++;
        }

        _progressStore.Record(_progress, _currentQuestion.Id, correct, _selectedIndex.Value);
        AnswerPanel.Visibility = Visibility.Visible;
        AnswerPanel.Background = (WpfBrush)FindResource(correct ? "MuSuccessSoftBrush" : "MuDangerSoftBrush");
        AnswerPanel.BorderBrush = (WpfBrush)FindResource(correct ? "MuSuccessBrush" : "MuDangerBrush");
        AnswerTitle.Foreground = (WpfBrush)FindResource(correct ? "MuSuccessBrush" : "MuDangerBrush");
        AnswerTitle.Text = correct
            ? "回答正确"
            : $"回答错误 · 已收进错题本 · 正确答案 {(char)('A' + _currentQuestion.CorrectIndex)}";
        AnswerExplanation.Text = _currentQuestion.Explanation;

        foreach (var (optionIndex, container) in _optionBorders)
        {
            if (optionIndex == _currentQuestion.CorrectIndex)
            {
                container.Background = (WpfBrush)FindResource("MuSuccessSoftBrush");
                container.BorderBrush = (WpfBrush)FindResource("MuSuccessBrush");
            }
            else if (optionIndex == _selectedIndex)
            {
                container.Background = (WpfBrush)FindResource("MuDangerSoftBrush");
                container.BorderBrush = (WpfBrush)FindResource("MuDangerBrush");
            }
        }

        SubmitButton.IsEnabled = false;
        NextButton.IsEnabled = true;
        RemoveWrongButton.Visibility = IsInWrongBook(_currentQuestion) ? Visibility.Visible : Visibility.Collapsed;
        UpdateExcludeDrawButton();
        ChooseHint.Text = "本题结果已计入本机练习记录。";
        UpdateProgressSummary();
    }

    private void RemoveWrong_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentQuestion is null)
        {
            return;
        }

        _progressStore.RemoveFromWrongBook(_progress, _currentQuestion.Id);
        RemoveWrongButton.Visibility = Visibility.Collapsed;
        UpdateProgressSummary();
        ChooseHint.Text = "已从错题本移出。";
        if (WrongOnlyToggle.IsChecked == true)
        {
            ApplyFilter();
        }
    }

    private void Mark_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentQuestion is null)
        {
            return;
        }

        _progressStore.SetMarked(_progress, _currentQuestion.Id, !IsMarked(_currentQuestion));
        UpdateMarkButton();
        UpdateProgressSummary();
        if (MarkedOnlyToggle.IsChecked == true && !IsMarked(_currentQuestion))
        {
            ApplyFilter();
        }
    }

    private void ExcludeDraw_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentQuestion is null)
        {
            return;
        }

        var wasExcluded = IsExcludedFromDraw(_currentQuestion);
        try
        {
            _progressStore.SetExcludedFromDraw(_progress, _currentQuestion.Id, !wasExcluded);
        }
        catch (InvalidOperationException exception)
        {
            WpfMessageBox.Show(this, exception.Message, "CISP 题库", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!wasExcluded)
        {
            _filteredQuestions.RemoveAll(question => question.Id == _currentQuestion.Id);
            ChooseHint.Text = "已设置：以后随机练习不会再抽到，可在左侧恢复。";
        }
        else
        {
            ChooseHint.Text = "已恢复：这道题会重新参与随机抽取。";
        }

        UpdateExcludeDrawButton();
        UpdateProgressSummary();
        if (ExcludedOnlyToggle.IsChecked == true && wasExcluded)
        {
            ApplyFilter();
        }
    }

    private void Next_OnClick(object sender, RoutedEventArgs e)
    {
        if (_sessionIndex + 1 < _sessionQuestions.Count)
        {
            _sessionIndex++;
            ShowQuestion(_sessionQuestions[_sessionIndex]);
        }
        else if (_filteredQuestions.Count > 0)
        {
            StartNewSession();
        }
    }

    private void Filter_OnChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private void PracticeMode_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        _requestedSetMode = PracticeModePicker.SelectedIndex == 1;
        ApplyFilter();
    }

    private void SetPicker_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded && IsSetMode)
        {
            ApplyFilter();
        }
    }

    private void SessionSize_OnClick(object sender, RoutedEventArgs e) => ApplySessionSize();

    private void SessionSize_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            ApplySessionSize();
            e.Handled = true;
        }
    }

    private void ApplySessionSize()
    {
        var activeQuestions = GetActiveQuestions();
        var maximum = activeQuestions.Count > 0 ? activeQuestions.Count : 907;
        if (!int.TryParse(SessionSizeBox.Text.Trim(), out var sessionSize) || sessionSize < 1 || sessionSize > maximum)
        {
            StatusText.Text = $"每组题数请输入 1–{maximum} 之间的整数。";
            SessionSizeBox.Text = _progress.SessionSize.ToString();
            SessionSizeBox.SelectAll();
            SessionSizeBox.Focus();
            return;
        }

        _progressStore.SetSessionSize(_progress, sessionSize);
        StatusText.Text = $"每组题数已设为 {sessionSize}；当前筛选不足时使用全部可用题目。";
        if (_filteredQuestions.Count > 0)
        {
            StartNewSession();
        }
    }

    private async void Refresh_OnClick(object sender, RoutedEventArgs e) => await LoadBankAsync();

    private void ExportWrongBook_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var outputDirectory = Path.Combine(desktop, "CISP错题导出");
            var result = CispWrongBookExporter.Export(
                GetAllKnownQuestions(),
                _progress,
                _imageRoot,
                outputDirectory);
            System.Windows.Clipboard.SetText(result.FilePath);
            StatusText.Text = $"已导出 {result.QuestionCount} 道错题，文件路径已复制。";
            var explorer = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            explorer.ArgumentList.Add("/select,");
            explorer.ArgumentList.Add(result.FilePath);
            Process.Start(explorer);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            WpfMessageBox.Show(this, exception.Message, "导出 CISP 错题", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OpenSource_OnClick(object sender, RoutedEventArgs e) => OpenWithShell(CispQuestionBank.SourcePage);

    private void OpenPdfs_OnClick(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_pdfRoot))
        {
            OpenWithShell(_pdfRoot);
        }
        else
        {
            WpfMessageBox.Show(this, "原版材料目录不存在。", "CISP 题库", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static void OpenWithShell(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    private void UpdateProgressSummary()
    {
        var attempted = _progress.Questions.Values.Count(item => item.AttemptCount > 0);
        var attempts = _progress.Questions.Values.Sum(item => item.AttemptCount);
        var correct = _progress.Questions.Values.Sum(item => item.CorrectCount);
        var wrong = _progress.Questions.Values.Count(item => item.IsInWrongBook == true);
        var marked = _progress.Questions.Values.Count(item => item.IsMarked);
        var excluded = _progress.Questions.Values.Count(item => item.IsExcludedFromDraw);
        if (IsSetMode)
        {
            AttemptedText.Text = $"本组已答 {_sessionAttemptCount} 道";
            AccuracyText.Text = _sessionAttemptCount == 0
                ? "本组正确率 --"
                : $"本组正确率 {(double)_sessionCorrectCount / _sessionAttemptCount:P0}";
        }
        else
        {
            AttemptedText.Text = $"已做 {attempted} 道";
            AccuracyText.Text = attempts == 0 ? "正确率 --" : $"正确率 {(double)correct / attempts:P0}";
        }
        WrongCountText.Text = $"错题本 {wrong} 道";
        MarkedCountText.Text = $"标记 {marked} 道";
        ExcludedCountText.Text = $"不再抽取 {excluded} 道";
    }

    private bool IsInWrongBook(CispQuestion question) =>
        _progress.Questions.TryGetValue(question.Id, out var item) && item.IsInWrongBook == true;

    private bool IsMarked(CispQuestion question) =>
        _progress.Questions.TryGetValue(question.Id, out var item) && item.IsMarked;

    private bool IsExcludedFromDraw(CispQuestion question) =>
        _progress.Questions.TryGetValue(question.Id, out var item) && item.IsExcludedFromDraw;

    private void UpdateExcludeDrawButton()
    {
        var excluded = _currentQuestion is not null && IsExcludedFromDraw(_currentQuestion);
        var canExclude = _currentQuestion is not null &&
            _progress.Questions.TryGetValue(_currentQuestion.Id, out var item) &&
            item.LastWasCorrect;
        ExcludeDrawButton.Content = excluded ? "恢复抽取" : "以后不再抽到";
        ExcludeDrawButton.Visibility = excluded || canExclude
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateMarkButton()
    {
        var marked = _currentQuestion is not null && IsMarked(_currentQuestion);
        MarkButton.Content = marked ? "★ 已标记" : "☆ 标记本题";
        MarkButton.Foreground = (WpfBrush)FindResource(marked ? "MuAccentBrush" : "MuInkBrush");
    }

    private void ShowWarning(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }
}

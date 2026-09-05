using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Toolbox.Core;

public sealed record CispQuestion(
    string Id,
    string SourceNumber,
    string Domain,
    string Stem,
    IReadOnlyList<string> Options,
    int CorrectIndex,
    string Explanation,
    IReadOnlyList<string> ImageNames);

public sealed record CispQuestionBankResult(
    IReadOnlyList<CispQuestion> Questions,
    bool RefreshedFromNetwork,
    string SourcePage,
    string? Warning);

public static partial class CispQuestionBank
{
    public const string SourcePage = "https://github.com/hackctf55/cisp";
    public const string RawSource = "https://raw.githubusercontent.com/npcola/CISP/main/%E8%AF%95%E9%A2%98.md";

    public static readonly IReadOnlyList<string> Domains =
    [
        "信息安全保障",
        "信息安全技术",
        "信息安全管理",
        "信息安全工程",
        "信息安全法规标准",
    ];

    public static async Task<CispQuestionBankResult> LoadAsync(
        string cachePath,
        string? bundledFallbackPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        var localCandidates = new List<IReadOnlyList<CispQuestion>>();
        foreach (var path in new[] { bundledFallbackPath, cachePath }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct())
        {
            if (path is null || !File.Exists(path))
            {
                continue;
            }

            try
            {
                var parsed = Parse(File.ReadAllText(path));
                if (parsed.Count > 0)
                {
                    localCandidates.Add(parsed);
                }
            }
            catch (Exception) when (path != bundledFallbackPath)
            {
                // A damaged user cache must not hide the bundled offline bank.
            }
        }

        var bestLocal = localCandidates.OrderByDescending(candidate => candidate.Count).FirstOrDefault();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MU-Desk-CISP-Question-Bank/0.1");
            var markdown = await client.GetStringAsync(RawSource, cancellationToken);
            var questions = Parse(markdown);
            if (questions.Count < 200)
            {
                throw new InvalidOperationException($"公开题库只解析出 {questions.Count} 道有效题，已拒绝覆盖本地缓存。");
            }

            // The upstream single-file bank currently contains fewer questions
            // than the bundled, merged edition.  Refresh only when it is at least
            // as complete, otherwise an online refresh would shrink 907 back to
            // the old 303-question set.
            if (bestLocal is null || questions.Count >= bestLocal.Count)
            {
                SaveCache(cachePath, markdown);
                return new CispQuestionBankResult(questions, true, SourcePage, null);
            }
        }
        catch (Exception) when (bestLocal is not null)
        {
            // Offline use is expected; the complete bundled bank remains usable.
        }

        if (bestLocal is null)
        {
            throw new InvalidOperationException("CISP 公开题库暂时无法下载，且本机还没有可用题库。");
        }

        return new CispQuestionBankResult(bestLocal, false, SourcePage, null);
    }

    public static IReadOnlyList<CispQuestion> Parse(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var starts = new List<int>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (QuestionStartRegex().IsMatch(lines[index]))
            {
                starts.Add(index);
            }
        }

        var questions = new List<CispQuestion>();
        for (var blockIndex = 0; blockIndex < starts.Count; blockIndex++)
        {
            var start = starts[blockIndex];
            var end = blockIndex + 1 < starts.Count ? starts[blockIndex + 1] : lines.Length;
            var startMatch = QuestionStartRegex().Match(lines[start]);
            var sourceNumber = startMatch.Groups[1].Value;
            var stemParts = new List<string> { startMatch.Groups[2].Value };
            var options = new Dictionary<char, string>();
            var correctLetters = new HashSet<char>();
            var explanationParts = new List<string>();
            var imageNames = new List<string>();
            var foundFirstOption = false;

            for (var lineIndex = start + 1; lineIndex < end; lineIndex++)
            {
                var rawLine = lines[lineIndex];
                foreach (Match imageMatch in ImageRegex().Matches(rawLine))
                {
                    var imageName = Path.GetFileName(imageMatch.Groups[1].Value.Replace('\\', '/'));
                    if (!string.IsNullOrWhiteSpace(imageName) && !imageNames.Contains(imageName, StringComparer.OrdinalIgnoreCase))
                    {
                        imageNames.Add(imageName);
                    }
                }

                var optionMatch = OptionRegex().Match(rawLine);
                if (optionMatch.Success)
                {
                    foundFirstOption = true;
                    var letter = optionMatch.Groups[1].Value[0];
                    var text = Clean(optionMatch.Groups[2].Value);
                    if (!string.IsNullOrWhiteSpace(text) && !options.ContainsKey(letter))
                    {
                        options[letter] = text;
                    }

                    if (rawLine.Contains("**", StringComparison.Ordinal))
                    {
                        correctLetters.Add(letter);
                    }

                    continue;
                }

                var cleaned = Clean(rawLine);
                if (string.IsNullOrWhiteSpace(cleaned) || cleaned.StartsWith("![", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!foundFirstOption && !rawLine.TrimStart().StartsWith('>'))
                {
                    stemParts.Add(cleaned);
                }
                else if (foundFirstOption && rawLine.TrimStart().StartsWith('>'))
                {
                    explanationParts.Add(cleaned);
                }
            }

            if (options.Count != 4 || !options.Keys.Order().SequenceEqual(new[] { 'A', 'B', 'C', 'D' }) ||
                correctLetters.Count != 1)
            {
                continue;
            }

            var stem = NormalizeText(string.Join(' ', stemParts));
            if (stem.Length < 6 || stem.Contains('�'))
            {
                continue;
            }

            var orderedOptions = new[] { options['A'], options['B'], options['C'], options['D'] };
            if (orderedOptions.Any(option => option.Contains('�') || option.Length == 0))
            {
                continue;
            }

            var correctLetter = correctLetters.Single();
            var explanation = explanationParts.Count == 0
                ? "公开题库未附解析。"
                : NormalizeText(string.Join(' ', explanationParts));
            var domain = Classify(stem + " " + string.Join(' ', orderedOptions));
            questions.Add(new CispQuestion(
                $"npcola-{questions.Count + 1:000}",
                sourceNumber,
                domain,
                stem,
                orderedOptions,
                correctLetter - 'A',
                explanation,
                imageNames));
        }

        return questions;
    }

    private static string Classify(string text)
    {
        if (ContainsAny(text, "网络安全法", "保密法", "法律", "法规", "标准", "GB/", "GB／", "等级保护", "合规", "国家秘密", "条例"))
        {
            return "信息安全法规标准";
        }

        if (ContainsAny(text, "SSE-CMM", "工程", "生命周期", "需求分析", "软件开发", "项目管理", "威胁建模", "代码审核", "测试"))
        {
            return "信息安全工程";
        }

        if (ContainsAny(text, "ISMS", "风险", "管理", "资产", "业务连续", "应急响应", "审计", "PDCA", "RPO", "RTO", "灾难恢复"))
        {
            return "信息安全管理";
        }

        if (ContainsAny(text, "密码", "加密", "操作系统", "Linux", "Windows", "网络", "防火墙", "数据库", "攻击", "漏洞", "恶意代码", "访问控制", "身份认证", "IP", "Web", "SQL"))
        {
            return "信息安全技术";
        }

        return "信息安全保障";
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string Clean(string value) => NormalizeText(
        value.Trim().TrimStart('>').Trim().Trim('`').Replace("**", string.Empty, StringComparison.Ordinal));

    private static string NormalizeText(string value) =>
        WhitespaceRegex().Replace(value, " ").Trim();

    private static void SaveCache(string cachePath, string markdown)
    {
        try
        {
            var fullPath = Path.GetFullPath(cachePath);
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("CISP 题库缓存路径无效。");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"bank.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, markdown);
                File.Move(temporaryPath, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"CISP 题库缓存无法写入：{exception.Message}", exception);
        }
    }

    [GeneratedRegex(@"^\s*(\d+)\.\s+(.+)$")]
    private static partial Regex QuestionStartRegex();

    [GeneratedRegex(@"^\s*(?:`)?(?:\*\*)?([A-D])[\.．。]\s*(.+?)(?:\*\*)?(?:`)?\s*$")]
    private static partial Regex OptionRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\((?:\./)?pic/([^\)]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ImageRegex();
}

public sealed class CispStudyProgress
{
    public int SessionSize { get; set; } = 100;

    public Dictionary<string, CispQuestionProgress> Questions { get; set; } =
        new(StringComparer.Ordinal);
}

public sealed class CispQuestionProgress
{
    public int AttemptCount { get; set; }

    public int CorrectCount { get; set; }

    public bool LastWasCorrect { get; set; }

    public int WrongCount { get; set; }

    public bool? IsInWrongBook { get; set; }

    public bool IsMarked { get; set; }

    public bool IsExcludedFromDraw { get; set; }

    public int? LastSelectedIndex { get; set; }

    public DateTimeOffset LastAnsweredAt { get; set; }
}

public sealed class CispProgressStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public CispProgressStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public CispStudyProgress Load()
    {
        if (!File.Exists(_filePath))
        {
            return new CispStudyProgress();
        }

        try
        {
            var progress = JsonSerializer.Deserialize<CispStudyProgress>(
                File.ReadAllText(_filePath),
                _options) ?? new CispStudyProgress();
            progress.Questions = new Dictionary<string, CispQuestionProgress>(
                progress.Questions ?? [],
                StringComparer.Ordinal);
            progress.SessionSize = progress.SessionSize is >= 1 and <= 1000
                ? progress.SessionSize
                : 100;
            foreach (var item in progress.Questions.Values)
            {
                item.WrongCount = Math.Max(item.WrongCount, item.AttemptCount - item.CorrectCount);
                item.IsInWrongBook ??= item.AttemptCount > 0 && !item.LastWasCorrect;
            }
            return progress;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException($"CISP 学习进度读取失败：{exception.Message}", exception);
        }
    }

    public void Record(CispStudyProgress progress, string questionId, bool correct, int? selectedIndex = null)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!progress.Questions.TryGetValue(questionId, out var item))
        {
            item = new CispQuestionProgress();
            progress.Questions[questionId] = item;
        }

        item.AttemptCount++;
        if (correct)
        {
            item.CorrectCount++;
        }
        else
        {
            item.WrongCount++;
            item.IsInWrongBook = true;
        }

        item.LastWasCorrect = correct;
        item.LastSelectedIndex = selectedIndex;
        item.LastAnsweredAt = DateTimeOffset.Now;
        Save(progress);
    }

    public void RemoveFromWrongBook(CispStudyProgress progress, string questionId)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (progress.Questions.TryGetValue(questionId, out var item))
        {
            item.IsInWrongBook = false;
            Save(progress);
        }
    }

    public void SetMarked(CispStudyProgress progress, string questionId, bool marked)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!progress.Questions.TryGetValue(questionId, out var item))
        {
            item = new CispQuestionProgress();
            progress.Questions[questionId] = item;
        }

        item.IsMarked = marked;
        Save(progress);
    }

    public void SetExcludedFromDraw(CispStudyProgress progress, string questionId, bool excluded)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!progress.Questions.TryGetValue(questionId, out var item))
        {
            if (excluded)
            {
                throw new InvalidOperationException("只有答对的题目才能设为以后不再抽取。");
            }

            return;
        }

        if (excluded && !item.LastWasCorrect)
        {
            throw new InvalidOperationException("只有刚刚答对的题目才能设为以后不再抽取。");
        }

        item.IsExcludedFromDraw = excluded;
        Save(progress);
    }

    public void SetSessionSize(CispStudyProgress progress, int sessionSize)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (sessionSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionSize), "每组题数必须在 1 到 1000 之间。");
        }

        progress.SessionSize = sessionSize;
        Save(progress);
    }

    private void Save(CispStudyProgress progress)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("CISP 学习进度路径无效。");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"progress.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(progress, _options));
                File.Move(temporaryPath, _filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException($"CISP 学习进度无法保存：{exception.Message}", exception);
        }
    }
}

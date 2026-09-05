using System.Text.Json;

namespace Toolbox.Core;

public sealed record CispQuestionSet(
    string Id,
    string Name,
    string Subtitle,
    string SourceFile,
    int QuestionCount,
    IReadOnlyList<CispQuestion> Questions);

public sealed record CispQuestionSetCatalog(
    int Version,
    IReadOnlyList<CispQuestionSet> Sets)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static CispQuestionSetCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("没有找到 2026 最新八套题数据。");
        }

        try
        {
            var catalog = JsonSerializer.Deserialize<CispQuestionSetCatalog>(File.ReadAllText(path), Options)
                ?? throw new InvalidOperationException("2026 最新八套题数据为空。");
            if (catalog.Sets.Count != 8)
            {
                throw new InvalidOperationException($"2026 最新套题应有 8 套，实际读取到 {catalog.Sets.Count} 套。");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var set in catalog.Sets)
            {
                if (set.Questions.Count == 0 || set.QuestionCount != set.Questions.Count)
                {
                    throw new InvalidOperationException($"套题“{set.Name}”题数不完整。");
                }

                foreach (var question in set.Questions)
                {
                    if (!ids.Add(question.Id) || question.Options.Count != 4 || question.CorrectIndex is < 0 or > 3)
                    {
                        throw new InvalidOperationException($"套题“{set.Name}”包含无效或重复题目编号。");
                    }
                }
            }

            return catalog;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"2026 最新八套题读取失败：{exception.Message}", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"2026 最新八套题读取失败：{exception.Message}", exception);
        }
    }
}

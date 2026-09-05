using System.IO.Compression;
using System.Text;

namespace Toolbox.Core;

public sealed record CispWrongBookExportResult(string FilePath, int QuestionCount, int ImageCount);

public static class CispWrongBookExporter
{
    public static CispWrongBookExportResult Export(
        IReadOnlyList<CispQuestion> questions,
        CispStudyProgress progress,
        string imageRoot,
        string outputDirectory,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var wrongQuestions = questions
            .Where(question => progress.Questions.TryGetValue(question.Id, out var item) && item.IsInWrongBook == true)
            .ToArray();
        if (wrongQuestions.Length == 0)
        {
            throw new InvalidOperationException("错题本还是空的，暂无可导出内容。");
        }

        Directory.CreateDirectory(outputDirectory);
        var timestamp = (now ?? DateTimeOffset.Now).ToString("yyyyMMdd-HHmmss");
        var filePath = Path.Combine(outputDirectory, $"CISP错题-{timestamp}.zip");
        var suffix = 1;
        while (File.Exists(filePath))
        {
            filePath = Path.Combine(outputDirectory, $"CISP错题-{timestamp}-{suffix++}.zip");
        }

        var imageRootFullPath = Path.GetFullPath(imageRoot);
        var markdown = new StringBuilder();
        markdown.AppendLine("# CISP 错题请教");
        markdown.AppendLine();
        markdown.AppendLine("请逐题解释：正确选项为什么正确，其他选项错在哪里，并给出相关 CISP 知识点和记忆方法。如题目或答案已过时，请明确指出。");
        markdown.AppendLine();
        markdown.AppendLine($">导出时间：{(now ?? DateTimeOffset.Now):yyyy-MM-dd HH:mm:ss}  ");
        markdown.AppendLine($">共 {wrongQuestions.Length} 道错题；题源为公开网络学习资料，不是官方真题。");

        var images = new List<(string SourcePath, string EntryName)>();
        for (var questionIndex = 0; questionIndex < wrongQuestions.Length; questionIndex++)
        {
            var question = wrongQuestions[questionIndex];
            progress.Questions.TryGetValue(question.Id, out var item);
            markdown.AppendLine();
            markdown.AppendLine($"## {questionIndex + 1}. [{question.Domain}] 公开题源 #{question.SourceNumber}");
            markdown.AppendLine();
            markdown.AppendLine(question.Stem);
            markdown.AppendLine();

            foreach (var imageName in question.ImageNames)
            {
                var sourcePath = Path.GetFullPath(Path.Combine(imageRootFullPath, imageName));
                if (!sourcePath.StartsWith(imageRootFullPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(sourcePath))
                {
                    continue;
                }

                var entryName = $"images/{question.Id}-{Path.GetFileName(imageName)}";
                images.Add((sourcePath, entryName));
                markdown.AppendLine($"![题图]({entryName})");
                markdown.AppendLine();
            }

            for (var optionIndex = 0; optionIndex < question.Options.Count; optionIndex++)
            {
                markdown.AppendLine($"- {(char)('A' + optionIndex)}. {question.Options[optionIndex]}");
            }

            markdown.AppendLine();
            var selected = item?.LastSelectedIndex is >= 0 and <= 3
                ? ((char)('A' + item.LastSelectedIndex.Value)).ToString()
                : "未记录";
            markdown.AppendLine($"- 我最近选的：{selected}");
            markdown.AppendLine($"- 参考答案：{(char)('A' + question.CorrectIndex)}");
            markdown.AppendLine($"- 题源解析：{question.Explanation}");
        }

        using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Create))
        {
            var markdownEntry = archive.CreateEntry("错题请教.md", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(markdownEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                writer.Write(markdown.ToString());
            }

            foreach (var image in images.DistinctBy(item => item.EntryName, StringComparer.OrdinalIgnoreCase))
            {
                archive.CreateEntryFromFile(image.SourcePath, image.EntryName, CompressionLevel.Optimal);
            }
        }

        return new CispWrongBookExportResult(filePath, wrongQuestions.Length, images.Count);
    }
}

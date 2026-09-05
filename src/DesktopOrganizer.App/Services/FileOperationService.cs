using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace DesktopOrganizer.Services;

public sealed class FileOperationService
{
    public FileMoveBatchResult MoveFiles(IEnumerable<string> paths, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var moves = new List<FileMoveRecord>();
        var failures = new List<FileOperationFailure>();

        foreach (var sourcePath in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                {
                    failures.Add(new(sourcePath, "源文件不存在"));
                    continue;
                }

                var targetPath = CreateUniqueTargetPath(targetDirectory, Path.GetFileName(sourcePath));
                if (string.Equals(
                        Path.GetFullPath(sourcePath),
                        Path.GetFullPath(targetPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MovePath(sourcePath, targetPath);
                moves.Add(new FileMoveRecord(sourcePath, targetPath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(new(sourcePath, exception.Message));
            }
        }

        return new FileMoveBatchResult(moves, failures);
    }

    public FileMoveBatchResult Rename(string path, string newName)
    {
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(newName) ||
            newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return new([], [new(path, "文件名无效")]);
        }

        var target = Path.Combine(parent, newName.Trim());
        if ((File.Exists(target) || Directory.Exists(target)) &&
            !string.Equals(path, target, StringComparison.OrdinalIgnoreCase))
        {
            return new([], [new(path, "同名文件已经存在")]);
        }

        try
        {
            if (string.Equals(path, target, StringComparison.Ordinal))
            {
                return new([], []);
            }

            MovePath(path, target);
            return new([new FileMoveRecord(path, target)], []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new([], [new(path, exception.Message)]);
        }
    }

    public FileMoveBatchResult UndoMoves(IEnumerable<FileMoveRecord> records)
    {
        var restored = new List<FileMoveRecord>();
        var failures = new List<FileOperationFailure>();
        foreach (var record in records.Reverse())
        {
            try
            {
                if ((!File.Exists(record.DestinationPath) && !Directory.Exists(record.DestinationPath)) ||
                    File.Exists(record.SourcePath) || Directory.Exists(record.SourcePath))
                {
                    failures.Add(new(record.DestinationPath, "文件已发生变化，无法安全撤销"));
                    continue;
                }

                MovePath(record.DestinationPath, record.SourcePath);
                restored.Add(new FileMoveRecord(record.DestinationPath, record.SourcePath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(new(record.DestinationPath, exception.Message));
            }
        }

        return new FileMoveBatchResult(restored, failures);
    }

    public IReadOnlyList<FileOperationFailure> MoveToRecycleBin(IEnumerable<string> paths)
    {
        var failures = new List<FileOperationFailure>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(path))
                {
                    FileSystem.DeleteFile(
                        path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin,
                        UICancelOption.DoNothing);
                }
                else if (Directory.Exists(path))
                {
                    FileSystem.DeleteDirectory(
                        path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin,
                        UICancelOption.DoNothing);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(new(path, exception.Message));
            }
        }

        return failures;
    }

    private static string CreateUniqueTargetPath(string directory, string name)
    {
        var candidate = Path.Combine(directory, name);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        for (var suffix = 2; ; suffix++)
        {
            candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static void MovePath(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(sourcePath))
        {
            Directory.Move(sourcePath, destinationPath);
            return;
        }

        try
        {
            File.Move(sourcePath, destinationPath);
        }
        catch (IOException) when (!string.Equals(
                                      Path.GetPathRoot(sourcePath),
                                      Path.GetPathRoot(destinationPath),
                                      StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, destinationPath, false);
            File.Delete(sourcePath);
        }
    }
}

public sealed record FileMoveRecord(string SourcePath, string DestinationPath);

public sealed record FileOperationFailure(string Path, string Reason);

public sealed record FileMoveBatchResult(
    IReadOnlyList<FileMoveRecord> Moves,
    IReadOnlyList<FileOperationFailure> Failures);

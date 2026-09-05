using System.IO;
using PersonalTools.Core;

namespace PersonalTools.App.Services;

internal static class EntryMigrationService
{
    public static async Task MigrateAsync(
        string dataDirectory,
        IEntryProvider entries,
        CancellationToken cancellationToken = default)
    {
        var reminderPath = Path.Combine(dataDirectory, "reminders.json");
        var notePath = Path.Combine(dataDirectory, "notes.json");
        BackupOnce(reminderPath);
        BackupOnce(notePath);

        var existing = await entries.QueryAsync(
            new EntryQuery(IncludeCompleted: true, IncludeArchived: true, Limit: 2000),
            cancellationToken);
        var externalIds = existing
            .Select(item => item.ExternalId)
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal);

        if (File.Exists(reminderPath))
        {
            var reminders = new LocalJsonReminderProvider(reminderPath);
            foreach (var reminder in await reminders.GetAllAsync(cancellationToken))
            {
                var externalId = $"legacy-reminder:{reminder.Id}";
                if (!externalIds.Add(externalId))
                {
                    continue;
                }

                var migrated = await entries.CreateAsync(
                    new CreateEntryCommand(
                        reminder.Content,
                        DueAt: reminder.DueAt,
                        Source: reminder.Source,
                        ExternalId: externalId,
                        Metadata: reminder.Metadata),
                    cancellationToken);
                if (reminder.Status == ReminderStatus.Completed)
                {
                    await entries.SetStatusAsync(migrated.Id, EntryStatus.Completed, cancellationToken);
                }
            }
        }

        if (File.Exists(notePath))
        {
            var notes = new LocalJsonNoteProvider(notePath);
            var legacyNotes = await notes.SearchAsync(
                new NoteSearchQuery(IncludeArchived: true, Limit: 1000),
                cancellationToken);
            foreach (var note in legacyNotes)
            {
                var externalId = $"legacy-note:{note.Id}";
                if (!externalIds.Add(externalId))
                {
                    continue;
                }

                var content = string.IsNullOrWhiteSpace(note.Title)
                    ? note.Content
                    : $"{note.Title}\n{note.Content}";
                var migrated = await entries.CreateAsync(
                    new CreateEntryCommand(
                        content,
                        note.Tags,
                        Source: note.Source,
                        ExternalId: externalId,
                        Metadata: note.Metadata),
                    cancellationToken);
                if (note.Status == NoteStatus.Archived)
                {
                    await entries.SetStatusAsync(migrated.Id, EntryStatus.Archived, cancellationToken);
                }
            }
        }
    }

    private static void BackupOnce(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var backup = path + ".pre-unified.bak";
        if (!File.Exists(backup))
        {
            File.Copy(path, backup, overwrite: false);
        }
    }
}

using PersonalTools.Core;

var root = Path.Combine(Path.GetTempPath(), $"reminder-notes-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
try
{
    var path = Path.Combine(root, "entries.json");
    var now = DateTimeOffset.UtcNow;
    var provider = new LocalJsonEntryProvider(path);

    var note = await provider.CreateAsync(new CreateEntryCommand("普通随记", ["灵感"]));
    var reminder = await provider.CreateAsync(new CreateEntryCommand("到点内容", DueAt: now.AddMinutes(-1)));
    Assert(note.DueAt is null, "普通随记不应被强制设置提醒。");
    Assert(reminder.DueAt is not null, "提醒随记应保存时间。");

    var due = await provider.GetMissedAsync(now);
    Assert(due.Count == 1 && due[0].Id == reminder.Id, "应只返回真正到期的活动随记。");
    var triggered = await provider.TryMarkTriggeredIfDueAsync(reminder.Id, reminder.Revision, now);
    Assert(triggered?.LastTriggeredAt is not null, "到期随记应被原子标记为已触发。");
    Assert((await provider.GetMissedAsync(now)).Count == 0, "触发后不应重复提醒。");

    var snoozed = await provider.SnoozeAsync(reminder.Id, now.AddMinutes(10));
    Assert(snoozed.LastTriggeredAt is null, "稍后提醒应清除触发标记。");
    Assert(snoozed.Status == EntryStatus.Active, "稍后提醒应恢复活动状态。");

    var card = await provider.SetDesktopCardAsync(
        note.Id,
        note.DesktopCard with
        {
            IsPinned = true,
            Left = 321,
            Top = 187,
            Width = 350,
            Height = 240,
            AlwaysOnTop = true,
            IsLocked = true,
        });
    Assert(card.DesktopCard.IsPinned, "随记应可贴到桌面。");

    var restarted = new LocalJsonEntryProvider(path);
    var restored = await restarted.GetAsync(note.Id);
    Assert(restored?.DesktopCard.Left == 321 && restored.DesktopCard.IsLocked, "桌面卡片位置和状态应持久化。");

    var edited = await restarted.UpdateAsync(
        reminder.Id,
        new UpdateEntryCommand("改成普通随记", ["完成"], ClearDueAt: true));
    Assert(edited.DueAt is null && edited.LastTriggeredAt is null, "取消提醒后应成为普通随记。");
    await restarted.SetStatusAsync(edited.Id, EntryStatus.Completed);
    var completed = await restarted.QueryAsync(new EntryQuery(IncludeCompleted: true));
    Assert(completed.Any(item => item.Id == edited.Id && item.Status == EntryStatus.Completed), "完成状态应可查询。");

    var recurring = await restarted.CreateAsync(new CreateEntryCommand(
        "每日站会",
        DueAt: now.AddMinutes(-5),
        Repeat: RepeatRule.Daily));
    var recurringNext = await restarted.CompleteAsync(recurring.Id);
    Assert(recurringNext.Status == EntryStatus.Active, "重复提醒完成后仍应保持活动状态。");
    Assert(recurringNext.DueAt > now && recurringNext.LastTriggeredAt is null, "重复提醒应滚动到未来并清除触发标记。");
    Assert(recurringNext.DueAt?.ToLocalTime().TimeOfDay == recurring.DueAt?.ToLocalTime().TimeOfDay, "每日重复应保留本地时间。");

    var creates = Enumerable.Range(0, 24)
        .Select(index => restarted.CreateAsync(new CreateEntryCommand($"并发 {index}")));
    await Task.WhenAll(creates);
    var all = await restarted.QueryAsync(new EntryQuery(IncludeCompleted: true, IncludeArchived: true));
    Assert(all.Count == 27, "并发创建不得丢失随记。");

    Console.WriteLine("PersonalTools unified-entry tests passed.");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

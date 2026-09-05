using System.Buffers.Binary;
using System.Text.Json;
using PersonalTools.Core;
using LightPet.Core.Behavior;
using LightPet.Core.Interaction;
using LightPet.Core.Packs;
using LightPet.Core.Placement;

var tests = new (string Name, Action Test)[]
{
    ("加载合法角色包", TestValidPack),
    ("拒绝目录穿越", TestPathTraversal),
    ("拒绝缺少标杆动作", TestMissingRequiredAction),
    ("解析逐帧时长", TestFrameDuration),
    ("拒绝错误画布尺寸", TestWrongCanvasSize),
    ("命中头部区域", TestHitRegion),
    ("查询可选动作", TestOptionalAction),
    ("窗口限制在工作区", TestWindowClamp),
    ("按可见轮廓贴近屏幕边缘", TestVisibleContentPlacement),
    ("屏幕边缘自动转向", TestWalkDirectionAtEdge),
    ("行走距离不超过可用空间", TestAvailableWalkDistance),
    ("四边巡游方向与转角", TestEdgePatrolDirections),
    ("交互基线保持轻量尺寸与横向拖动", TestInteractionBaseline),
    ("时间段与工作休息日解析", TestPetContextResolution),
    ("QA 时间覆盖保持本地日期", TestPetContextQaTime),
    ("情境代表动作每时段只触发一次", TestContextSignatureAction),
    ("自主动作避免近期重复", TestContextActionDiversity),
    ("深夜与贴边活动自动降频", TestContextActivityDelay),
    ("提醒持久化并恢复错过项", TestReminderPersistenceAndMissed),
    ("提醒稍后完成与删除", TestReminderSnoozeCompleteDelete),
    ("提醒触发拒绝陈旧快照", TestReminderTriggerRejectsStaleSnapshot),
    ("提醒并发写入串行化", TestConcurrentReminderWrites),
    ("便签搜索更新归档与删除", TestNoteSearchArchiveDelete),
    ("损坏 JSON 安全降级", TestCorruptJsonRecovery),
};

var failures = new List<string>();
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {name}: {exception.Message}");
    }
}

foreach (var failure in failures)
{
    Console.Error.WriteLine(failure);
}

return failures.Count == 0 ? 0 : 1;

static void TestValidPack()
{
    using var fixture = PackFixture.Create();
    var pack = PetPackLoader.Load(fixture.Root);
    Equal("test-pet", pack.Manifest.Id);
    Equal(4, pack.Actions.Count);
}

static void TestPathTraversal()
{
    using var fixture = PackFixture.Create();
    fixture.Manifest.Actions[0].Phases[0] = new PhaseDefinition
    {
        Kind = AnimationPhase.Loop,
        Folder = "../outside",
    };
    fixture.SaveManifest();
    Throws<PetPackException>(() => PetPackLoader.Load(fixture.Root));
}

static void TestMissingRequiredAction()
{
    using var fixture = PackFixture.Create();
    fixture.Manifest.Actions.RemoveAll(action => action.Id == "touch-head");
    fixture.SaveManifest();
    Throws<PetPackException>(() => PetPackLoader.Load(fixture.Root));
}

static void TestFrameDuration()
{
    using var fixture = PackFixture.Create();
    var pack = PetPackLoader.Load(fixture.Root);
    var frame = pack.GetAction("idle").GetPhase(AnimationPhase.Loop)!.Frames[0];
    Equal(250, frame.DurationMilliseconds);
}

static void TestWrongCanvasSize()
{
    using var fixture = PackFixture.Create();
    fixture.SetFirstFrameDimensions(512, 512);
    Throws<PetPackException>(() => PetPackLoader.Load(fixture.Root));
}

static void TestHitRegion()
{
    using var fixture = PackFixture.Create();
    var pack = PetPackLoader.Load(fixture.Root);
    Equal("head", pack.HitTest(500, 240)!);
    Equal("body", pack.HitTest(500, 600)!);
}

static void TestOptionalAction()
{
    using var fixture = PackFixture.Create();
    var pack = PetPackLoader.Load(fixture.Root);
    Equal(true, pack.HasAction("idle"));
    Equal(false, pack.HasAction("fall-land"));
}

static void TestWindowClamp()
{
    var position = WindowPlacement.Clamp(-30, 900, 260, 260, 0, 0, 1920, 1040);
    Equal(0d, position.Left);
    Equal(780d, position.Top);
}

static void TestVisibleContentPlacement()
{
    var bounds = WindowPlacement.ExpandForVisibleContent(
        0, 0, 1920, 1040,
        130, 130,
        0.25, 0.055, 0.75, 0.95,
        safetyGap: 2);
    Equal(-30.5, bounds.Left);
    Equal(-5.15, bounds.Top);
    Equal(1950.5, bounds.Right);
    Equal(1044.5, bounds.Bottom);

    var position = WindowPlacement.Clamp(
        -100, 1200, 130, 130,
        bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    Equal(-30.5, position.Left);
    Equal(914.5, position.Top);
}

static void TestWalkDirectionAtEdge()
{
    Equal(-1, WindowPlacement.ChooseWalkDirection(1, 1580, 260, 0, 1920, 180));
    Equal(-1, WindowPlacement.ChooseWalkDirection(1, 1530, 260, 0, 1920, 180));
    Equal(1, WindowPlacement.ChooseWalkDirection(-1, 80, 260, 0, 1920, 180));
    Equal(1, WindowPlacement.ChooseWalkDirection(1, 800, 260, 0, 1920, 180));
}

static void TestAvailableWalkDistance()
{
    Equal(80d, WindowPlacement.AvailableWalkDistance(1, 1580, 260, 0, 1920, 180));
    Equal(180d, WindowPlacement.AvailableWalkDistance(-1, 1580, 260, 0, 1920, 180));
    Equal(80d, WindowPlacement.AvailableWalkDistance(-1, 80, 260, 0, 1920, 180));
}

static void TestEdgePatrolDirections()
{
    Equal(-1, EdgePatrol.AxisDirection(ScreenEdge.Bottom, 1));
    Equal(1, EdgePatrol.AxisDirection(ScreenEdge.Top, 1));
    Equal(1, EdgePatrol.AxisDirection(ScreenEdge.Right, 1));
    Equal(-1, EdgePatrol.AxisDirection(ScreenEdge.Left, 1));
    Equal(ScreenEdge.Left, EdgePatrol.NextEdge(ScreenEdge.Bottom, 1));
    Equal(ScreenEdge.Top, EdgePatrol.NextEdge(ScreenEdge.Left, 1));
    Equal(ScreenEdge.Right, EdgePatrol.NextEdge(ScreenEdge.Bottom, -1));
    Equal(80d, EdgePatrol.AvailableDistance(
        ScreenEdge.Bottom,
        -1,
        80,
        780,
        260,
        260,
        0,
        0,
        1920,
        1040));
    Equal(ScreenEdge.Bottom, EdgePatrol.Detect(800, 780, 260, 260, 0, 0, 1920, 1040));
    Equal(ScreenEdge.Left, EdgePatrol.Detect(0, 300, 260, 260, 0, 0, 1920, 1040));
    Equal(null, EdgePatrol.Detect(800, 300, 260, 260, 0, 0, 1920, 1040));
}

static void TestInteractionBaseline()
{
    Equal(130d, PetInteractionContract.DefaultDisplaySize);
    Equal("raise", PetInteractionContract.BodyDragAction);
    Equal("fall-land", PetInteractionContract.DragReleaseAction);
    Equal("cheek-poke", PetInteractionContract.DirectActionForRegion("cheek")!);
    Equal("touch-head", PetInteractionContract.DirectActionForRegion("head")!);
    Equal("touch-body", PetInteractionContract.DirectActionForRegion("body")!);
    Equal(null, PetInteractionContract.DirectActionForRegion("transparent"));
}

static void TestPetContextResolution()
{
    var mondayMorning = PetContextResolver.Resolve(
        new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.FromHours(8)));
    Equal(PetDayType.Workday, mondayMorning.DayType);
    Equal(PetTimeBlock.Morning, mondayMorning.TimeBlock);

    var sundayAfternoon = PetContextResolver.Resolve(
        new DateTimeOffset(2026, 8, 23, 15, 0, 0, TimeSpan.FromHours(8)));
    Equal(PetDayType.RestDay, sundayAfternoon.DayType);
    Equal(PetTimeBlock.Afternoon, sundayAfternoon.TimeBlock);

    var adjustedWorkday = PetContextResolver.Resolve(
        sundayAfternoon.LocalTime,
        "workday");
    Equal(PetDayType.Workday, adjustedWorkday.DayType);
}

static void TestPetContextQaTime()
{
    var realTime = new DateTimeOffset(2026, 8, 23, 10, 15, 0, TimeSpan.FromHours(8));
    var overridden = PetContextResolver.ResolveQaTime(realTime, "21:30");
    Equal(2026, overridden.Year);
    Equal(8, overridden.Month);
    Equal(23, overridden.Day);
    Equal(21, overridden.Hour);
    Equal(30, overridden.Minute);
    Equal(PetTimeBlock.Evening, PetContextResolver.Resolve(overridden).TimeBlock);
}

static void TestContextSignatureAction()
{
    var planner = new ContextBehaviorPlanner();
    IReadOnlySet<string> available = new HashSet<string>(
        ["stretch", "yawn", "hair-fix", "sit-rest"],
        StringComparer.OrdinalIgnoreCase);
    var morning = PetContextResolver.Resolve(
        new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.FromHours(8)));

    Equal(true, planner.TryChooseSignatureAction(morning, available, 0, out var action));
    Equal("stretch", action);
    Equal(false, planner.TryChooseSignatureAction(morning, available, 0.5, out _));

    var midday = PetContextResolver.Resolve(morning.LocalTime.AddHours(4));
    Equal(true, planner.TryChooseSignatureAction(midday, available, 0, out action));
    Equal("sit-rest", action);
}

static void TestContextActionDiversity()
{
    var planner = new ContextBehaviorPlanner();
    IReadOnlySet<string> available = new HashSet<string>(
        ["idle-random", "think"],
        StringComparer.OrdinalIgnoreCase);
    var context = PetContextResolver.Resolve(
        new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.FromHours(8)));

    var first = planner.ChooseAmbientAction(context, available, 0.8);
    var second = planner.ChooseAmbientAction(context, available, 0.8);
    Equal(false, string.Equals(first, second, StringComparison.OrdinalIgnoreCase));
    Equal(null, planner.ChooseAmbientAction(context, available, 0.2));
}

static void TestContextActivityDelay()
{
    var daytime = PetContextResolver.Resolve(
        new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.FromHours(8)));
    var lateNight = PetContextResolver.Resolve(daytime.LocalTime.AddHours(14));

    Equal(new ActivityDelayRange(45, 91),
        ContextBehaviorPlanner.GetActivityDelay(daytime, restingOnSideOrTop: false, qaStress: false));
    Equal(new ActivityDelayRange(90, 181),
        ContextBehaviorPlanner.GetActivityDelay(lateNight, restingOnSideOrTop: false, qaStress: false));
    Equal(new ActivityDelayRange(240, 481),
        ContextBehaviorPlanner.GetActivityDelay(lateNight, restingOnSideOrTop: true, qaStress: false));
}

static void TestReminderPersistenceAndMissed()
{
    using var fixture = PersonalToolFixture.Create();
    var now = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
    var clock = new ManualTimeProvider(now);
    var provider = new LocalJsonReminderProvider(fixture.RemindersPath, "local-test", clock);
    var missed = provider.CreateAsync(new CreateReminderCommand(
        "提交方案",
        now.AddMinutes(-5),
        Source: "pet-menu",
        ExternalId: "task-42",
        Metadata: new Dictionary<string, string> { ["project"] = "LightPet" }))
        .GetAwaiter().GetResult();
    provider.CreateAsync(new CreateReminderCommand("喝水", now.AddMinutes(30)))
        .GetAwaiter().GetResult();

    Equal("local-test", missed.ProviderId);
    Equal("task-42", missed.ExternalId!);
    Equal("pet-menu", missed.Source);
    Equal("LightPet", missed.Metadata["project"]);
    Equal(1L, missed.Revision);
    Equal(now, missed.CreatedAt);

    // A fresh provider proves that state is loaded from disk rather than memory.
    var restarted = new LocalJsonReminderProvider(fixture.RemindersPath, "local-test", clock);
    var recovered = restarted.GetMissedAsync().GetAwaiter().GetResult();
    Equal(1, recovered.Count);
    Equal(missed.Id, recovered[0].Id);

    var triggered = restarted.TryMarkTriggeredIfDueAsync(missed.Id, missed.Revision, now)
        .GetAwaiter().GetResult()!;
    Equal(now, triggered.LastTriggeredAt!.Value);
    Equal(2L, triggered.Revision);
    Equal(0, restarted.GetMissedAsync().GetAwaiter().GetResult().Count);

    var restartedAgain = new LocalJsonReminderProvider(fixture.RemindersPath, "local-test", clock);
    Equal(0, restartedAgain.GetMissedAsync().GetAwaiter().GetResult().Count);
}

static void TestReminderSnoozeCompleteDelete()
{
    using var fixture = PersonalToolFixture.Create();
    var now = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
    var clock = new ManualTimeProvider(now);
    var provider = new LocalJsonReminderProvider(fixture.RemindersPath, clock: clock);
    var created = provider.CreateAsync(new CreateReminderCommand("休息", now.AddMinutes(-1)))
        .GetAwaiter().GetResult();
    provider.TryMarkTriggeredIfDueAsync(created.Id, created.Revision, now)
        .GetAwaiter().GetResult();

    clock.SetUtcNow(now.AddMinutes(1));
    var snoozed = provider.SnoozeAsync(created.Id, now.AddMinutes(10)).GetAwaiter().GetResult();
    Equal(now.AddMinutes(10), snoozed.DueAt);
    Equal(null, snoozed.LastTriggeredAt);
    Equal(ReminderStatus.Pending, snoozed.Status);
    Equal(3L, snoozed.Revision);

    var completed = provider.CompleteAsync(created.Id).GetAwaiter().GetResult();
    Equal(ReminderStatus.Completed, completed.Status);
    Equal(4L, completed.Revision);
    Equal(0, provider.GetPendingAsync().GetAwaiter().GetResult().Count);
    Equal(true, provider.DeleteAsync(created.Id).GetAwaiter().GetResult());
    Equal(false, provider.DeleteAsync(created.Id).GetAwaiter().GetResult());
}

static void TestReminderTriggerRejectsStaleSnapshot()
{
    using var fixture = PersonalToolFixture.Create();
    var now = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
    var clock = new ManualTimeProvider(now);
    var provider = new LocalJsonReminderProvider(fixture.RemindersPath, clock: clock);

    var completedSnapshot = provider.CreateAsync(
        new CreateReminderCommand("已完成", now.AddMinutes(-1))).GetAwaiter().GetResult();
    provider.CompleteAsync(completedSnapshot.Id).GetAwaiter().GetResult();
    Equal(null, provider.TryMarkTriggeredIfDueAsync(
        completedSnapshot.Id,
        completedSnapshot.Revision,
        now).GetAwaiter().GetResult());

    var snoozedSnapshot = provider.CreateAsync(
        new CreateReminderCommand("已稍后", now.AddMinutes(-1))).GetAwaiter().GetResult();
    provider.SnoozeAsync(snoozedSnapshot.Id, now.AddMinutes(10)).GetAwaiter().GetResult();
    Equal(null, provider.TryMarkTriggeredIfDueAsync(
        snoozedSnapshot.Id,
        snoozedSnapshot.Revision,
        now).GetAwaiter().GetResult());

    var deletedSnapshot = provider.CreateAsync(
        new CreateReminderCommand("已删除", now.AddMinutes(-1))).GetAwaiter().GetResult();
    provider.DeleteAsync(deletedSnapshot.Id).GetAwaiter().GetResult();
    Equal(null, provider.TryMarkTriggeredIfDueAsync(
        deletedSnapshot.Id,
        deletedSnapshot.Revision,
        now).GetAwaiter().GetResult());
}

static void TestConcurrentReminderWrites()
{
    using var fixture = PersonalToolFixture.Create();
    var now = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
    var provider = new LocalJsonReminderProvider(
        fixture.RemindersPath,
        clock: new ManualTimeProvider(now));
    var writes = Enumerable.Range(0, 40)
        .Select(index => provider.CreateAsync(new CreateReminderCommand(
            $"提醒 {index}",
            now.AddMinutes(index + 1))))
        .ToArray();
    Task.WhenAll(writes).GetAwaiter().GetResult();

    var restarted = new LocalJsonReminderProvider(fixture.RemindersPath);
    Equal(40, restarted.GetPendingAsync().GetAwaiter().GetResult().Count);
    using var document = JsonDocument.Parse(File.ReadAllText(fixture.RemindersPath));
    Equal(40, document.RootElement.GetProperty("items").GetArrayLength());
    Equal(0, Directory.GetFiles(fixture.Root, "*.tmp").Length);
}

static void TestNoteSearchArchiveDelete()
{
    using var fixture = PersonalToolFixture.Create();
    var now = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
    var clock = new ManualTimeProvider(now);
    var provider = new LocalJsonNoteProvider(fixture.NotesPath, "local-notes", clock);
    var design = provider.CreateAsync(new CreateNoteCommand(
        "Provider 接口必须保持稳定",
        Title: "LightPet 设计",
        Tags: ["项目", "接口"],
        Source: "pet-menu",
        ExternalId: "note-7"))
        .GetAwaiter().GetResult();
    provider.CreateAsync(new CreateNoteCommand("今天买牛奶", Tags: ["生活"]))
        .GetAwaiter().GetResult();

    Equal(1, provider.SearchAsync(new NoteSearchQuery(Text: "provider")).GetAwaiter().GetResult().Count);
    Equal(1, provider.SearchAsync(new NoteSearchQuery(Tags: ["接口"])).GetAwaiter().GetResult().Count);

    clock.SetUtcNow(now.AddMinutes(2));
    var updated = provider.UpdateAsync(design.Id, new UpdateNoteCommand(
        "Provider 接口与 JSON 格式必须稳定",
        Title: "架构决定",
        Tags: ["项目", "接口", "架构"]))
        .GetAwaiter().GetResult();
    Equal(2L, updated.Revision);
    Equal(3, updated.Tags.Count);
    Equal(now.AddMinutes(2), updated.UpdatedAt);

    var archived = provider.ArchiveAsync(design.Id).GetAwaiter().GetResult();
    Equal(NoteStatus.Archived, archived.Status);
    Equal(0, provider.SearchAsync(new NoteSearchQuery(Text: "JSON")).GetAwaiter().GetResult().Count);
    Equal(1, provider.SearchAsync(new NoteSearchQuery(Text: "JSON", IncludeArchived: true))
        .GetAwaiter().GetResult().Count);

    var restarted = new LocalJsonNoteProvider(fixture.NotesPath, "local-notes", clock);
    Equal(NoteStatus.Archived, restarted.GetAsync(design.Id).GetAwaiter().GetResult()!.Status);
    Equal(true, restarted.DeleteAsync(design.Id).GetAwaiter().GetResult());
    Equal(null, restarted.GetAsync(design.Id).GetAwaiter().GetResult());
}

static void TestCorruptJsonRecovery()
{
    using var fixture = PersonalToolFixture.Create();
    File.WriteAllText(fixture.RemindersPath, "{ this is not valid json");
    var provider = new LocalJsonReminderProvider(fixture.RemindersPath);
    Equal(0, provider.GetPendingAsync().GetAwaiter().GetResult().Count);
    Equal(1, Directory.GetFiles(fixture.Root, "reminders.json.corrupt-*").Length);

    provider.CreateAsync(new CreateReminderCommand("恢复后的提醒", DateTimeOffset.UtcNow.AddHours(1)))
        .GetAwaiter().GetResult();
    var restarted = new LocalJsonReminderProvider(fixture.RemindersPath);
    Equal(1, restarted.GetPendingAsync().GetAwaiter().GetResult().Count);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void Throws<T>(Action action)
    where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

internal sealed class PackFixture : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private PackFixture(string root, PetPackManifest manifest)
    {
        Root = root;
        Manifest = manifest;
    }

    public string Root { get; }

    public PetPackManifest Manifest { get; }

    public static PackFixture Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "lightpet-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var actions = new List<ActionDefinition>();
        foreach (var id in new[] { "idle", "walk-left", "walk-right", "touch-head" })
        {
            var relative = Path.Combine("actions", id, "loop");
            var directory = Path.Combine(root, relative);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "frame_000_250.png"), CreatePngHeader(1000, 1000));
            actions.Add(new ActionDefinition
            {
                Id = id,
                Phases =
                [
                    new PhaseDefinition { Kind = AnimationPhase.Loop, Folder = relative },
                ],
            });
        }

        var manifest = new PetPackManifest
        {
            Id = "test-pet",
            DisplayName = "Test Pet",
            HitRegions =
            [
                new HitRegionDefinition { Id = "head", X = 300, Y = 100, Width = 400, Height = 300 },
                new HitRegionDefinition { Id = "body", X = 300, Y = 400, Width = 400, Height = 400 },
            ],
            Actions = actions,
        };
        var fixture = new PackFixture(root, manifest);
        fixture.SaveManifest();
        return fixture;
    }

    public void SaveManifest() =>
        File.WriteAllText(Path.Combine(Root, "pet.json"), JsonSerializer.Serialize(Manifest, JsonOptions));

    public void SetFirstFrameDimensions(int width, int height)
    {
        var path = Path.Combine(Root, "actions", "idle", "loop", "frame_000_250.png");
        File.WriteAllBytes(path, CreatePngHeader(width, height));
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private static byte[] CreatePngHeader(int width, int height)
    {
        var header = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8, 4), 13);
        new byte[] { 73, 72, 68, 82 }.CopyTo(header, 12);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20, 4), height);
        return header;
    }
}

internal sealed class PersonalToolFixture : IDisposable
{
    private PersonalToolFixture(string root)
    {
        Root = root;
        RemindersPath = Path.Combine(root, "reminders.json");
        NotesPath = Path.Combine(root, "notes.json");
    }

    public string Root { get; }

    public string RemindersPath { get; }

    public string NotesPath { get; }

    public static PersonalToolFixture Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "lightpet-personal-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new PersonalToolFixture(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset value) => _utcNow = value.ToUniversalTime();
}

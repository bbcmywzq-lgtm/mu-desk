using Toolbox.Core;

var defaults = new ToolboxSettings();
Assert(defaults.RunAtStartup, "工具箱默认应随 Windows 启动。");
Assert(defaults.MouseRingEnabled, "四向轮盘默认应启用。");
Assert(defaults.LightPetEnabled, "桌面伙伴默认应启用。");
Assert(defaults.DesktopOrganizerEnabled, "栖格默认应启用。");
Assert(!defaults.StartMinimized, "首次启动应显示工具箱窗口。");
Assert(defaults.EffectCapture.DefaultDurationSeconds == 2, "动态拾取默认应录制 2 秒。");
Assert(defaults.EffectCapture.CountdownEnabled, "动态拾取默认应启用倒计时。");
Assert(defaults.EffectCapture.GlobalHotkey == "Ctrl+Alt+R", "动态拾取默认应提供可直接使用的全局快捷键。");
EffectCaptureHotkeyMigrationIsExplicit();
Assert(defaults.FileShelf.Enabled, "临时货架默认应启用。");
Assert(defaults.FileShelf.DockSide == "Right", "临时货架默认应停靠右侧。");
Assert(defaults.FileShelf.VerticalPosition == 0.5, "临时货架默认应位于屏幕边缘中部。");
Assert(!defaults.Cue.Enabled, "Mujun Cue 升级后默认应保持关闭，避免未经确认占用 Caps Lock。");
Assert(defaults.Cue.FocusHoldEnabled, "Mujun Cue 启用后应默认提供按住聚焦。");
Assert(defaults.Cue.HoldThresholdMilliseconds == 160, "按住聚焦默认阈值应为 160 毫秒。");
Assert(defaults.Cue.AnimationMilliseconds == 220, "聚焦动画默认应使用柔和的 220 毫秒过渡。");
Assert(defaults.Cue.ZoomFactor == 2.0, "按住聚焦默认倍率应为 2 倍。");
CueSettingsNormalizeSafely();
CueCameraIsContinuous();

var fixtureRoot = Path.Combine(
    Path.GetTempPath(),
    $"mu-desk-cursor-gallery-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(fixtureRoot);
try
{
    TargetLibraryWins(fixtureRoot);
    LegacyLibraryIsCopiedWithoutDeletion(fixtureRoot);
    MissingLibrariesRemainAnEmptyTarget(fixtureRoot);
    MigrationFailureFallsBackWithARealError(fixtureRoot);
    ToolboxSettingsAreCopiedWithoutDeletion(fixtureRoot);
    ToolboxSettingsMigrationFailureFallsBack(fixtureRoot);
    KeyFrameSelectionPreservesMotionPeak();
    PackageValidationRejectsUnsafeData();
    JsonRpcBufferHandlesFragmentedLines();
    JsonRpcDiagnosticsHideSecrets();
    CispPublicBankProvidesFirstRelease();
    CispLatestSetsRemainSeparated();
    CispWrongAnswersStayCollectedUntilMastered(fixtureRoot);
    CispCorrectAnswersCanBeExcludedAndRestored(fixtureRoot);
    CispSessionSizeCanBeCustomized(fixtureRoot);
    CispWrongBookExportsForAi(fixtureRoot);
    FileShelfSettingsNormalizeSafely();
    FileShelfGroupsAndPinsStayDeterministic(fixtureRoot);
    FileShelfLimitIsEnforced(fixtureRoot);
    FileShelfStoreRecoversFromBackup(fixtureRoot);
}
finally
{
    var resolvedFixture = Path.GetFullPath(fixtureRoot);
    var resolvedTemp = Path.GetFullPath(Path.GetTempPath());
    Assert(
        resolvedFixture.StartsWith(resolvedTemp, StringComparison.OrdinalIgnoreCase),
        "测试清理目录必须位于系统临时目录内。");
    if (Directory.Exists(resolvedFixture))
    {
        Directory.Delete(resolvedFixture, recursive: true);
    }
}

Console.WriteLine("Toolbox core tests passed.");
return;

static void TargetLibraryWins(string root)
{
    var caseRoot = Path.Combine(root, "target-wins");
    var target = Path.Combine(caseRoot, "new", "library.json");
    var legacy = Path.Combine(caseRoot, "legacy", "library.json");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
    File.WriteAllText(target, "target");
    File.WriteAllText(legacy, "legacy");

    var result = CursorGalleryLibraryMigration.Establish(target, legacy);

    Assert(result.TargetEstablished, "已有 MU Desk 库时应直接使用目标库。");
    Assert(!result.Migrated, "已有目标库时不应再次迁移。");
    Assert(Path.GetFullPath(result.LibraryPath) == Path.GetFullPath(target), "应选择目标库路径。");
    Assert(File.ReadAllText(target) == "target", "迁移不得覆盖已有目标库。");
}

static void CueCameraIsContinuous()
{
    foreach (var fps in new[] { 30, 60, 165 })
    {
        var camera = new CueCameraMotion();
        camera.AimAt(2, 700, 300);
        for (var frame = 0; frame < fps; frame++)
        {
            camera.Step(1d / fps, 220);
            Assert(Math.Abs(700 * camera.Zoom + camera.X - 700) < 1e-8,
                "缩放过程中原指向内容应保持原屏幕位置，不因边界钳制突然居中。");
        }
        Assert(Math.Abs(camera.Zoom - 2) < 1e-7, "不同刷新率下镜头应达到相同状态。");
    }
    var interrupted = new CueCameraMotion();
    interrupted.AimAt(3, 700, 300);
    interrupted.Step(.06, 220);
    var zoom = interrupted.Zoom;
    var velocity = interrupted.ZoomVelocity;
    var x = interrupted.X;
    interrupted.Aim(1, 0, 0);
    Assert(interrupted.Zoom == zoom && interrupted.ZoomVelocity == velocity && interrupted.X == x,
        "中途松开不能重置位置或速度。");
    interrupted.Step(.03, 220);
    zoom = interrupted.Zoom; x = interrupted.X; velocity = interrupted.ZoomVelocity;
    interrupted.AimAt(2.25, 1100, 400);
    Assert(interrupted.Zoom == zoom && interrupted.X == x && interrupted.ZoomVelocity == velocity,
        "复原中重按或调倍率不得使当前状态跳变。");
    interrupted.Aim(1, 0, 0);
    interrupted.Step(2, 220);
    Assert(interrupted.IsSettled && Math.Abs(interrupted.Zoom - 1) < 1e-8 && Math.Abs(interrupted.X) < 1e-6,
        "复原应收敛到真实桌面。");
    var oneStep = new CueCameraMotion();
    var splitStep = new CueCameraMotion();
    oneStep.AimAt(2, 500, 800); splitStep.AimAt(2, 500, 800);
    oneStep.Step(.1, 220);
    foreach (var dt in new[] { .006, .025, .009, .04, .02 }) splitStep.Step(dt, 220);
    Assert(Math.Abs(oneStep.Zoom - splitStep.Zoom) < 1e-12 && Math.Abs(oneStep.X - splitStep.X) < 1e-9,
        "丢帧和不均匀时间步不应改变镜头轨迹。");
}

static void CueSettingsNormalizeSafely()
{
    var legacy = new CueSettings { AnimationMilliseconds = 120 };
    legacy.Normalize();
    Assert(legacy.AnimationMilliseconds == 220, "Cue 首版生硬的 120 毫秒默认值应迁移到 220 毫秒。");

    var settings = new CueSettings
    {
        HoldThresholdMilliseconds = 2,
        AnimationMilliseconds = 900,
        ZoomFactor = double.NaN,
        SpotlightRadius = -20,
        SpotlightOpacity = 5,
        MagnifierRadius = double.PositiveInfinity,
        MagnifierZoom = 99,
        SpotlightShape = "triangle",
        MagnifierShape = "RoundedRectangle",
        ScreenshotFolder = null!,
    };

    settings.Normalize();

    Assert(settings.HoldThresholdMilliseconds == 80, "Cue 按住阈值必须安全夹取。");
    Assert(settings.AnimationMilliseconds == 500, "Cue 动画时长必须安全夹取。");
    Assert(settings.ZoomFactor == 2.0, "Cue 非有限聚焦倍率必须回到默认值。");
    Assert(settings.SpotlightRadius == 60, "Cue 聚光半径必须安全夹取。");
    Assert(settings.SpotlightOpacity == 0.9, "Cue 遮罩透明度必须安全夹取。");
    Assert(settings.MagnifierRadius == 240, "Cue 非有限镜片半径必须回到默认值。");
    Assert(settings.MagnifierZoom == 5.0, "Cue 镜片倍率必须安全夹取。");
    Assert(settings.SpotlightShape == "Circle", "Cue 未知聚光形状必须回到圆形。");
    Assert(settings.MagnifierShape == "RoundedRectangle", "Cue 已知镜片形状必须保留。");
    Assert(settings.ScreenshotFolder.Length == 0, "Cue 截图目录不能保留 null。");
}

static void LegacyLibraryIsCopiedWithoutDeletion(string root)
{
    var caseRoot = Path.Combine(root, "copy-legacy");
    var target = Path.Combine(caseRoot, "new", "library.json");
    var legacy = Path.Combine(caseRoot, "legacy", "library.json");
    Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
    File.WriteAllText(legacy, "legacy-content");

    var result = CursorGalleryLibraryMigration.Establish(target, legacy);

    Assert(result.TargetEstablished && result.Migrated, "首次使用时应建立 MU Desk 库副本。");
    Assert(File.Exists(target), "目标 library.json 应存在。");
    Assert(File.ReadAllText(target) == "legacy-content", "目标库内容应与旧库一致。");
    Assert(File.Exists(legacy), "旧 library.json 必须保留。");
    Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(target)!, "*.tmp").Any(), "迁移后不应残留临时文件。");
}

static void MissingLibrariesRemainAnEmptyTarget(string root)
{
    var caseRoot = Path.Combine(root, "missing");
    var target = Path.Combine(caseRoot, "new", "library.json");
    var legacy = Path.Combine(caseRoot, "legacy", "library.json");

    var result = CursorGalleryLibraryMigration.Establish(target, legacy);

    Assert(!result.TargetEstablished, "没有来源时不应伪造目标库。");
    Assert(result.ErrorMessage is null, "单纯没有库不是迁移故障。");
    Assert(Path.GetFullPath(result.LibraryPath) == Path.GetFullPath(target), "未来写入位置仍应是 MU Desk 目标路径。");
}

static void MigrationFailureFallsBackWithARealError(string root)
{
    var caseRoot = Path.Combine(root, "failure");
    var blocker = Path.Combine(caseRoot, "blocked-parent");
    var target = Path.Combine(blocker, "library.json");
    var legacy = Path.Combine(caseRoot, "legacy", "library.json");
    Directory.CreateDirectory(caseRoot);
    Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
    File.WriteAllText(blocker, "this file blocks directory creation");
    File.WriteAllText(legacy, "legacy-content");

    var result = CursorGalleryLibraryMigration.Establish(target, legacy);

    Assert(!result.TargetEstablished, "迁移失败时不能声称目标库已经建立。");
    Assert(result.UsingLegacyFallback, "迁移失败时应保持旧库只读兼容。");
    Assert(!string.IsNullOrWhiteSpace(result.ErrorMessage), "迁移失败必须返回真实错误。");
    Assert(Path.GetFullPath(result.LibraryPath) == Path.GetFullPath(legacy), "失败后应继续读取旧库。");
    Assert(File.Exists(legacy), "失败时旧库必须保留。");
}

static void ToolboxSettingsAreCopiedWithoutDeletion(string root)
{
    var caseRoot = Path.Combine(root, "toolbox-settings-copy");
    var target = Path.Combine(caseRoot, "new", "settings.json");
    var legacy = Path.Combine(caseRoot, "legacy", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
    File.WriteAllText(legacy, "{\"RunAtStartup\":false}");

    var result = ToolboxSettingsMigration.Establish(target, legacy);

    Assert(result.TargetEstablished && result.Migrated, "工具箱设置首次迁移应建立目标副本。");
    Assert(File.ReadAllText(target) == File.ReadAllText(legacy), "设置副本必须保持原内容。");
    Assert(File.Exists(legacy), "迁移不得删除旧工具箱设置。");
    Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(target)!, "*.tmp").Any(), "设置迁移后不应残留临时文件。");
}

static void ToolboxSettingsMigrationFailureFallsBack(string root)
{
    var caseRoot = Path.Combine(root, "toolbox-settings-failure");
    var blocker = Path.Combine(caseRoot, "blocked-parent");
    var target = Path.Combine(blocker, "settings.json");
    var legacy = Path.Combine(caseRoot, "legacy", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
    File.WriteAllText(blocker, "this file blocks directory creation");
    File.WriteAllText(legacy, "{}");

    var result = ToolboxSettingsMigration.Establish(target, legacy);

    Assert(result.UsingLegacyFallback, "设置迁移失败应继续读取旧设置。");
    Assert(!result.TargetEstablished, "设置迁移失败不能报告目标已经建立。");
    Assert(!string.IsNullOrWhiteSpace(result.ErrorMessage), "设置迁移失败必须保留真实错误。");
    Assert(Path.GetFullPath(result.SettingsPath) == Path.GetFullPath(legacy), "失败时应返回旧设置路径。");
}

static void KeyFrameSelectionPreservesMotionPeak()
{
    var frames = Enumerable.Range(0, 60)
        .Select(index => new EffectFrameMetric(
            index,
            index * 16,
            index == 23 ? 100 : index % 9 == 0 ? 4 : 1,
            index == 24 ? 50 : 0))
        .ToArray();

    var selected = EffectKeyFrameSelector.Select(frames, 12);

    Assert(selected.Count == 12, "关键帧选择应返回请求数量。");
    Assert(selected[0].Index == 0 && selected[^1].Index == 59, "关键帧选择必须保留起止帧。");
    Assert(selected.Any(frame => frame.Index is 23 or 24), "关键帧选择必须保留快速过冲峰值。");
    Assert(selected.SequenceEqual(selected.OrderBy(frame => frame.TimestampMilliseconds)), "关键帧必须按时间排序。");
    Assert(
        selected.SequenceEqual(EffectKeyFrameSelector.Select(frames, 12)),
        "相同输入的关键帧选择必须确定性一致。");
}

static void PackageValidationRejectsUnsafeData()
{
    var manifest = new EffectCaptureManifest
    {
        PackageId = "test",
        DurationMilliseconds = 1000,
        LogicalWidth = 321,
        LogicalHeight = 181,
        EncodedWidth = 322,
        EncodedHeight = 182,
        Frames =
        [
            new EffectFrameMetadata(0, "frames/frame-000.png", 0, 0),
            new EffectFrameMetadata(1, "../secret.png", 1000, 2),
        ],
    };

    Assert(manifest.Validate().Any(error => error.Contains("相对路径", StringComparison.Ordinal)), "越界路径必须被拒绝。");
    manifest.Frames[1] = new EffectFrameMetadata(1, "frames/frame-001.png", 1000, 2);
    Assert(manifest.Validate().Count == 0, "合法奇数选区和偶数编码尺寸应通过验证。");
}

static void JsonRpcBufferHandlesFragmentedLines()
{
    var buffer = new JsonRpcLineBuffer();
    Assert(buffer.Push("{\"id\":1,").Count == 0, "不完整 JSON 行不能提前输出。");
    var documents = buffer.Push("\"result\":{}}\r\n{\"method\":\"turn/completed\"}\n");
    try
    {
        Assert(documents.Count == 2, "完整的两条 JSON-RPC 行应分别输出。");
        Assert(documents[0].RootElement.GetProperty("id").GetInt32() == 1, "分片响应应被正确重组。");
        Assert(documents[1].RootElement.GetProperty("method").GetString() == "turn/completed", "通知方法应保留。");
    }
    finally
    {
        foreach (var document in documents)
        {
            document.Dispose();
        }
    }
}

static void JsonRpcDiagnosticsHideSecrets()
{
    var message = CodexJsonRpc.SanitizeDiagnostic("server failed Bearer top-secret-token\nsecond line");
    Assert(!message.Contains("top-secret", StringComparison.Ordinal), "诊断信息不得保留 bearer token。");
    Assert(message.Contains("已隐藏敏感信息", StringComparison.Ordinal), "诊断信息应标明敏感内容已隐藏。");
}

static void CispPublicBankProvidesFirstRelease()
{
    var sourcePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "Toolbox.App", "Assets", "Cisp", "public-bank.md"));
    Assert(File.Exists(sourcePath), "CISP 离线公开题源必须随应用保留。");
    var questions = CispQuestionBank.Parse(File.ReadAllText(sourcePath));
    Assert(questions.Count >= 800, $"CISP 扩展版至少需要 800 道可交互题，实际为 {questions.Count} 道。");
    Assert(questions.All(question => question.Options.Count == 4), "CISP 交互题必须有四个选项。");
    Assert(questions.Any(question => question.ImageNames.Count > 0), "CISP 题库必须保留图片题引用。");
    var imageRoot = Path.Combine(Path.GetDirectoryName(sourcePath)!, "pic");
    var missingImages = questions
        .SelectMany(question => question.ImageNames)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(imageName => !File.Exists(Path.Combine(imageRoot, imageName)))
        .ToArray();
    Assert(missingImages.Length == 0, $"CISP 图片题缺少素材：{string.Join(", ", missingImages)}");
    Console.WriteLine($"CISP interactive bank: {questions.Count} questions.");
}

static void CispLatestSetsRemainSeparated()
{
    var sourcePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "Toolbox.App", "Assets", "Cisp", "2026-sets.json"));
    var catalog = CispQuestionSetCatalog.Load(sourcePath);
    Assert(catalog.Sets.Count == 8, "2026 最新题库必须保留为八个独立套题入口。");
    Assert(catalog.Sets.Sum(set => set.Questions.Count) == 797, "八套题应共保留 797 道可交互题。");
    Assert(catalog.Sets.All(set => set.Questions.All(question => question.Id.StartsWith(set.Id, StringComparison.Ordinal))),
        "每道套题必须使用所属套题的独立编号，防止跨套混抽。");
    var ids = catalog.Sets.SelectMany(set => set.Questions).Select(question => question.Id).ToArray();
    Assert(ids.Length == ids.Distinct(StringComparer.Ordinal).Count(), "八套题的题目编号必须全局唯一。");
    var imageRoot = Path.Combine(Path.GetDirectoryName(sourcePath)!, "pic");
    var missingImages = catalog.Sets
        .SelectMany(set => set.Questions)
        .SelectMany(question => question.ImageNames)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(imageName => !File.Exists(Path.Combine(imageRoot, imageName)))
        .ToArray();
    Assert(missingImages.Length == 0, $"八套题缺少图片素材：{string.Join(", ", missingImages)}");
    Console.WriteLine($"CISP source sets: {catalog.Sets.Count} isolated sets, {ids.Length} questions.");
}

static void CispWrongAnswersStayCollectedUntilMastered(string root)
{
    var path = Path.Combine(root, "cisp-wrong-book", "progress.json");
    var store = new CispProgressStore(path);
    var progress = store.Load();
    store.Record(progress, "question-1", correct: false);
    Assert(progress.Questions["question-1"].IsInWrongBook == true, "答错后必须自动收进错题本。");
    store.Record(progress, "question-1", correct: true);
    Assert(progress.Questions["question-1"].IsInWrongBook == true, "后续答对不应自动删除错题。");
    store.RemoveFromWrongBook(progress, "question-1");
    Assert(progress.Questions["question-1"].IsInWrongBook == false, "标记已掌握后应移出错题本。");
    Assert(store.Load().Questions["question-1"].IsInWrongBook == false, "错题本移出状态必须持久化。");
    store.SetMarked(progress, "question-1", marked: true);
    Assert(store.Load().Questions["question-1"].IsMarked, "蒙对或不确定的题应能持久标记。");
    store.SetMarked(progress, "question-1", marked: false);
    Assert(!store.Load().Questions["question-1"].IsMarked, "取消标记必须持久化。");
}

static void CispCorrectAnswersCanBeExcludedAndRestored(string root)
{
    var path = Path.Combine(root, "cisp-excluded-draw", "progress.json");
    var store = new CispProgressStore(path);
    var progress = store.Load();

    store.Record(progress, "correct-question", correct: true, selectedIndex: 2);
    store.SetExcludedFromDraw(progress, "correct-question", excluded: true);
    Assert(progress.Questions["correct-question"].IsExcludedFromDraw, "答对后应能设置以后不再抽取。");
    Assert(store.Load().Questions["correct-question"].IsExcludedFromDraw, "不再抽取状态必须持久化。");

    store.SetExcludedFromDraw(progress, "correct-question", excluded: false);
    Assert(!store.Load().Questions["correct-question"].IsExcludedFromDraw, "用户应能恢复题目的随机抽取资格。");

    store.Record(progress, "wrong-question", correct: false, selectedIndex: 1);
    var rejected = false;
    try
    {
        store.SetExcludedFromDraw(progress, "wrong-question", excluded: true);
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }

    Assert(rejected, "未答对的题目不能设为以后不再抽取。");
}

static void CispSessionSizeCanBeCustomized(string root)
{
    var path = Path.Combine(root, "cisp-session-size", "progress.json");
    var store = new CispProgressStore(path);
    var progress = store.Load();
    Assert(progress.SessionSize == 100, "每组题数默认应为 100。");
    store.SetSessionSize(progress, 37);
    Assert(store.Load().SessionSize == 37, "自定义每组题数必须持久化。");

    var rejected = false;
    try
    {
        store.SetSessionSize(progress, 0);
    }
    catch (ArgumentOutOfRangeException)
    {
        rejected = true;
    }

    Assert(rejected, "每组题数不能小于 1。");
}

static void FileShelfSettingsNormalizeSafely()
{
    var settings = new FileShelfSettings
    {
        DockSide = "somewhere",
        DisplayDeviceName = "  DISPLAY-TEST  ",
        VerticalPosition = double.NaN,
    };
    settings.Normalize();
    Assert(settings.DockSide == "Right", "未知停靠侧应安全回退到右侧。");
    Assert(settings.DisplayDeviceName == "DISPLAY-TEST", "显示器设备名应去除首尾空白。");
    Assert(settings.VerticalPosition == 0.5, "非有限位置应回退到中部。");
    settings.DockSide = "left";
    settings.VerticalPosition = 3;
    settings.Normalize();
    Assert(settings.DockSide == "Left", "左侧设置应忽略大小写并规范化。");
    Assert(settings.VerticalPosition == 0.92, "超界位置应限制在安全范围内。");
}

static void EffectCaptureHotkeyMigrationIsExplicit()
{
    var legacy = new EffectCaptureSettings { GlobalHotkey = null, GlobalHotkeyConfigured = null };
    legacy.Normalize();
    Assert(legacy.GlobalHotkey == EffectCaptureSettings.DefaultGlobalHotkey, "旧设置首次升级时应获得默认拾取快捷键。");

    var disabled = new EffectCaptureSettings { GlobalHotkey = null, GlobalHotkeyConfigured = true };
    disabled.Normalize();
    Assert(disabled.GlobalHotkey is null, "用户主动关闭快捷键后不应被默认值重新打开。");
}

static void FileShelfGroupsAndPinsStayDeterministic(string root)
{
    var caseRoot = Path.Combine(root, "file-shelf-group");
    Directory.CreateDirectory(caseRoot);
    var first = Path.Combine(caseRoot, "first.txt");
    var second = Path.Combine(caseRoot, "second.txt");
    File.WriteAllText(first, "one");
    File.WriteAllText(second, "two");
    var document = new FileShelfDocument();

    var batch = FileShelfOperations.AddBatch(document, [first, second, first]);

    Assert(document.Batches.Count == 1, "一次放入必须只建立一个批次。");
    Assert(batch.Items.Count == 2, "同一次放入中的重复路径应去重。");
    Assert(batch.Items[0].DisplayName == "first.txt" && batch.Items[1].DisplayName == "second.txt", "批次顺序必须稳定。");
    batch.Items[0].IsPinned = true;
    var removed = FileShelfOperations.RemoveItems(
        document,
        batch.Items.Select(item => item.Id).ToArray(),
        includePinned: false);
    Assert(removed.Count == 1, "成功拖出清理只能移除未固定项目。");
    Assert(document.ItemCount == 1 && document.Batches[0].Items[0].IsPinned, "固定项目必须保留。");
    Assert(File.Exists(first) && File.Exists(second), "元数据操作不得删除原文件。");
}

static void FileShelfLimitIsEnforced(string root)
{
    var caseRoot = Path.Combine(root, "file-shelf-limit");
    Directory.CreateDirectory(caseRoot);
    var available = Path.Combine(caseRoot, "available.txt");
    File.WriteAllText(available, "test");
    var document = new FileShelfDocument
    {
        Batches =
        [
            new FileShelfBatch
            {
                Items = Enumerable.Range(0, FileShelfDocument.MaximumItemCount)
                    .Select(index => new FileShelfItem
                    {
                        Id = $"item-{index}",
                        Path = available,
                        DisplayName = "available.txt",
                        Kind = FileShelfItemKind.File,
                        Order = index,
                    })
                    .ToList(),
            },
        ],
    };

    var threw = false;
    try
    {
        FileShelfOperations.AddBatch(document, [available]);
    }
    catch (InvalidOperationException)
    {
        threw = true;
    }

    Assert(threw, "达到 200 项上限时必须拒绝新增。");
    Assert(document.ItemCount == FileShelfDocument.MaximumItemCount, "拒绝新增后现有货架不得变化。");
}

static void FileShelfStoreRecoversFromBackup(string root)
{
    var caseRoot = Path.Combine(root, "file-shelf-store");
    Directory.CreateDirectory(caseRoot);
    var source = Path.Combine(caseRoot, "source.txt");
    File.WriteAllText(source, "source-content");
    var storePath = Path.Combine(caseRoot, "data", "shelf.json");
    var store = new FileShelfStore(storePath);
    var first = new FileShelfDocument();
    FileShelfOperations.AddBatch(first, [source]);
    Assert(store.TrySave(first, out var firstError), $"首次货架保存应成功：{firstError}");
    var second = first.Clone();
    second.Batches[0].Items[0].IsPinned = true;
    Assert(store.TrySave(second, out var secondError), $"第二次货架保存应成功：{secondError}");
    File.WriteAllText(storePath, "{ broken json");

    var recovered = store.Load();

    Assert(recovered.RecoveredFromBackup, "主数据损坏时应从最近备份恢复。");
    Assert(recovered.Document.ItemCount == 1, "备份恢复必须保留项目。");
    Assert(!recovered.Document.Batches[0].Items[0].IsPinned, "备份应是上一次可靠版本。");
    Assert(File.ReadAllText(storePath) == "{ broken json", "读取备份不得静默覆盖损坏证据。");
    var serialized = File.ReadAllText(storePath + ".bak");
    Assert(serialized.Contains("source.txt", StringComparison.Ordinal), "路径只能出现在接受的货架元数据中。");
}

static void CispWrongBookExportsForAi(string root)
{
    var caseRoot = Path.Combine(root, "cisp-ai-export");
    var imageRoot = Path.Combine(caseRoot, "images");
    var outputRoot = Path.Combine(caseRoot, "output");
    Directory.CreateDirectory(imageRoot);
    File.WriteAllBytes(Path.Combine(imageRoot, "diagram.png"), [1, 2, 3, 4]);
    var question = new CispQuestion(
        "question-1",
        "7",
        "信息安全管理",
        "测试题干",
        ["选项一", "选项二", "选项三", "选项四"],
        2,
        "测试解析",
        ["diagram.png"]);
    var progress = new CispStudyProgress
    {
        Questions = new Dictionary<string, CispQuestionProgress>
        {
            [question.Id] = new() { IsInWrongBook = true, LastSelectedIndex = 1 },
        },
    };

    var result = CispWrongBookExporter.Export(
        [question],
        progress,
        imageRoot,
        outputRoot,
        new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.FromHours(8)));
    Assert(File.Exists(result.FilePath), "AI 错题导出必须生成 ZIP 文件。");
    using var archive = System.IO.Compression.ZipFile.OpenRead(result.FilePath);
    Assert(archive.Entries.Any(entry => entry.FullName == "错题请教.md"), "AI 导出必须包含 Markdown 题目。");
    Assert(archive.Entries.Any(entry => entry.FullName.StartsWith("images/", StringComparison.Ordinal)), "AI 导出必须携带题图。");
    var markdownEntry = archive.GetEntry("错题请教.md")!;
    using var reader = new StreamReader(markdownEntry.Open());
    var markdown = reader.ReadToEnd();
    Assert(markdown.Contains("我最近选的：B", StringComparison.Ordinal), "AI 导出必须记录用户最近选项。");
    Assert(markdown.Contains("参考答案：C", StringComparison.Ordinal), "AI 导出必须包含参考答案。");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

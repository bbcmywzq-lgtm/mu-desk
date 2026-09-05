using MouseRing.Core;

var tests = new (string Name, Action Test)[]
{
    ("中心区域取消", () => Equal(Direction.None, DirectionResolver.Resolve(10, 10, 26))),
    ("上方解析", () => Equal(Direction.Up, DirectionResolver.Resolve(2, -80, 26))),
    ("右方解析", () => Equal(Direction.Right, DirectionResolver.Resolve(80, 2, 26))),
    ("下方解析", () => Equal(Direction.Down, DirectionResolver.Resolve(2, 80, 26))),
    ("左方解析", () => Equal(Direction.Left, DirectionResolver.Resolve(-80, 2, 26))),
    ("默认布局", TestDefaultLayout),
    ("设置范围收敛", TestNormalization),
    ("动作枚举兼容", TestActionEnumCompatibility),
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

static void TestDefaultLayout()
{
    var settings = new AppSettings();
    Equal(ActionKind.BringCodex, settings.GetAction(Direction.Up));
    Equal(ActionKind.RegionScreenshot, settings.GetAction(Direction.Right));
    Equal(ActionKind.ShowDesktop, settings.GetAction(Direction.Down));
    Equal(ActionKind.ClipboardHistory, settings.GetAction(Direction.Left));
}

static void TestNormalization()
{
    var settings = new AppSettings
    {
        TriggerDelayMs = 1,
        MenuDiameter = 999,
        UpAction = (ActionKind)999,
    };
    settings.Normalize();
    Equal(80, settings.TriggerDelayMs);
    Equal(360d, settings.MenuDiameter);
    Equal(ActionKind.None, settings.UpAction);
}

static void TestActionEnumCompatibility()
{
    Equal(0, (int)ActionKind.None);
    Equal(1, (int)ActionKind.BringCodex);
    Equal(2, (int)ActionKind.RegionScreenshot);
    Equal(3, (int)ActionKind.ShowDesktop);
    Equal(4, (int)ActionKind.ClipboardHistory);
    Equal(5, (int)ActionKind.TaskView);
    Equal(6, (int)ActionKind.WindowsSearch);
    Equal(7, (int)ActionKind.ToggleMute);
    Equal(8, (int)ActionKind.OpenFileExplorer);
    Equal(9, (int)ActionKind.PreviousWindow);
    Equal(10, (int)ActionKind.EffectCapture);
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

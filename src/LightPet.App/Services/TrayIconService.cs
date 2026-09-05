using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace LightPet.App.Services;

internal sealed record TrayPackItem(string Id, string Label);

internal sealed class TrayIconService : IDisposable
{
    private const int CallbackMessage = 0x8001;
    private const int NotifyAdd = 0x00000000;
    private const int NotifyDelete = 0x00000002;
    private const int NotifyMessage = 0x00000001;
    private const int NotifyIcon = 0x00000002;
    private const int NotifyTip = 0x00000004;
    private const int MouseRightButtonUp = 0x0205;
    private const int MouseLeftButtonDoubleClick = 0x0203;
    private const uint MenuString = 0x00000000;
    private const uint MenuSeparator = 0x00000800;
    private const uint MenuChecked = 0x00000008;
    private const uint MenuPopup = 0x00000010;
    private const uint TrackReturnCommand = 0x0100;
    private const uint TrackRightButton = 0x0002;
    private const uint ImageIcon = 1;
    private const uint LoadResourceShared = 0x00008000;
    private const int SmallIconWidth = 49;
    private const int SmallIconHeight = 50;
    private static readonly IntPtr ApplicationIconResource = new(32512);

    private const uint ShowPetCommand = 1;
    private const uint IdleCommand = 2;
    private const uint WalkLeftCommand = 3;
    private const uint WalkRightCommand = 4;
    private const uint TouchHeadCommand = 5;
    private const uint ClickThroughCommand = 6;
    private const uint ActivityCommand = 7;
    private const uint ExitCommand = 8;
    private const uint QuickToolsCommand = 9;
    private const uint EdgePatrolCommand = 10;
    private const uint FirstActionCommand = 100;
    private const uint HalfSizeCommand = 28;
    private const uint MiniSizeCommand = 29;
    private const uint SmallSizeCommand = 30;
    private const uint StandardSizeCommand = 31;
    private const uint LargeSizeCommand = 32;
    private const uint MaximumSizeCommand = 33;
    private const uint FirstPackCommand = 200;

    private static readonly (string Id, string Label)[] PreviewActions =
    [
        ("idle", "基础待机"),
        ("idle-random", "随机小动作"),
        ("walk-left", "向左行走"),
        ("walk-right", "向右行走"),
        ("raise", "提起 / 拖拽"),
        ("fall-land", "下落 / 落地"),
        ("touch-head", "摸头"),
        ("touch-body", "触碰身体"),
        ("pinch", "长按 / 捏起"),
        ("startup", "出现"),
        ("shutdown", "离场"),
        ("sleep", "睡眠"),
        ("think", "思考"),
        ("say", "说话"),
        ("happy", "开心"),
        ("sad", "难过"),
        ("surprised", "惊讶"),
        ("focus", "专注 / 处理中"),
        ("wave", "招手 / 问候"),
        ("reminder-alert", "提醒到点"),
        ("note-write", "记录便签"),
        ("edge-climb-left", "左侧攀爬"),
        ("edge-climb-right", "右侧攀爬"),
        ("edge-top-left", "顶边向左悬移"),
        ("edge-top-right", "顶边向右悬移"),
        ("observe-cursor", "观察鼠标"),
        ("head-rub", "持续摸头"),
        ("cheek-poke", "戳脸"),
        ("annoyed-dodge", "被点烦后躲开"),
        ("stretch", "伸懒腰"),
        ("hair-fix", "整理头发"),
        ("yawn", "打哈欠"),
        ("sit-rest", "坐下休息"),
    ];

    private readonly Action _showPet;
    private readonly Action _openPersonalTools;
    private readonly Action _playIdle;
    private readonly Action _walkLeft;
    private readonly Action _walkRight;
    private readonly Action _touchHead;
    private readonly Action _patrolEdges;
    private readonly Action<string> _previewAction;
    private readonly Action<int> _setSize;
    private readonly Action<string> _switchPack;
    private readonly IReadOnlyList<TrayPackItem> _packs;
    private readonly Action<bool> _setClickThrough;
    private readonly Action<bool> _setActivity;
    private readonly Action _exit;
    private readonly HwndSource _messageWindow;
    private NotifyIconData _iconData;
    private bool _clickThrough;
    private bool _activity = true;
    private int _size = 130;
    private string _packId;
    private bool _disposed;

    public TrayIconService(
        Action showPet,
        Action openPersonalTools,
        Action playIdle,
        Action walkLeft,
        Action walkRight,
        Action touchHead,
        Action patrolEdges,
        Action<string> previewAction,
        Action<int> setSize,
        IReadOnlyList<TrayPackItem> packs,
        string packId,
        Action<string> switchPack,
        Action<bool> setClickThrough,
        Action<bool> setActivity,
        Action exit)
    {
        _showPet = showPet;
        _openPersonalTools = openPersonalTools;
        _playIdle = playIdle;
        _walkLeft = walkLeft;
        _walkRight = walkRight;
        _touchHead = touchHead;
        _patrolEdges = patrolEdges;
        _previewAction = previewAction;
        _setSize = setSize;
        _packs = packs;
        _packId = packId;
        _switchPack = switchPack;
        _setClickThrough = setClickThrough;
        _setActivity = setActivity;
        _exit = exit;

        var parameters = new HwndSourceParameters("LightPet.TrayMessageWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WindowProcedure);

        _iconData = new NotifyIconData
        {
            Size = Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = _messageWindow.Handle,
            Id = 1,
            Flags = NotifyMessage | NotifyIcon | NotifyTip,
            CallbackMessage = CallbackMessage,
            IconHandle = LoadApplicationIcon(),
            Tip = "Mujun Pal",
            Info = string.Empty,
            InfoTitle = string.Empty,
        };
        if (!ShellNotifyIcon(NotifyAdd, ref _iconData))
        {
            throw new InvalidOperationException("Unable to create the LightPet tray icon.");
        }
    }

    public void SetClickThroughChecked(bool value) => _clickThrough = value;

    public void SetActivityChecked(bool value) => _activity = value;

    public void SetSizeChecked(int value) => _size = value;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = ShellNotifyIcon(NotifyDelete, ref _iconData);
        _messageWindow.RemoveHook(WindowProcedure);
        _messageWindow.Dispose();
    }

    private IntPtr WindowProcedure(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message != CallbackMessage)
        {
            return IntPtr.Zero;
        }

        var mouseMessage = unchecked((int)(longParameter.ToInt64() & 0xFFFF));
        if (mouseMessage == MouseLeftButtonDoubleClick)
        {
            _showPet();
            handled = true;
        }
        else if (mouseMessage == MouseRightButtonUp)
        {
            ShowContextMenu();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Append(menu, ShowPetCommand, "显示桌宠");
            Append(menu, QuickToolsCommand, "Memo · 随记提醒");
            AppendSeparator(menu);
            Append(menu, IdleCommand, "基础待机");
            Append(menu, WalkLeftCommand, "向左走");
            Append(menu, WalkRightCommand, "向右走");
            Append(menu, TouchHeadCommand, "摸头");
            Append(menu, EdgePatrolCommand, "沿屏幕边缘移动");
            var actionMenu = CreatePopupMenu();
            if (actionMenu != IntPtr.Zero)
            {
                for (var index = 0; index < PreviewActions.Length; index++)
                {
                    Append(actionMenu, FirstActionCommand + (uint)index, PreviewActions[index].Label);
                }

                AppendSubmenu(menu, actionMenu, "动作预览（32组）");
            }
            AppendSeparator(menu);
            var packMenu = CreatePopupMenu();
            if (packMenu != IntPtr.Zero)
            {
                for (var index = 0; index < _packs.Count; index++)
                {
                    Append(
                        packMenu,
                        FirstPackCommand + (uint)index,
                        _packs[index].Label,
                        string.Equals(_packs[index].Id, _packId, StringComparison.OrdinalIgnoreCase));
                }

                AppendSubmenu(menu, packMenu, "角色包");
            }
            var sizeMenu = CreatePopupMenu();
            if (sizeMenu != IntPtr.Zero)
            {
                Append(sizeMenu, HalfSizeCommand, "推荐（130）", _size == 130);
                Append(sizeMenu, MiniSizeCommand, "迷你（160）", _size == 160);
                Append(sizeMenu, SmallSizeCommand, "小（200）", _size == 200);
                Append(sizeMenu, StandardSizeCommand, "标准（260）", _size == 260);
                Append(sizeMenu, LargeSizeCommand, "大（320）", _size == 320);
                Append(sizeMenu, MaximumSizeCommand, "最大（400）", _size == 400);
                AppendSubmenu(menu, sizeMenu, "显示大小");
            }
            Append(menu, ClickThroughCommand, "点击穿透", _clickThrough);
            Append(menu, ActivityCommand, "自主活动", _activity);
            AppendSeparator(menu);
            Append(menu, ExitCommand, "退出");

            _ = GetCursorPosition(out var point);
            _ = SetForegroundWindow(_messageWindow.Handle);
            var command = TrackPopupMenu(
                menu,
                TrackReturnCommand | TrackRightButton,
                point.X,
                point.Y,
                0,
                _messageWindow.Handle,
                IntPtr.Zero);
            ExecuteCommand(command);
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private void ExecuteCommand(uint command)
    {
        if (command >= FirstActionCommand && command < FirstActionCommand + PreviewActions.Length)
        {
            _previewAction(PreviewActions[(int)(command - FirstActionCommand)].Id);
            return;
        }

        if (command >= FirstPackCommand && command < FirstPackCommand + _packs.Count)
        {
            var pack = _packs[(int)(command - FirstPackCommand)];
            if (!string.Equals(pack.Id, _packId, StringComparison.OrdinalIgnoreCase))
            {
                _packId = pack.Id;
                _switchPack(pack.Id);
            }

            return;
        }

        switch (command)
        {
            case ShowPetCommand:
                _showPet();
                break;
            case QuickToolsCommand:
                _openPersonalTools();
                break;
            case IdleCommand:
                _playIdle();
                break;
            case WalkLeftCommand:
                _walkLeft();
                break;
            case WalkRightCommand:
                _walkRight();
                break;
            case TouchHeadCommand:
                _touchHead();
                break;
            case EdgePatrolCommand:
                _patrolEdges();
                break;
            case HalfSizeCommand:
                _setSize(130);
                break;
            case MiniSizeCommand:
                _setSize(160);
                break;
            case SmallSizeCommand:
                _setSize(200);
                break;
            case StandardSizeCommand:
                _setSize(260);
                break;
            case LargeSizeCommand:
                _setSize(320);
                break;
            case MaximumSizeCommand:
                _setSize(400);
                break;
            case ClickThroughCommand:
                _setClickThrough(!_clickThrough);
                break;
            case ActivityCommand:
                _setActivity(!_activity);
                break;
            case ExitCommand:
                _exit();
                break;
        }
    }

    private static void Append(IntPtr menu, uint id, string text, bool isChecked = false)
    {
        var flags = MenuString | (isChecked ? MenuChecked : 0);
        _ = AppendMenu(menu, flags, new UIntPtr(id), text);
    }

    private static void AppendSeparator(IntPtr menu) =>
        _ = AppendMenu(menu, MenuSeparator, UIntPtr.Zero, null);

    private static void AppendSubmenu(IntPtr menu, IntPtr submenu, string text) =>
        _ = AppendMenu(menu, MenuPopup, new UIntPtr(unchecked((ulong)submenu.ToInt64())), text);

    private static IntPtr LoadApplicationIcon()
    {
        var module = GetModuleHandle(null);
        var width = Math.Max(16, GetSystemMetrics(SmallIconWidth));
        var height = Math.Max(16, GetSystemMetrics(SmallIconHeight));
        var icon = LoadImage(module, ApplicationIconResource, ImageIcon, width, height, LoadResourceShared);
        return icon != IntPtr.Zero ? icon : LoadIcon(IntPtr.Zero, ApplicationIconResource);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr WindowHandle;
        public int Id;
        public int Flags;
        public int CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public int State;
        public int StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public int TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public int InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(int message, ref NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", EntryPoint = "LoadImageW")]
    private static extern IntPtr LoadImage(
        IntPtr instance,
        IntPtr name,
        uint type,
        int width,
        int height,
        uint loadFlags);

    [DllImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "CreatePopupMenu")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll", EntryPoint = "DestroyMenu")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", EntryPoint = "TrackPopupMenu")]
    private static extern uint TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr window,
        IntPtr rectangle);

    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPosition(out NativePoint point);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}

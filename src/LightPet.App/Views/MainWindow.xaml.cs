using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LightPet.App.Services;
using LightPet.Core.Behavior;
using LightPet.Core.Interaction;
using LightPet.Core.Packs;
using LightPet.Core.Placement;
using WpfApplication = System.Windows.Application;
using WpfMatrix = System.Windows.Media.Matrix;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace LightPet.App.Views;

public partial class MainWindow : Window
{
    private const int WindowMessageNonClientHitTest = 0x0084;
    private static readonly IntPtr HitTestTransparent = new(-1);
    private const double DragThreshold = 7;
    private const double RubTravelThreshold = 24;
    private const double CursorAttentionDistance = 72;
    private const int WalkLoopCycles = 2;
    private const double WalkDistance = 180;
    private const int EdgeLoopCycles = 4;
    private const double EdgeTravelDistance = 280;
    private readonly LoadedPetPack _pack;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly AnimationPlayer _player;
    private TrayIconService? _tray;
    private readonly DispatcherTimer _activityTimer;
    private readonly DispatcherTimer _attentionTimer;
    private readonly DispatcherTimer _longPressTimer;
    private readonly Random _random = new();
    private readonly ContextBehaviorPlanner _behaviorPlanner = new();
    private readonly IReadOnlySet<string> _availableActionIds;
    private readonly LocalLineService _localLines = new();
    private readonly PersonalToolsLauncher _personalTools = new();
    private readonly FileShelfClient _fileShelf = new();
    private readonly bool _qaStress = string.Equals(
        Environment.GetEnvironmentVariable("LIGHTPET_QA_STRESS"),
        "1",
        StringComparison.Ordinal);
    private WpfPoint _pressPoint;
    private WpfPoint _dragScreenStart;
    private WpfPoint _dragWindowStart;
    private WpfPoint _lastGesturePoint;
    private WpfMatrix _dragTransformFromDevice = WpfMatrix.Identity;
    private string? _pressRegion;
    private double _rubTravel;
    private double _lastRubDirection;
    private int _rubDirectionChanges;
    private bool _dragging;
    private bool _rubTriggered;
    private bool _allowClose;
    private bool _exiting;
    private bool _longPressTriggered;
    private bool _transientAction;
    private bool _externalFileDrag;
    private int _actionVersion;
    private int _noticeVersion;
    private int _rapidTapCount;
    private DateTime _lastTapAtUtc = DateTime.MinValue;
    private DateTime _lastObservationAtUtc = DateTime.MinValue;
    private DateTime _lastUserInteractionAtUtc = DateTime.UtcNow;
    private DateTime _nextSpontaneousLineAtUtc;
    private ScreenEdge? _restingEdge;
    private int _perimeterDirection = 1;
    private NoticeBubbleWindow? _noticeWindow;
    private HwndSource? _windowSource;

    public MainWindow(
        LoadedPetPack pack,
        IReadOnlyList<LoadedPetPack> availablePacks,
        SettingsStore settingsStore,
        bool hostedByToolbox = false)
    {
        _pack = pack;
        _availableActionIds = pack.Actions
            .Select(action => action.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();
        _perimeterDirection = Environment.GetEnvironmentVariable("LIGHTPET_QA_EDGE_DIRECTION")
            ?.ToLowerInvariant() switch
            {
                "clockwise" => 1,
                "counterclockwise" => -1,
                _ => _random.Next(2) == 0 ? -1 : 1,
            };
        _settings.PackId = pack.Manifest.Id;
        _settings.Normalize(pack.Manifest.Display.MinimumSize, pack.Manifest.Display.MaximumSize);
        if (double.TryParse(
                Environment.GetEnvironmentVariable("LIGHTPET_QA_SIZE"),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var qaSize))
        {
            _settings.Size = Math.Clamp(
                qaSize,
                pack.Manifest.Display.MinimumSize,
                pack.Manifest.Display.MaximumSize);
        }

        InitializeComponent();
        LocationChanged += (_, _) => _noticeWindow?.UpdatePlacement(this);
        SizeChanged += (_, _) => _noticeWindow?.UpdatePlacement(this);
        ShowInTaskbar = string.Equals(
            Environment.GetEnvironmentVariable("LIGHTPET_QA_WINDOW"),
            "1",
            StringComparison.Ordinal);
        Width = _settings.Size;
        Height = _settings.Size;
        RestorePosition();

        _player = new AnimationPlayer(
            pack,
            PetImage,
            decodePixelWidth: (int)Math.Ceiling(_settings.Size * 1.15));
        _player.PlaybackFailed += exception => ShowNotice(
            $"动画播放失败：{exception.Message}",
            kind: NoticeBubbleKind.Notification);
        if (!hostedByToolbox)
        {
            _tray = new TrayIconService(
                ShowPet,
                OpenReminderTool,
                () => StartIdle(),
                () => _ = WalkAsync(-1),
                () => _ = WalkAsync(1),
                () => _ = TouchHeadAsync(),
                () => _ = PatrolEdgeAsync(),
                actionId => _ = PreviewActionAsync(actionId),
                SetSize,
                availablePacks
                    .Select(item => new TrayPackItem(item.Manifest.Id, item.Manifest.DisplayName))
                    .ToArray(),
                pack.Manifest.Id,
                SwitchPack,
                SetClickThrough,
                SetAutonomousActivity,
                ExitApplication);
            _tray.SetClickThroughChecked(_settings.ClickThrough);
            _tray.SetActivityChecked(_settings.AutonomousActivity);
            _tray.SetSizeChecked((int)Math.Round(_settings.Size));
        }

        _activityTimer = new DispatcherTimer(DispatcherPriority.Background);
        _activityTimer.Tick += OnActivityTimerTick;
        ScheduleNextSpontaneousLine(initial: true);
        ScheduleNextActivity();

        _attentionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _attentionTimer.Tick += OnAttentionTimerTick;
        _attentionTimer.Start();

        _longPressTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(650),
        };
        _longPressTimer.Tick += OnLongPressTimerTick;

        SourceInitialized += (_, _) =>
        {
            _windowSource = PresentationSource.FromVisual(this) as HwndSource;
            _windowSource?.AddHook(WindowProcedure);
            NativeWindowStyles.SetClickThrough(
                this,
                ShowInTaskbar ? false : _settings.ClickThrough);
        };
        Loaded += async (_, _) =>
        {
            _personalTools.EnsureBackgroundRunning();
            ClampToCurrentWorkArea();
            ApplyQaPlacement();
            UpdateRestingEdgeFromPosition();
            var qaNotice = Environment.GetEnvironmentVariable("LIGHTPET_QA_NOTICE");
            if (!string.IsNullOrWhiteSpace(qaNotice))
            {
                var qaNoticeKind = string.Equals(
                    Environment.GetEnvironmentVariable("LIGHTPET_QA_NOTICE_KIND"),
                    "notification",
                    StringComparison.OrdinalIgnoreCase)
                    ? NoticeBubbleKind.Notification
                    : NoticeBubbleKind.Casual;
                ShowNotice(qaNotice, durationMilliseconds: 10_000, kind: qaNoticeKind);
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() => Title = FormattableString.Invariant(
                        $"LightPet QA notice={(_noticeWindow?.IsVisible == true ? "Visible" : "Collapsed")} visible={_noticeWindow?.IsVisible == true} size={_noticeWindow?.BubbleWidth ?? 0:F0}x{_noticeWindow?.BubbleHeight ?? 0:F0} placement={_noticeWindow?.PlacementName ?? "none"}")));
            }
            if (int.TryParse(
                    Environment.GetEnvironmentVariable("LIGHTPET_QA_EXIT_MS"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var qaExitMs) && qaExitMs >= 500)
            {
                _ = ExitAfterQaDelayAsync(qaExitMs);
            }
            if (int.TryParse(
                    Environment.GetEnvironmentVariable("LIGHTPET_QA_EDGE_STEPS"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var qaEdgeSteps) && qaEdgeSteps > 0)
            {
                _activityTimer.Stop();
                StartIdle();
                await Task.Delay(250);
                for (var index = 0; index < qaEdgeSteps; index++)
                {
                    await PatrolEdgeAsync();
                    _activityTimer.Stop();
                    await Task.Delay(100);
                }

                if (string.Equals(
                        Environment.GetEnvironmentVariable("LIGHTPET_QA_EXIT_ON_EDGE_STEPS"),
                        "1",
                        StringComparison.Ordinal))
                {
                    _allowClose = true;
                    Close();
                    WpfApplication.Current.Shutdown();
                }
                else
                {
                    ScheduleNextActivity();
                }
                return;
            }
            var qaPlaylist = Environment.GetEnvironmentVariable("LIGHTPET_QA_PLAYLIST");
            if (!string.IsNullOrWhiteSpace(qaPlaylist))
            {
                _activityTimer.Stop();
                StartIdle();
                await Task.Delay(350);
                foreach (var actionId in qaPlaylist.Split(
                             ',',
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (_pack.HasAction(actionId) && !string.Equals(actionId, "idle", StringComparison.OrdinalIgnoreCase))
                    {
                        await PreviewActionAsync(actionId);
                        await Task.Delay(150);
                    }
                }

                if (string.Equals(
                        Environment.GetEnvironmentVariable("LIGHTPET_QA_EXIT_ON_PLAYLIST"),
                        "1",
                        StringComparison.Ordinal))
                {
                    _activityTimer.Stop();
                    _longPressTimer.Stop();
                    _allowClose = true;
                    Close();
                    WpfApplication.Current.Shutdown();
                }
                return;
            }

            var qaAction = Environment.GetEnvironmentVariable("LIGHTPET_QA_ACTION");
            if (string.IsNullOrWhiteSpace(qaAction))
            {
                if (_pack.HasAction("startup"))
                {
                    await PlayTransientActionAsync("startup");
                }
                else
                {
                    StartIdle();
                }
            }
            else if (_pack.HasAction(qaAction))
            {
                StartIdle();
                await Task.Delay(350);
                if (string.Equals(
                    Environment.GetEnvironmentVariable("LIGHTPET_QA_LOOP"),
                    "1",
                    StringComparison.Ordinal))
                {
                    _actionVersion++;
                    _transientAction = true;
                    _player.StartLoop(qaAction);
                }
                else
                {
                    await PreviewActionAsync(qaAction);
                }
            }
            else
            {
                StartIdle();
            }
        };
    }

    private async Task ExitAfterQaDelayAsync(int milliseconds)
    {
        await Task.Delay(milliseconds);
        if (_exiting)
        {
            return;
        }

        _activityTimer.Stop();
        _longPressTimer.Stop();
        _allowClose = true;
        Close();
        WpfApplication.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            _actionVersion++;
            _transientAction = false;
            _activityTimer.Stop();
            _attentionTimer.Stop();
            _longPressTimer.Stop();
            _player.Stop();
            _noticeWindow?.HideNow();
            Hide();
            return;
        }

        _activityTimer.Stop();
        _attentionTimer.Stop();
        _longPressTimer.Stop();
        _noticeWindow?.Close();
        _exiting = true;
        _actionVersion++;
        SaveSettings();
        _player.Dispose();
        _tray?.Dispose();
        _tray = null;
        _windowSource?.RemoveHook(WindowProcedure);
        _windowSource = null;
        base.OnClosing(e);
    }

    private IntPtr WindowProcedure(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message != WindowMessageNonClientHitTest ||
            _settings.ClickThrough)
        {
            return IntPtr.Zero;
        }

        var packedPoint = longParameter.ToInt64();
        var screenPoint = new WpfPoint(
            unchecked((short)(packedPoint & 0xffff)),
            unchecked((short)((packedPoint >> 16) & 0xffff)));
        var localPoint = PointFromScreen(screenPoint);
        if (IsOpaquePetPixel(localPoint))
        {
            return IntPtr.Zero;
        }

        handled = true;
        return HitTestTransparent;
    }

    private bool IsOpaquePetPixel(WpfPoint point)
    {
        if (point.X < 0 || point.Y < 0 ||
            point.X >= PetImage.ActualWidth || point.Y >= PetImage.ActualHeight ||
            _player.CurrentFrame is not BitmapSource frame)
        {
            return false;
        }

        var pixelX = Math.Clamp(
            (int)(point.X / Math.Max(1, PetImage.ActualWidth) * frame.PixelWidth),
            0,
            frame.PixelWidth - 1);
        var pixelY = Math.Clamp(
            (int)(point.Y / Math.Max(1, PetImage.ActualHeight) * frame.PixelHeight),
            0,
            frame.PixelHeight - 1);
        var format = frame.Format;
        var bytesPerPixel = Math.Max(1, (format.BitsPerPixel + 7) / 8);
        var sample = new byte[bytesPerPixel];
        frame.CopyPixels(new Int32Rect(pixelX, pixelY, 1, 1), sample, bytesPerPixel, 0);

        byte alpha;
        if (format == System.Windows.Media.PixelFormats.Bgra32 ||
            format == System.Windows.Media.PixelFormats.Pbgra32)
        {
            alpha = sample[3];
        }
        else if (format == System.Windows.Media.PixelFormats.Rgba64 ||
                 format == System.Windows.Media.PixelFormats.Prgba64)
        {
            alpha = sample[7];
        }
        else if (format == System.Windows.Media.PixelFormats.Indexed8 &&
                 frame.Palette is { } palette &&
                 sample[0] < palette.Colors.Count)
        {
            alpha = palette.Colors[sample[0]].A;
        }
        else
        {
            alpha = byte.MaxValue;
        }

        return alpha >= 20;
    }

    private void OnPetMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RegisterUserInteraction();
        _pressPoint = e.GetPosition(this);
        _pressRegion = GetHitRegion(_pressPoint);
        if (e.ClickCount == 2 &&
            string.Equals(_pressRegion, "body", StringComparison.OrdinalIgnoreCase))
        {
            OpenReminderTool();
            e.Handled = true;
            return;
        }

        _lastGesturePoint = _pressPoint;
        _rubTravel = 0;
        _lastRubDirection = 0;
        _rubDirectionChanges = 0;
        _rubTriggered = false;
        _dragScreenStart = PointToScreen(_pressPoint);
        _dragWindowStart = new WpfPoint(Left, Top);
        _dragTransformFromDevice = PresentationSource.FromVisual(this)?
            .CompositionTarget?
            .TransformFromDevice ?? WpfMatrix.Identity;
        _dragging = false;
        _longPressTriggered = false;
        _longPressTimer.Stop();
        _longPressTimer.Start();
        PetImage.CaptureMouse();
        e.Handled = true;
    }

    private void OnPetMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !PetImage.IsMouseCaptured)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (string.Equals(_pressRegion, "head", StringComparison.OrdinalIgnoreCase))
        {
            var movement = current - _lastGesturePoint;
            _rubTravel += movement.Length;
            if (Math.Abs(movement.X) >= 1.5)
            {
                var direction = Math.Sign(movement.X);
                if (_lastRubDirection != 0 && direction != _lastRubDirection)
                {
                    _rubDirectionChanges++;
                }

                _lastRubDirection = direction;
            }

            _lastGesturePoint = current;
            if (!_rubTriggered &&
                _rubTravel >= RubTravelThreshold &&
                _rubDirectionChanges >= 1)
            {
                StartHeadRub();
            }

            return;
        }

        if (_longPressTriggered)
        {
            return;
        }

        if (string.Equals(_pressRegion, "cheek", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var screenDeltaPixels = PointToScreen(current) - _dragScreenStart;
        var delta = _dragTransformFromDevice.Transform(screenDeltaPixels);
        if (!_dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) >= DragThreshold)
        {
            _longPressTimer.Stop();
            _dragging = true;
            _restingEdge = null;
            _actionVersion++;
            _transientAction = true;
            if (_pack.HasAction(PetInteractionContract.BodyDragAction))
            {
                _player.StartLoop(PetInteractionContract.BodyDragAction);
            }
        }

        if (_dragging)
        {
            // Mouse events report coordinates relative to a window that is itself
            // moving. Anchor the gesture in physical screen space, convert the
            // displacement once to WPF DIPs, then apply it to the drag-start
            // window position. This keeps the grab point under the pointer without
            // the local-coordinate feedback that caused oscillation and jumps.
            Left = _dragWindowStart.X + delta.X;
            Top = _dragWindowStart.Y + delta.Y;
        }
    }

    private async void OnPetMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!PetImage.IsMouseCaptured)
        {
            return;
        }

        PetImage.ReleaseMouseCapture();
        _longPressTimer.Stop();
        if (_rubTriggered)
        {
            _rubTriggered = false;
            _transientAction = false;
            StartIdle();
            return;
        }

        if (_dragging)
        {
            _dragging = false;
            ClampToCurrentWorkArea();
            UpdateRestingEdgeFromPosition();
            SaveSettings();
            await LandAfterDragAsync();
            return;
        }

        if (_longPressTriggered)
        {
            return;
        }

        if (RegisterTapBurst())
        {
            ShowNotice(_localLines.Pick("annoyed-dodge"));
            await PlayDirectInteractionAsync("annoyed-dodge");
            return;
        }

        if (string.Equals(_pressRegion, "cheek", StringComparison.OrdinalIgnoreCase))
        {
            ShowNotice(_localLines.Pick("cheek-poke"));
            await PlayDirectInteractionAsync("cheek-poke");
        }
        else if (string.Equals(_pressRegion, "head", StringComparison.OrdinalIgnoreCase))
        {
            await TouchHeadAsync();
        }
        else if (string.Equals(_pressRegion, "body", StringComparison.OrdinalIgnoreCase))
        {
            ShowNotice(_localLines.Pick("touch-body"));
            await PlayDirectInteractionAsync("touch-body");
        }
    }

    private string? GetHitRegion(WpfPoint point)
    {
        var canvasX = point.X / Math.Max(1, PetImage.ActualWidth) * _pack.Manifest.Canvas.Width;
        var canvasY = point.Y / Math.Max(1, PetImage.ActualHeight) * _pack.Manifest.Canvas.Height;
        return _pack.HitTest(canvasX, canvasY);
    }

    private bool RegisterTapBurst()
    {
        var now = DateTime.UtcNow;
        _rapidTapCount = now - _lastTapAtUtc <= TimeSpan.FromMilliseconds(950)
            ? _rapidTapCount + 1
            : 1;
        _lastTapAtUtc = now;
        if (_rapidTapCount < 4)
        {
            return false;
        }

        _rapidTapCount = 0;
        return true;
    }

    private void StartHeadRub()
    {
        if (_rubTriggered || !_pack.HasAction("head-rub"))
        {
            return;
        }

        _longPressTimer.Stop();
        _rubTriggered = true;
        _longPressTriggered = true;
        _transientAction = true;
        _actionVersion++;
        _player.StartLoop("head-rub");
    }

    private void OnActivityTimerTick(object? sender, EventArgs e)
    {
        ScheduleNextActivity();
        if ((_settings.AutonomousActivity || _qaStress) && !_transientAction && !_dragging)
        {
            var context = GetCurrentContext();
            if (_restingEdge is ScreenEdge.Left or ScreenEdge.Right or ScreenEdge.Top)
            {
                _behaviorPlanner.RecordPatrol();
                _ = PatrolEdgeAsync();
                return;
            }

            if (DateTime.UtcNow - _lastUserInteractionAtUtc < TimeSpan.FromSeconds(30))
            {
                return;
            }

            if (_behaviorPlanner.TryChooseSignatureAction(
                    context,
                    _availableActionIds,
                    _random.NextDouble(),
                    out var signatureAction))
            {
                _ = PlayTransientActionAsync(
                    signatureAction,
                    ContextBehaviorPlanner.GetLoopCycles(signatureAction));
                return;
            }

            if (DateTime.UtcNow >= _nextSpontaneousLineAtUtc &&
                _pack.HasAction("say") &&
                _random.NextDouble() < 0.35)
            {
                ShowNotice(_localLines.PickContextual(context));
                ScheduleNextSpontaneousLine(initial: false);
                _ = PlayTransientActionAsync("say");
                return;
            }

            if (HasEdgePatrolActions() &&
                _behaviorPlanner.ShouldPatrol(context, _random.NextDouble()))
            {
                _behaviorPlanner.RecordPatrol();
                _ = PatrolEdgeAsync();
                return;
            }

            var ambientAction = _behaviorPlanner.ChooseAmbientAction(
                context,
                _availableActionIds,
                _random.NextDouble());
            if (ambientAction is not null)
            {
                _ = PlayTransientActionAsync(
                    ambientAction,
                    ContextBehaviorPlanner.GetLoopCycles(ambientAction));
            }
        }
    }

    private void OnAttentionTimerTick(object? sender, EventArgs e)
    {
        if (_exiting || !IsVisible || _transientAction || _dragging ||
            _restingEdge is ScreenEdge.Left or ScreenEdge.Right or ScreenEdge.Top ||
            !_pack.HasAction("observe-cursor") ||
            DateTime.UtcNow - _lastObservationAtUtc < TimeSpan.FromSeconds(12))
        {
            return;
        }

        var pointer = Mouse.GetPosition(this);
        var horizontalDistance = Math.Max(0, Math.Max(-pointer.X, pointer.X - ActualWidth));
        var verticalDistance = Math.Max(0, Math.Max(-pointer.Y, pointer.Y - ActualHeight));
        if (Math.Sqrt(horizontalDistance * horizontalDistance + verticalDistance * verticalDistance) >
            CursorAttentionDistance)
        {
            return;
        }

        _lastObservationAtUtc = DateTime.UtcNow;
        _ = PlayTransientActionAsync("observe-cursor");
    }

    private void OnPetMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        RegisterUserInteraction();
        OpenContextMenu(PlacementMode.MousePoint);
        e.Handled = true;
    }

    private void OpenContextMenu(PlacementMode placement)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = PetImage,
            Placement = placement,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(252, 250, 255)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(45, 40, 65)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(194, 174, 235)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text"),
            FontSize = 12,
            HasDropShadow = true,
        };

        menu.Items.Add(CreatePetMenuItem("⏰  提醒我…", OpenReminderTool));
        menu.Items.Add(CreatePetMenuItem("✎  记一下…", OpenNoteTool));
        menu.Items.Add(CreatePetMenuItem("▣  Drop…", OpenFileShelf));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreatePetMenuItem("和我说句话", () => _ = SaySomethingAsync()));
        menu.Items.Add(CreatePetMenuItem("沿屏幕边缘移动", () => _ = PatrolEdgeAsync()));
        menu.Items.Add(CreatePetMenuItem("和我互动一下", () =>
        {
            var actions = new[] { "observe-cursor", "head-rub", "cheek-poke", "annoyed-dodge" }
                .Where(_pack.HasAction)
                .ToArray();
            if (actions.Length > 0)
            {
                var actionId = actions[_random.Next(actions.Length)];
                ShowNotice(_localLines.Pick(actionId));
                _ = PlayDirectInteractionAsync(actionId);
            }
        }));

        var activityItem = CreatePetMenuItem("自主活动", () =>
        {
            SetAutonomousActivity(!_settings.AutonomousActivity);
        });
        activityItem.IsCheckable = true;
        activityItem.IsChecked = _settings.AutonomousActivity;
        menu.Items.Add(activityItem);

        var sizeMenu = new MenuItem
        {
            Header = "显示大小",
            Padding = new Thickness(10, 6, 12, 6),
        };
        foreach (var size in new[] { 130, 160, 200, 260, 320, 400 })
        {
            var sizeItem = CreatePetMenuItem(
                size == 130 ? "130（推荐）" : size.ToString(CultureInfo.InvariantCulture),
                () => SetSize(size));
            sizeItem.IsCheckable = true;
            sizeItem.IsChecked = Math.Abs(Width - size) < 0.5;
            sizeMenu.Items.Add(sizeItem);
        }

        menu.Items.Add(sizeMenu);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreatePetMenuItem("暂时隐藏", HidePetFromHost));
        menu.IsOpen = true;
    }

    public void SetHostedMode()
    {
        _tray?.Dispose();
        _tray = null;
    }

    public void ShowPetFromHost() => ShowPet();

    public void HidePetFromHost() => Close();

    public void OpenSettingsFromHost()
    {
        ShowPet();
        OpenContextMenu(PlacementMode.Bottom);
    }

    public void ExitFromHost() => ExitApplication();

    private static MenuItem CreatePetMenuItem(string label, Action action)
    {
        var item = new MenuItem
        {
            Header = label,
            Padding = new Thickness(10, 6, 12, 6),
        };
        item.Click += (_, _) => action();
        return item;
    }

    private void OnLongPressTimerTick(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();
        if (_dragging || !PetImage.IsMouseCaptured || Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (string.Equals(_pressRegion, "head", StringComparison.OrdinalIgnoreCase) &&
            _pack.HasAction("head-rub"))
        {
            StartHeadRub();
            return;
        }

        _longPressTriggered = true;
        ShowNotice(_localLines.Pick("pinch"));
        _ = PlayTransientActionAsync("pinch");
    }

    private void StartIdle()
    {
        if (_exiting)
        {
            return;
        }

        _actionVersion++;
        _transientAction = false;
        _player.StartLoop(GetRestAction());
    }

    private void OpenReminderTool() => OpenPersonalTool(PersonalToolsPage.Reminder);

    private void OpenNoteTool() => OpenPersonalTool(PersonalToolsPage.Note);

    private async void OpenFileShelf()
    {
        RegisterUserInteraction();
        var result = await _fileShelf.OpenAsync();
        ShowNotice(
            result.Message,
            kind: result.Ok ? NoticeBubbleKind.Casual : NoticeBubbleKind.Notification);
        if (_pack.HasAction(result.Ok ? "happy" : "sad"))
        {
            await PlayDirectInteractionAsync(result.Ok ? "happy" : "sad");
        }
    }

    private void OnExternalFileDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        _externalFileDrag = TryGetExternalFileDrop(e.Data, out _);
        UpdateExternalFileDragFeedback(e);
        if (_externalFileDrag && !_transientAction && _pack.HasAction("surprised"))
        {
            RegisterUserInteraction();
            _ = PlayTransientActionAsync("surprised");
        }
    }

    private void OnExternalFileDragOver(object sender, System.Windows.DragEventArgs e) =>
        UpdateExternalFileDragFeedback(e);

    private void OnExternalFileDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        _externalFileDrag = false;
        e.Handled = true;
    }

    private async void OnExternalFileDrop(object sender, System.Windows.DragEventArgs e)
    {
        _externalFileDrag = false;
        if (!TryGetExternalFileDrop(e.Data, out var paths))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            ShowNotice("这里只能接住本地文件或文件夹。", kind: NoticeBubbleKind.Notification);
            return;
        }

        e.Effects = System.Windows.DragDropEffects.Copy;
        e.Handled = true;
        RegisterUserInteraction();
        var result = await _fileShelf.AddPathsAsync(paths);
        ShowNotice(
            result.Ok ? _localLines.PickShelfAdded(result.AddedCount) : result.Message,
            kind: result.Ok ? NoticeBubbleKind.Casual : NoticeBubbleKind.Notification);
        var feedbackAction = result.Ok ? "happy" : "sad";
        if (_pack.HasAction(feedbackAction))
        {
            await PlayDirectInteractionAsync(feedbackAction);
        }
    }

    private static bool TryGetExternalFileDrop(
        System.Windows.IDataObject data,
        out string[] paths)
    {
        paths = [];
        if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop) ||
            data.GetData(System.Windows.DataFormats.FileDrop) is not string[] dropped)
        {
            return false;
        }

        paths = dropped
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return paths.Length > 0;
    }

    private static void UpdateExternalFileDragFeedback(System.Windows.DragEventArgs e)
    {
        e.Effects = TryGetExternalFileDrop(e.Data, out _)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OpenPersonalTool(PersonalToolsPage page)
    {
        if (!_personalTools.TryOpen(page))
        {
            System.Windows.MessageBox.Show(
                this,
                "没有找到 Memo 模块。请重新发布或安装 MU Desk。",
                "Pal",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private async Task TouchHeadAsync()
    {
        ShowNotice(_localLines.Pick("touch-head"));
        await PlayDirectInteractionAsync("touch-head");
    }

    private async Task SaySomethingAsync()
    {
        RegisterUserInteraction();
        ShowNotice(_localLines.PickContextual(GetCurrentContext()));
        ScheduleNextSpontaneousLine(initial: false);
        await PlayDirectInteractionAsync(_pack.HasAction("say") ? "say" : "wave");
    }

    private async Task PlayDirectInteractionAsync(string actionId)
    {
        if (!_pack.HasAction(actionId) || _exiting || _dragging)
        {
            return;
        }

        _transientAction = true;
        var version = ++_actionVersion;
        var action = _pack.GetAction(actionId);
        if (action.GetPhase(AnimationPhase.Single) is not null)
        {
            await _player.PlayOnceAsync(actionId);
        }
        else
        {
            await _player.PlayStagedCyclesAsync(actionId, loopCycles: 1);
        }

        if (!_exiting && version == _actionVersion)
        {
            _transientAction = false;
            StartIdle();
        }
    }

    private async Task PlayTransientActionAsync(string actionId, int loopCycles = 1)
    {
        if (_transientAction || !_pack.HasAction(actionId))
        {
            return;
        }

        _transientAction = true;
        var version = ++_actionVersion;
        var action = _pack.GetAction(actionId);
        if (action.GetPhase(AnimationPhase.Single) is not null)
        {
            await _player.PlayOnceAsync(actionId);
        }
        else
        {
            await _player.PlayStagedCyclesAsync(actionId, loopCycles);
        }

        if (version == _actionVersion)
        {
            _transientAction = false;
            StartIdle();
        }
    }

    private async Task PreviewActionAsync(string actionId)
    {
        if (string.Equals(actionId, "idle", StringComparison.OrdinalIgnoreCase))
        {
            StartIdle();
            return;
        }

        if (string.Equals(actionId, "walk-left", StringComparison.OrdinalIgnoreCase))
        {
            await WalkAsync(-1);
            return;
        }

        if (string.Equals(actionId, "walk-right", StringComparison.OrdinalIgnoreCase))
        {
            await WalkAsync(1);
            return;
        }

        if (actionId is "say" or "wave" or "happy" or "sad" or "surprised" or
            "think" or "stretch" or "hair-fix" or "yawn" or "sit-rest" or
            "sleep" or "observe-cursor" or "head-rub")
        {
            ShowNotice(string.Equals(actionId, "say", StringComparison.OrdinalIgnoreCase)
                ? _localLines.PickContextual(GetCurrentContext())
                : _localLines.Pick(actionId));
        }

        await PlayTransientActionAsync(
            actionId,
            string.Equals(actionId, "sleep", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
    }

    private async Task LandAfterDragAsync()
    {
        if (_exiting)
        {
            return;
        }

        if (_restingEdge is ScreenEdge.Left or ScreenEdge.Right or ScreenEdge.Top)
        {
            StartIdle();
            return;
        }

        if (!_pack.HasAction(PetInteractionContract.DragReleaseAction))
        {
            StartIdle();
            return;
        }

        _transientAction = true;
        var version = ++_actionVersion;
        await _player.PlayOnceAsync(PetInteractionContract.DragReleaseAction);
        if (!_exiting && version == _actionVersion)
        {
            _transientAction = false;
            StartIdle();
        }
    }

    private async Task WalkAsync(int direction)
    {
        if (_transientAction || _dragging)
        {
            return;
        }

        var workArea = MonitorWorkArea.GetFor(this);
        var placementArea = GetPetPlacementArea(workArea);
        if (_restingEdge != ScreenEdge.Bottom ||
            Math.Abs(Top - (placementArea.Bottom - Height)) > 2)
        {
            await DescendToBottomEdgeAsync(workArea);
            return;
        }

        _transientAction = true;
        var version = ++_actionVersion;
        direction = WindowPlacement.ChooseWalkDirection(
            direction,
            Left,
            Width,
            placementArea.Left,
            placementArea.Right,
            desiredDistance: WalkDistance);
        var travelDistance = WindowPlacement.AvailableWalkDistance(
            direction,
            Left,
            Width,
            placementArea.Left,
            placementArea.Right,
            WalkDistance);
        var action = direction < 0 ? "walk-left" : "walk-right";
        var duration = _player.GetStagedDuration(action, loopCycles: WalkLoopCycles);
        var playback = _player.PlayStagedCyclesAsync(action, loopCycles: WalkLoopCycles);
        var movement = MoveWindowAsync(direction, travelDistance, duration, version, placementArea);
        await Task.WhenAll(playback, movement);
        if (version == _actionVersion)
        {
            _transientAction = false;
            _restingEdge = ScreenEdge.Bottom;
            StartIdle();
            SaveSettings();
        }
    }

    private async Task PatrolEdgeAsync()
    {
        if (_transientAction || _dragging || !HasEdgePatrolActions())
        {
            return;
        }

        var workArea = MonitorWorkArea.GetFor(this);
        var placementArea = GetPetPlacementArea(workArea);
        _restingEdge ??= EdgePatrol.Detect(
            Left,
            Top,
            Width,
            Height,
            placementArea.Left,
            placementArea.Top,
            placementArea.Right,
            placementArea.Bottom);
        if (_restingEdge is null)
        {
            await DescendToBottomEdgeAsync(workArea);
            return;
        }

        var edge = _restingEdge.Value;
        var axisDirection = EdgePatrol.AxisDirection(edge, _perimeterDirection);
        var available = EdgePatrol.AvailableDistance(
            edge,
            axisDirection,
            Left,
            Top,
            Width,
            Height,
            placementArea.Left,
            placementArea.Top,
            placementArea.Right,
            placementArea.Bottom);
        if (available <= 0.5)
        {
            edge = EdgePatrol.NextEdge(edge, _perimeterDirection);
            _restingEdge = edge;
            axisDirection = EdgePatrol.AxisDirection(edge, _perimeterDirection);
            available = EdgePatrol.AvailableDistance(
                edge,
                axisDirection,
                Left,
                Top,
                Width,
                Height,
                placementArea.Left,
                placementArea.Top,
                placementArea.Right,
                placementArea.Bottom);
        }

        var travelDistance = Math.Min(EdgeTravelDistance, available);
        if (travelDistance <= 0.5)
        {
            StartIdle();
            return;
        }

        _transientAction = true;
        var version = ++_actionVersion;
        var actionId = GetEdgeMovementAction(edge, axisDirection);
        var duration = _player.GetStagedDuration(actionId, EdgeLoopCycles);
        var targetLeft = Left;
        var targetTop = Top;
        if (edge is ScreenEdge.Top or ScreenEdge.Bottom)
        {
            targetLeft += axisDirection * travelDistance;
        }
        else
        {
            targetTop += axisDirection * travelDistance;
        }

        var playback = _player.PlayStagedCyclesAsync(actionId, EdgeLoopCycles);
        var movement = MoveWindowToAsync(
            targetLeft,
            targetTop,
            duration,
            version,
            placementArea);
        await Task.WhenAll(playback, movement);
        if (version != _actionVersion)
        {
            return;
        }

        if (available - travelDistance <= 0.5)
        {
            _restingEdge = EdgePatrol.NextEdge(edge, _perimeterDirection);
        }
        else
        {
            _restingEdge = edge;
        }

        _transientAction = false;
        StartIdle();
        SaveSettings();
        ScheduleNextActivity();
    }

    private async Task DescendToBottomEdgeAsync(Rect workArea)
    {
        _transientAction = true;
        var version = ++_actionVersion;
        var placementArea = GetPetPlacementArea(workArea);
        var targetTop = placementArea.Bottom - Height;
        var actionId = _pack.HasAction("fall-land") ? "fall-land" : "idle";
        var fallDistance = Math.Abs(targetTop - Top);
        var loopCycles = fallDistance switch
        {
            < 180 => 1,
            < 420 => 2,
            _ => 3,
        };
        var travelDuration = _player.GetStagedTravelDuration(actionId, loopCycles);
        var playback = _player.PlayStagedCyclesAsync(actionId, loopCycles);
        var movement = MoveWindowToAsync(
            Left,
            targetTop,
            travelDuration,
            version,
            placementArea,
            accelerate: true);
        await Task.WhenAll(playback, movement);
        if (version == _actionVersion)
        {
            _restingEdge = ScreenEdge.Bottom;
            _transientAction = false;
            StartIdle();
            SaveSettings();
        }
    }

    private bool HasEdgePatrolActions() =>
        _pack.HasAction("edge-climb-left") &&
        _pack.HasAction("edge-climb-right") &&
        _pack.HasAction("edge-top-left") &&
        _pack.HasAction("edge-top-right") &&
        _pack.HasAction("edge-hold-left") &&
        _pack.HasAction("edge-hold-right") &&
        _pack.HasAction("edge-hold-top-left") &&
        _pack.HasAction("edge-hold-top-right");

    private static string GetEdgeMovementAction(ScreenEdge edge, int axisDirection) =>
        edge switch
        {
            ScreenEdge.Bottom => axisDirection < 0 ? "walk-left" : "walk-right",
            ScreenEdge.Top => axisDirection < 0 ? "edge-top-left" : "edge-top-right",
            ScreenEdge.Left => "edge-climb-left",
            ScreenEdge.Right => "edge-climb-right",
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };

    private string GetRestAction() =>
        _restingEdge switch
        {
            ScreenEdge.Left when _pack.HasAction("edge-hold-left") => "edge-hold-left",
            ScreenEdge.Right when _pack.HasAction("edge-hold-right") => "edge-hold-right",
            ScreenEdge.Top when _perimeterDirection < 0 && _pack.HasAction("edge-hold-top-left") =>
                "edge-hold-top-left",
            ScreenEdge.Top when _pack.HasAction("edge-hold-top-right") => "edge-hold-top-right",
            _ => "idle",
        };

    private async Task MoveWindowAsync(
        int direction,
        double distance,
        TimeSpan duration,
        int actionVersion,
        Rect workArea)
    {
        await MoveWindowToAsync(
            Left + direction * distance,
            Top,
            duration,
            actionVersion,
            workArea);
    }

    private async Task MoveWindowToAsync(
        double targetLeft,
        double targetTop,
        TimeSpan duration,
        int actionVersion,
        Rect workArea,
        bool accelerate = false)
    {
        var startLeft = Left;
        var startTop = Top;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            var remainingMilliseconds = duration.TotalMilliseconds - stopwatch.Elapsed.TotalMilliseconds;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(16, Math.Max(1, remainingMilliseconds))));
            if (actionVersion != _actionVersion || _dragging)
            {
                break;
            }

            var progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            var eased = accelerate
                ? progress * progress
                : progress * progress * (3 - 2 * progress);
            var nextLeft = startLeft + (targetLeft - startLeft) * eased;
            var nextTop = startTop + (targetTop - startTop) * eased;
            var position = WindowPlacement.Clamp(
                nextLeft,
                nextTop,
                Width,
                Height,
                workArea.Left,
                workArea.Top,
                workArea.Right,
                workArea.Bottom);
            Left = position.Left;
            Top = position.Top;
        }
    }

    private void SetClickThrough(bool enabled)
    {
        _settings.ClickThrough = enabled;
        NativeWindowStyles.SetClickThrough(this, enabled);
        _tray?.SetClickThroughChecked(enabled);
        SaveSettings();
    }

    private void SetAutonomousActivity(bool enabled)
    {
        _settings.AutonomousActivity = enabled;
        _tray?.SetActivityChecked(enabled);
        SaveSettings();
    }

    private void SetSize(int requestedSize)
    {
        var size = Math.Clamp(
            (double)requestedSize,
            _pack.Manifest.Display.MinimumSize,
            _pack.Manifest.Display.MaximumSize);
        var centerX = Left + Width / 2;
        var centerY = Top + Height / 2;
        var bottom = Top + Height;
        var workArea = MonitorWorkArea.GetFor(this);
        Width = size;
        Height = size;
        var placementArea = GetPetPlacementArea(workArea);
        (Left, Top) = _restingEdge switch
        {
            ScreenEdge.Top => (centerX - size / 2, placementArea.Top),
            ScreenEdge.Left => (placementArea.Left, centerY - size / 2),
            ScreenEdge.Right => (placementArea.Right - size, centerY - size / 2),
            ScreenEdge.Bottom => (centerX - size / 2, placementArea.Bottom - size),
            _ => (centerX - size / 2, bottom - size),
        };
        ClampToCurrentWorkArea();
        _settings.Size = size;
        _player.SetDecodePixelWidth((int)Math.Ceiling(size * 1.15));
        _tray?.SetSizeChecked((int)Math.Round(size));
        StartIdle();
        SaveSettings();
    }

    private void SwitchPack(string packId)
    {
        _settings.PackId = packId;
        SaveSettings();
        _allowClose = true;
        ((App)WpfApplication.Current).Restart();
    }

    private void ShowPet()
    {
        Show();
        Topmost = true;
        ClampToCurrentWorkArea();
        StartIdle();
        _attentionTimer.Start();
        ScheduleNextActivity();
    }

    private async void ShowNotice(
        string text,
        int durationMilliseconds = 2600,
        NoticeBubbleKind kind = NoticeBubbleKind.Casual)
    {
        if (durationMilliseconds == 2600)
        {
            durationMilliseconds = Math.Clamp(1800 + text.Length * 85, 2600, 4800);
        }

        var version = ++_noticeVersion;
        _noticeWindow ??= new NoticeBubbleWindow();
        _noticeWindow.ShowMessage(this, text, kind);
        await Task.Delay(durationMilliseconds);
        if (version == _noticeVersion)
        {
            _noticeWindow.BeginHideAnimation();
            await Task.Delay(150);
            if (version == _noticeVersion)
            {
                _noticeWindow.HideNow();
            }
        }
    }

    private void ScheduleNextActivity()
    {
        var delay = ContextBehaviorPlanner.GetActivityDelay(
            GetCurrentContext(),
            _restingEdge is ScreenEdge.Left or ScreenEdge.Right or ScreenEdge.Top,
            _qaStress);
        _activityTimer.Interval = TimeSpan.FromSeconds(
            _random.Next(delay.MinimumSeconds, delay.MaximumSecondsExclusive));
        if (!_activityTimer.IsEnabled)
        {
            _activityTimer.Start();
        }
    }

    private void RegisterUserInteraction()
    {
        var now = DateTime.UtcNow;
        _lastUserInteractionAtUtc = now;
        if (_player.CurrentAction is { } currentAction &&
            _availableActionIds.Contains(currentAction) &&
            currentAction is "stretch" or "hair-fix" or "yawn" or "sit-rest" or "sleep" or "think")
        {
            StartIdle();
        }
    }

    private PetContextSnapshot GetCurrentContext()
    {
        var localTime = PetContextResolver.ResolveQaTime(
            DateTimeOffset.Now,
            Environment.GetEnvironmentVariable("LIGHTPET_QA_TIME"));
        return PetContextResolver.Resolve(
            localTime,
            Environment.GetEnvironmentVariable("LIGHTPET_QA_DAYTYPE"));
    }

    private void ScheduleNextSpontaneousLine(bool initial)
    {
        var context = GetCurrentContext();
        var (minimumMinutes, maximumMinutesExclusive) = context.TimeBlock == PetTimeBlock.LateNight
            ? (initial ? 7 : 14, initial ? 13 : 29)
            : (initial ? 3 : 8, initial ? 7 : 19);
        _nextSpontaneousLineAtUtc = DateTime.UtcNow.AddMinutes(
            _random.Next(minimumMinutes, maximumMinutesExclusive));
    }

    private void RestorePosition()
    {
        var defaultLeft = SystemParameters.WorkArea.Right - Width - 32;
        var defaultTop = SystemParameters.WorkArea.Bottom - Height - 24;
        Left = _settings.Left ?? defaultLeft;
        Top = _settings.Top ?? defaultTop;

        var visible = Left + Width >= SystemParameters.VirtualScreenLeft + 40 &&
            Left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 &&
            Top + Height >= SystemParameters.VirtualScreenTop + 40 &&
            Top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
        if (!visible)
        {
            Left = defaultLeft;
            Top = defaultTop;
        }
    }

    private void ClampToCurrentWorkArea()
    {
        var workArea = MonitorWorkArea.GetFor(this);
        var placementArea = GetPetPlacementArea(workArea);
        var position = WindowPlacement.Clamp(
            Left,
            Top,
            Width,
            Height,
            placementArea.Left,
            placementArea.Top,
            placementArea.Right,
            placementArea.Bottom);
        Left = position.Left;
        Top = position.Top;
    }

    private void UpdateRestingEdgeFromPosition()
    {
        var workArea = MonitorWorkArea.GetFor(this);
        var placementArea = GetPetPlacementArea(workArea);
        _restingEdge = EdgePatrol.Detect(
            Left,
            Top,
            Width,
            Height,
            placementArea.Left,
            placementArea.Top,
            placementArea.Right,
            placementArea.Bottom);
    }

    private void ApplyQaPlacement()
    {
        var placement = Environment.GetEnvironmentVariable("LIGHTPET_QA_PLACEMENT");
        if (string.IsNullOrWhiteSpace(placement))
        {
            return;
        }

        var workArea = MonitorWorkArea.GetFor(this);
        var placementArea = GetPetPlacementArea(workArea);
        (Left, Top) = placement.ToLowerInvariant() switch
        {
            "top-left" => (placementArea.Left, placementArea.Top),
            "top-right" => (placementArea.Right - Width, placementArea.Top),
            "bottom-left" => (placementArea.Left, placementArea.Bottom - Height),
            "bottom-right" => (placementArea.Right - Width, placementArea.Bottom - Height),
            "near-left" => (placementArea.Left + 80, placementArea.Bottom - Height),
            "near-right" => (placementArea.Right - Width - 80, placementArea.Bottom - Height),
            "center" => ((placementArea.Left + placementArea.Right - Width) / 2, placementArea.Bottom - Height),
            _ => (Left, Top),
        };
        ClampToCurrentWorkArea();
    }

    private Rect GetPetPlacementArea(Rect workArea)
    {
        var visible = _pack.Manifest.Display.VisibleBounds;
        var bounds = WindowPlacement.ExpandForVisibleContent(
            workArea.Left,
            workArea.Top,
            workArea.Right,
            workArea.Bottom,
            Width,
            Height,
            visible.Left,
            visible.Top,
            visible.Right,
            visible.Bottom);
        return new Rect(
            new WpfPoint(bounds.Left, bounds.Top),
            new WpfPoint(bounds.Right, bounds.Bottom));
    }

    private void SaveSettings()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Size = Width;
        _settingsStore.Save(_settings);
    }

    private async void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _activityTimer.Stop();
        _longPressTimer.Stop();
        if (IsVisible && _pack.HasAction("shutdown"))
        {
            _actionVersion++;
            _transientAction = true;
            await _player.PlayOnceAsync("shutdown");
        }

        _allowClose = true;
        Close();
        WpfApplication.Current.Shutdown();
    }
}

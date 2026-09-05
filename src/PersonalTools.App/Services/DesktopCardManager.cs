using System.Windows;
using PersonalTools.App.Views;
using PersonalTools.Core;

namespace PersonalTools.App.Services;

public sealed class DesktopCardManager : IDisposable
{
    private const double ArrangeGap = 14;
    private readonly IEntryProvider _provider;
    private readonly Dictionary<string, DesktopCardWindow> _windows = new(StringComparer.Ordinal);
    private bool _cardsHidden;
    private bool _disposed;

    public DesktopCardManager(IEntryProvider provider)
    {
        _provider = provider;
    }

    public bool AreCardsHidden => _cardsHidden;

    public int VisibleCardCount => _windows.Values.Count(window => window.IsVisible);

    public event EventHandler? Changed;

    public event EventHandler<string>? OpenRequested;

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        var pinned = await _provider.QueryAsync(
            new EntryQuery(IncludeCompleted: true, IncludeArchived: false, PinnedToDesktop: true),
            cancellationToken);
        var wanted = pinned.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _windows.Keys.Where(id => !wanted.Contains(id)).ToArray())
        {
            _windows[stale].CloseWithoutUnpin();
            _windows.Remove(stale);
        }

        foreach (var entry in pinned)
        {
            if (_windows.TryGetValue(entry.Id, out var existing))
            {
                existing.UpdateEntry(entry);
                continue;
            }

            var window = new DesktopCardWindow(_provider, ClampToVisibleArea(entry), GetSnapTargets);
            window.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            window.OpenRequested += (_, id) => OpenRequested?.Invoke(this, id);
            window.Closed += (_, _) => _windows.Remove(entry.Id);
            _windows.Add(entry.Id, window);
            window.Show();
            if (_cardsHidden)
            {
                window.Hide();
            }
        }
    }

    public void ToggleVisibility()
    {
        _cardsHidden = !_cardsHidden;
        foreach (var window in _windows.Values)
        {
            if (_cardsHidden)
            {
                window.Hide();
            }
            else
            {
                window.Show();
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void BringAllToFront()
    {
        _cardsHidden = false;
        foreach (var window in _windows.Values)
        {
            window.Show();
            var wasTopmost = window.Topmost;
            if (!wasTopmost)
            {
                window.Topmost = true;
                window.Topmost = false;
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ArrangeAsync(CancellationToken cancellationToken = default)
    {
        var pinned = await _provider.QueryAsync(
            new EntryQuery(IncludeCompleted: true, IncludeArchived: false, PinnedToDesktop: true),
            cancellationToken);
        if (pinned.Count == 0)
        {
            return;
        }

        var area = SystemParameters.WorkArea;
        var width = Math.Clamp(pinned.Select(item => item.DesktopCard.Width).DefaultIfEmpty(286).Average(), 250, 330);
        var height = Math.Clamp(pinned.Select(item => item.DesktopCard.Height).DefaultIfEmpty(205).Average(), 170, 250);
        var columns = Math.Max(1, (int)Math.Floor((area.Width - 32 + ArrangeGap) / (width + ArrangeGap)));
        var monitorId = System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
        for (var index = 0; index < pinned.Count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var entry = pinned[index];
            var card = entry.DesktopCard with
            {
                Left = area.Left + 16 + column * (width + ArrangeGap),
                Top = area.Top + 16 + row * (height + ArrangeGap),
                Width = width,
                Height = height,
                IsCollapsed = false,
                MonitorId = monitorId,
            };
            await _provider.SetDesktopCardAsync(entry.Id, card, cancellationToken);
        }
        _cardsHidden = false;
        await SynchronizeAsync(cancellationToken);
        BringAllToFront();
    }

    public async Task RescueOffscreenAsync(CancellationToken cancellationToken = default)
    {
        var pinned = await _provider.QueryAsync(
            new EntryQuery(IncludeCompleted: true, IncludeArchived: false, PinnedToDesktop: true),
            cancellationToken);
        foreach (var entry in pinned)
        {
            var rescued = ClampToVisibleArea(entry);
            if (rescued.DesktopCard != entry.DesktopCard)
            {
                await _provider.SetDesktopCardAsync(entry.Id, rescued.DesktopCard, cancellationToken);
            }
        }
        _cardsHidden = false;
        await SynchronizeAsync(cancellationToken);
        BringAllToFront();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (var window in _windows.Values.ToArray())
        {
            window.CloseWithoutUnpin();
        }
        _windows.Clear();
    }

    private IReadOnlyList<Rect> GetSnapTargets(string exceptId) => _windows
        .Where(pair => pair.Key != exceptId && pair.Value.IsVisible)
        .Select(pair => pair.Value.Bounds)
        .ToArray();

    private static EntryItem ClampToVisibleArea(EntryItem entry)
    {
        var card = entry.DesktopCard;
        var virtualArea = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var cardArea = new Rect(card.Left, card.Top, card.Width, card.IsCollapsed ? 58 : card.Height);
        var visible = Rect.Intersect(virtualArea, cardArea);
        if (visible.Width >= 48 && visible.Height >= 32)
        {
            return entry;
        }

        var area = SystemParameters.WorkArea;
        var left = Math.Clamp(card.Left, area.Left + 8, Math.Max(area.Left + 8, area.Right - card.Width - 8));
        var top = Math.Clamp(card.Top, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - card.Height - 8));
        if (double.IsNaN(left) || double.IsInfinity(left))
        {
            left = area.Left + 24;
        }
        if (double.IsNaN(top) || double.IsInfinity(top))
        {
            top = area.Top + 24;
        }
        return entry with
        {
            DesktopCard = card with
            {
                Left = left,
                Top = top,
                MonitorId = System.Windows.Forms.Screen.PrimaryScreen?.DeviceName,
            },
        };
    }
}

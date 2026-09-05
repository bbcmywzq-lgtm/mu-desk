using System.IO;
using System.Windows.Media.Imaging;

namespace LightPet.App.Services;

internal sealed class ImageFrameCache
{
    private sealed record CacheEntry(BitmapSource Source, LinkedListNode<string> Node);

    private readonly int _capacity;
    private readonly int _decodePixelWidth;
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recent = [];

    public ImageFrameCache(int capacity, int decodePixelWidth)
    {
        _capacity = Math.Max(4, capacity);
        _decodePixelWidth = Math.Max(64, decodePixelWidth);
    }

    public BitmapSource Get(string path)
    {
        if (_entries.TryGetValue(path, out var cached))
        {
            _recent.Remove(cached.Node);
            _recent.AddFirst(cached.Node);
            return cached.Source;
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Animation frame is missing.", path);
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.DecodePixelWidth = _decodePixelWidth;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        var node = _recent.AddFirst(path);
        _entries.Add(path, new CacheEntry(bitmap, node));
        Trim();
        return bitmap;
    }

    public void Clear()
    {
        _entries.Clear();
        _recent.Clear();
    }

    private void Trim()
    {
        while (_entries.Count > _capacity && _recent.Last is { } last)
        {
            _entries.Remove(last.Value);
            _recent.RemoveLast();
        }
    }
}

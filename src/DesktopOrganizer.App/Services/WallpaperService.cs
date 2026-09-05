using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace DesktopOrganizer.Services;

public sealed class WallpaperService
{
    public ImageSource? LoadCurrentWallpaper()
    {
        foreach (var path in GetWallpaperCandidates())
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return frame;
            }
            catch (IOException)
            {
                // Wallpaper slideshow transitions can briefly lock the source file.
            }
            catch (NotSupportedException)
            {
                // Continue to the transcoded wallpaper fallback.
            }
        }

        return null;
    }

    private static IEnumerable<string?> GetWallpaperCandidates()
    {
        using var desktopKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        yield return desktopKey?.GetValue("WallPaper") as string;

        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(applicationData, "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
    }
}

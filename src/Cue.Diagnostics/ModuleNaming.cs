using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PersonalToolbox.Modules;
using Toolbox.Core;

internal static partial class Program
{
    // Read-only shell check: no module starts, worker launches, hooks or settings saves.
    private static async Task VerifyModuleNames()
    {
        var resources = System.Windows.Application.Current.Resources;
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DesktopOrganizer;component/Themes/DesktopOrganizerResources.xaml", UriKind.Relative)
        });
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/PersonalToolbox;component/Styles/MuProductTheme.xaml", UriKind.Relative)
        });
        resources["InkBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 42));
        resources["MutedBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(115, 115, 125));
        resources["AccentBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 85, 217));
        var settings = new ToolboxSettings();
        var ring = new MouseRing.MouseRingModule();
        var cue = new CueModule(settings.Cue, () => { });
        using var capture = new EffectCaptureModule(settings.EffectCapture, () => { },
            defaultOutputRoot: Path.Combine(Path.GetTempPath(), "MU-Names-QA", Guid.NewGuid().ToString("N")));
        var tip = new CursorGalleryModule();
        var cisp = new CispQuestionBankModule();
        var grid = new DesktopOrganizer.DesktopOrganizerModule();
        var drop = new FileShelfModule(settings.FileShelf, () => { });
        var shell = new PersonalToolbox.MainWindow(ring, cue, capture, tip, cisp, grid, drop,
            new LightPetModule(), new ReminderNotesModule(), settings) { ShowActivated = false };
        var expected = new[] { "Orbit · 快捷轮盘", "Cue · 屏幕讲解", "Clip · 动态拾取", "Tip · 光标装扮",
            "Grid · 桌面整理", "Drop · 文件暂存", "Memo · 随记提醒", "Pal · 桌面伙伴", "CISP 题库" };
        try
        {
            shell.Show();
            foreach (var width in new[] { 1020d, 900d })
            {
                shell.Width = width;
                shell.UpdateLayout();
                await Task.Delay(100);
                var texts = Descendants(shell).OfType<TextBlock>().ToArray();
                foreach (var name in expected)
                {
                    var title = texts.Single(t => t.Text == name);
                    var panel = (FrameworkElement)title.Parent;
                    // Horizontal title + status must fit the module's allocated column.
                    if (panel.DesiredSize.Width > panel.RenderSize.Width + 1)
                        throw new Exception($"Title overflow at {width}: {name}, desired={panel.DesiredSize.Width}, actual={panel.RenderSize.Width}");
                }
                var scroll = Descendants(shell).OfType<ScrollViewer>().First();
                scroll.ScrollToTop(); shell.UpdateLayout();
                SaveShell($"artifacts/module-names-{width}-top.png");
                scroll.ScrollToBottom(); shell.UpdateLayout();
                SaveShell($"artifacts/module-names-{width}-bottom.png");
            }
            if (ring.Id != "mouse-ring" || cue.Id != "mujun-cue" || tip.Id != "cursor-gallery"
                || grid.Id != "desktop-organizer" || drop.Id != "file-shelf" || cisp.DisplayName != "CISP 题库")
                throw new Exception("A stable module identity changed.");
            Console.WriteLine("PASS: all 9 module labels; 900/1020 DIP title layouts; stable module IDs; CISP identity unchanged.");
        }
        finally { shell.PrepareForShutdown(); }

        void SaveShell(string path)
        {
            var content = (FrameworkElement)shell.Content;
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight,
                96, 96, PixelFormats.Pbgra32);
            var surface = new DrawingVisual();
            using (var drawing = surface.RenderOpen())
            {
                var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
                drawing.DrawRectangle(shell.Background, null, bounds);
                drawing.DrawRectangle(new VisualBrush(content), null, bounds);
            }
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.GetFullPath(path));
            encoder.Save(stream);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}

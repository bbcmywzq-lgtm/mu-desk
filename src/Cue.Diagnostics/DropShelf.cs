using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PersonalToolbox.Modules;
using PersonalToolbox.Views;
using Toolbox.Core;

internal static partial class Program
{
    private static async Task VerifyDropShelf()
    {
        var root = Path.Combine(Path.GetTempPath(), "MU-Drop-QA", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fixture = Path.Combine(root, "项目说明.txt");
        var second = Path.Combine(root, "设计参考.txt");
        File.WriteAllText(fixture, "Drop test fixture: never modify this source.");
        File.WriteAllText(second, "Another local path.");
        var settings = new FileShelfSettings();
        using var module = new FileShelfModule(settings, () => { }, new FileShelfStore(Path.Combine(root, "shelf.json")));
        var shelf = new FileShelfWindow(module, settings) { AnimationEnabledForDiagnostics = true };
        var hwnd = nint.Zero;
        try
        {
            shelf.ShowCollapsed();
            await Task.Delay(80);
            hwnd = new WindowInteropHelper(shelf).Handle;
            Check(shelf.Width == 22 && shelf.Height == 76, "compact persistent handle");
            Save("handle");
            var start = shelf.HostPlacementCount;
            shelf.ShowExpanded(false);
            await Task.Delay(35);
            GetWindowRect(hwnd, out var opening);
            if (WindowFromPoint(new ProbePoint(opening.Left + 4, opening.Top + 16)) == hwnd)
                throw new Exception("Transparent animation area blocks desktop input.");
            await Task.Delay(80);
            GetWindowRect(hwnd, out var middle);
            Check(opening.Equals(middle), "fixed native window during reveal");
            var before = shelf.VisibleReveal;
            shelf.ShowCollapsed();
            Check(Math.Abs(before.Width - shelf.VisibleReveal.Width) < 2, "reversal retains position");
            await Task.Delay(55);
            shelf.ShowExpanded(false);
            await Task.Delay(550);
            Check(shelf.IsExpanded && !shelf.IsTransitioning, "reversal settles expanded");
            Check(((Border)shelf.FindName("ExpandedPanel")).CacheMode is null, "temporary cache released");
            Save("empty");
            var valid = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { fixture, second });
            var drop = Drag(valid, System.Windows.DragDrop.DropEvent);
            Check(drop.Effects == System.Windows.DragDropEffects.Copy && module.ItemCount == 2, "real drop handler saves isolated paths");
            var item = module.Snapshot().Batches[0].Items[0];
            Check(module.TrySetPinned(item.Id, true, out _), "pin");
            await Task.Delay(150); Save("files");
            // Exercise the actual remove button handler, not just the model.
            var button = Descendants((DependencyObject)shelf.Content).OfType<System.Windows.Controls.Button>()
                .Single(b => b.Tag as string == item.Id);
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(module.ItemCount == 1 && module.CanUndo, "remove button");
            ((System.Windows.Controls.Button)shelf.FindName("UndoButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(module.ItemCount == 2 && module.PinnedCount == 1, "undo button preserves pin");
            Check(module.TryClearUnpinned(out _) && module.ItemCount == 1, "clear excludes pinned paths");
            module.TryUndo(out _);
            typeof(FileShelfWindow).GetField("_outgoingDrag", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shelf, true);
            var self = Drag(valid, System.Windows.DragDrop.DropEvent);
            Check(self.Effects == System.Windows.DragDropEffects.None && module.ItemCount == 2, "self-drop cancels without duplicating/removing");
            typeof(FileShelfWindow).GetField("_outgoingDrag", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shelf, false);
            shelf.ShowCollapsed(); await Task.Delay(550);
            var invalid = Drag(new System.Windows.DataObject(System.Windows.DataFormats.Text, "not a local file"), System.Windows.DragDrop.DragEnterEvent);
            Check(!shelf.IsExpanded && invalid.Effects == System.Windows.DragDropEffects.None, "invalid drag does not expand");
            Drag(valid, System.Windows.DragDrop.DragEnterEvent);
            Check(shelf.IsExpanded, "valid drag opens without activation");
            Drag(valid, System.Windows.DragDrop.DragLeaveEvent);
            shelf.Hide();
            Check(!shelf.IsTransitioning, "hide finalizes animation");
            shelf.ShowCollapsed();
            shelf.AnimationEnabledForDiagnostics = false;
            shelf.ShowExpanded(false);
            Check(!shelf.IsTransitioning && shelf.Width == 360, "reduced motion is immediate");
            shelf.ShowCollapsed();
            Check(shelf.Width == 22, "collapsed native bounds restored");
            shelf.ShowExpanded(false);
            // The live content must not reflow during the compositor reveal.
            var panel = (Border)shelf.FindName("ExpandedPanel");
            var body = panel.Child; panel.Child = null;
            var layout = new LayoutCounter { Child = body }; panel.Child = layout;
            shelf.UpdateLayout();
            var measures = layout.Measures; var arranges = layout.Arranges;
            shelf.AnimationEnabledForDiagnostics = true;
            var placements = shelf.HostPlacementCount;
            for (var i = 0; i < 8; i++)
            {
                if (i % 2 == 0) shelf.ShowCollapsed(); else shelf.ShowExpanded(false);
                await Task.Delay(65);
            }
            await Task.Delay(550);
            Check(layout.Measures == measures && layout.Arranges == arranges, "no per-frame content layout on rapid reversals");
            Check(shelf.HostPlacementCount - placements <= 10, "native placement is bounded to transitions");
            Console.WriteLine($"Drop rapid-reversal check: 8 retargets, {layout.Measures - measures} content measures, {layout.Arranges - arranges} content arranges, {shelf.HostPlacementCount - placements} native placements.");
            shelf.AnimationEnabledForDiagnostics = false;
            // Trigger the normal pointer-leave grace timer with a short test interval.
            if (!shelf.IsMouseOver)
            {
                shelf.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                    { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
                var timer = (System.Windows.Threading.DispatcherTimer)typeof(FileShelfWindow)
                    .GetField("_collapseTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shelf)!;
                timer.Interval = TimeSpan.FromMilliseconds(20);
                await Task.Delay(100);
                Check(!shelf.IsExpanded, "pointer leave auto-collapses after grace");
            }
            settings.DockSide = "Left";
            shelf.ShowExpanded(false);
            Save("left");
            GetWindowRect(hwnd, out var left);
            Check(left.Left == System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea.Left, "left docking");
            shelf.ShowCollapsed();
            Check(File.ReadAllText(fixture) == "Drop test fixture: never modify this source.", "source file unchanged");
            Console.WriteLine($"PASS Drop: reveal, native hit-through, reversal, cache cleanup, drop routing, pin/remove/undo, reduced motion, left dock. Host placements: {shelf.HostPlacementCount - start} (all transition boundaries, not per-frame).");
        }
        finally { shelf.CloseForModuleStop(); }

        System.Windows.DragEventArgs Drag(System.Windows.IDataObject data, RoutedEvent routedEvent)
        {
            var constructor = typeof(System.Windows.DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(c => c.GetParameters().Length == 5);
            var args = (System.Windows.DragEventArgs)constructor.Invoke(new object[] { data,
                System.Windows.DragDropKeyStates.None, System.Windows.DragDropEffects.Copy, shelf, new System.Windows.Point(10, 10) });
            args.RoutedEvent = routedEvent;
            shelf.RaiseEvent(args);
            return args;
        }
        void Save(string name)
        {
            shelf.UpdateLayout();
            var visual = (FrameworkElement)shelf.Content;
            var bitmap = new RenderTargetBitmap((int)shelf.Width * 2, (int)shelf.Height * 2, 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.GetFullPath($"artifacts/drop-{name}.png")); encoder.Save(stream);
        }
        static void Check(bool condition, string name) { if (!condition) throw new Exception("Drop: " + name); }
    }
}

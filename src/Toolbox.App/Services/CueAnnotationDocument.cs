using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace PersonalToolbox.Services;

internal sealed record CueMark(Guid Id, string Tool, Point[] Points, Color Color, double Size, string Text = "", int Number = 0, double TextWidth = 0);

// Every gesture is one immutable object and one transaction, regardless of type.
internal sealed class CueAnnotationDocument
{
    private readonly List<CueMark> _items = [];
    private readonly List<CueMark[]> _undo = [];
    private readonly Stack<CueMark[]> _redo = new();
    internal IReadOnlyList<CueMark> Items => _items;
    internal bool CanUndo => _undo.Count > 0;
    internal bool CanRedo => _redo.Count > 0;
    internal int NextNumber => _items.Where(x => x.Tool == "Step").Select(x => x.Number).DefaultIfEmpty(0).Max() + 1;
    internal event Action? Changed;
    internal void Add(CueMark item) => Change(() => _items.Add(item));
    internal void Remove(Guid id)
    {
        if (_items.Any(x => x.Id == id)) Change(() => _items.RemoveAll(x => x.Id == id));
    }
    internal void Replace(CueMark item)
    {
        var index = _items.FindIndex(x => x.Id == item.Id);
        if (index < 0) return;
        var old = _items[index];
        if (old.Tool == item.Tool && old.Color == item.Color && old.Size == item.Size && old.Text == item.Text && old.Number == item.Number && old.TextWidth == item.TextWidth && old.Points.SequenceEqual(item.Points)) return;
        Change(() => _items[index] = item);
    }
    internal void Clear() { if (_items.Count > 0) Change(_items.Clear); }
    private void Change(Action action)
    {
        _undo.Add(_items.ToArray());
        if (_undo.Count > 150) _undo.RemoveAt(0);
        _redo.Clear(); action(); Changed?.Invoke();
    }
    internal void Undo()
    {
        if (!CanUndo) return;
        _redo.Push(_items.ToArray()); var previous = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); Restore(previous);
    }
    internal void Redo() { if (CanRedo) { _undo.Add(_items.ToArray()); Restore(_redo.Pop()); } }
    private void Restore(CueMark[] items) { _items.Clear(); _items.AddRange(items); Changed?.Invoke(); }
}

internal static class CueMarkDrawing
{
    internal static void Draw(DrawingContext dc, CueMark mark)
    {
        var brush = new SolidColorBrush(mark.Color);
        if (mark.Tool == "Text") { dc.DrawText(Text(mark), mark.Points[0]); return; }
        if (mark.Tool == "Step")
        {
            var p = mark.Points[0]; dc.DrawEllipse(brush, null, p, 15, 15);
            var label = new FormattedText(mark.Number.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 16,
                mark.Color.R * .299 + mark.Color.G * .587 + mark.Color.B * .114 > 180 ? Brushes.Black : Brushes.White, 1);
            dc.DrawText(label, new Point(p.X - label.Width / 2, p.Y - label.Height / 2)); return;
        }
        var pen = new System.Windows.Media.Pen(brush, mark.Size) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (mark.Tool == "Highlighter") dc.PushOpacity(.35);
        dc.DrawGeometry(null, pen, Geometry(mark));
        if (mark.Tool == "Highlighter") dc.Pop();
    }
    internal static bool Hit(CueMark mark, Point point)
    {
        if (mark.Tool == "Text")
        {
            var text = Text(mark); var rect = new Rect(mark.Points[0], new System.Windows.Size(Math.Max(10, text.Width), text.Height));
            rect.Inflate(5, 5); return rect.Contains(point);
        }
        if (mark.Tool == "Step") return (point - mark.Points[0]).Length <= 19;
        return Geometry(mark).StrokeContains(new System.Windows.Media.Pen(Brushes.Black, Math.Max(12, mark.Size + 8)), point);
    }
    private static FormattedText Text(CueMark mark)
    {
        var text = new FormattedText(mark.Text, System.Globalization.CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), mark.Size, new SolidColorBrush(mark.Color), 1);
        if (mark.TextWidth > 0) text.MaxTextWidth = mark.TextWidth;
        return text;
    }
    private static Geometry Geometry(CueMark mark)
    {
        var start = mark.Points[0]; var end = mark.Points[^1];
        if (mark.Tool == "Rectangle") return new RectangleGeometry(new Rect(start, end), 3, 3);
        if (mark.Tool == "Ellipse") return new EllipseGeometry(new Rect(start, end));
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(start, false, false);
            if (mark.Points.Length == 1) path.LineTo(start + new Vector(.1, 0), true, false);
            else if (mark.Tool is "Pen" or "Highlighter")
            {
                for (var i = 1; i < mark.Points.Length - 1; i++)
                    path.QuadraticBezierTo(mark.Points[i], mark.Points[i] + (mark.Points[i + 1] - mark.Points[i]) * .5, true, true);
                path.LineTo(end, true, true);
            }
            else path.LineTo(end, true, false);
            if (mark.Tool == "Arrow" && (end - start).Length > 2)
            {
                var direction = end - start; direction.Normalize();
                var wing = Math.Min((end - start).Length * .4, 12 + mark.Size);
                var normal = new Vector(-direction.Y, direction.X);
                path.BeginFigure(end - direction * wing + normal * wing * .5, false, false);
                path.LineTo(end, true, false); path.LineTo(end - direction * wing - normal * wing * .5, true, false);
            }
        }
        geometry.Freeze(); return geometry;
    }
}

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace RisupEditor;

// Decoration only: TextBox retains original text, selection and IME.
public sealed class SyntaxBox : TextBox
{
    SyntaxAdorner? decoration;
    AdornerLayer? layer;
    ScrollViewer? outerScroll;
    readonly DispatcherTimer delay = new() { Interval = TimeSpan.FromMilliseconds(100) };
    internal record Span(int Start, int End, int Color);
    internal List<Span> Highlights = new();
    internal int LastGeometryQueries;
    bool stale = true;
    static readonly List<WeakReference<SyntaxBox>> editors = new();
    static bool additionalChecks;
    public static bool AdditionalChecks
    {
        get => additionalChecks;
        set { if (additionalChecks == value) return; additionalChecks = value; editors.RemoveAll(w => !w.TryGetTarget(out _)); foreach (var weak in editors) if (weak.TryGetTarget(out var editor)) editor.QueueDiagnostics(); }
    }
    bool enableDiagnostics;
    public bool EnableDiagnostics { get => enableDiagnostics; set { enableDiagnostics = value; QueueDiagnostics(); } }
    public IReadOnlyList<SyntaxDiagnostic> Diagnostics { get; private set; } = Array.Empty<SyntaxDiagnostic>();
    public event EventHandler? DiagnosticsChanged;
    readonly DispatcherTimer diagnosticDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    int revision;
    void QueueDiagnostics()
    {
        revision++; diagnosticDelay.Stop();
        if (Diagnostics.Count > 0) { Diagnostics = Array.Empty<SyntaxDiagnostic>(); DiagnosticsChanged?.Invoke(this, EventArgs.Empty); }
        ToolTip = null; decoration?.InvalidateVisual();
        if (enableDiagnostics) diagnosticDelay.Start();
    }
    async void AnalyzeDiagnostics()
    {
        diagnosticDelay.Stop(); int version = revision; string source = Text; bool extra = AdditionalChecks;
        var found = await Task.Run(() => SyntaxDiagnostics.Analyze(source, extra));
        if (version != revision) return;
        Diagnostics = found; DiagnosticsChanged?.Invoke(this, EventArgs.Empty); decoration?.InvalidateVisual();
    }
    public SyntaxBox()
    {
        editors.Add(new WeakReference<SyntaxBox>(this));
        diagnosticDelay.Tick += (_, _) => AnalyzeDiagnostics();
        MouseMove += (_, e) => { int index = GetCharacterIndexFromPoint(e.GetPosition(this), true); ToolTip = Diagnostics.FirstOrDefault(d => index >= d.Start && index < d.Start + d.Length)?.Message; };
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        FontFamily = new FontFamily("Consolas, Malgun Gothic"); FontSize = 12;
        Padding = new Thickness(9, 7, 9, 7); BorderBrush = new SolidColorBrush(Color.FromRgb(216, 224, 233));
        delay.Tick += (_, _) => { delay.Stop(); Refresh(); };
        Loaded += (_, _) =>
        {
            layer = AdornerLayer.GetAdornerLayer(this);
            if (layer is not null) { decoration = new SyntaxAdorner(this); layer.Add(decoration); }
            for (DependencyObject? p = VisualTreeHelper.GetParent(this); p is not null; p = VisualTreeHelper.GetParent(p))
                if (p is ScrollViewer scroll) { outerScroll = scroll; break; }
            if (outerScroll is not null) outerScroll.ScrollChanged += Scrolled;
            Refresh();
        };
        Unloaded += (_, _) => { delay.Stop(); if (outerScroll is not null) outerScroll.ScrollChanged -= Scrolled; outerScroll = null; if (decoration is not null) layer?.Remove(decoration); decoration = null; layer = null; };
        TextChanged += (_, _) => { stale = true; QueueDiagnostics(); if (IsLoaded) { delay.Stop(); delay.Start(); } };
        SizeChanged += (_, _) => decoration?.InvalidateVisual();
        IsVisibleChanged += (_, _) =>
        {
            if (decoration is not null) { decoration.Visibility = IsVisible ? Visibility.Visible : Visibility.Collapsed; decoration.InvalidateVisual(); }
            if (IsVisible && IsLoaded) Refresh();
        };
    }
    void Scrolled(object sender, ScrollChangedEventArgs e) => decoration?.InvalidateVisual();
    void Refresh() { if (stale) { Highlights = Scan(Text); stale = false; } decoration?.InvalidateVisual(); }
    internal static List<Span> Scan(string text)
    {
        var spans = new List<Span>();
        foreach (Match m in Regex.Matches(text, @"(?m)^#[^\r\n]*|</?[A-Za-z_][^>\r\n]*>")) spans.Add(new(m.Index, m.Index + m.Length, text[m.Index] == '#' ? 0 : 1));
        for (int i = 0; i + 1 < text.Length; i++)
        {
            if (text[i] != '{' || text[i + 1] != '{') continue;
            int start = i, depth = 1; i += 2;
            while (i < text.Length && depth > 0)
            {
                if (i + 1 < text.Length && text[i] == '{' && text[i + 1] == '{') { depth++; i += 2; }
                else if (i + 1 < text.Length && text[i] == '}' && text[i + 1] == '}') { depth--; i += 2; }
                else i++;
            }
            spans.Add(new(start, i, 2)); i--;
        }
        return spans;
    }
    sealed class SyntaxAdorner : Adorner
    {
        readonly SyntaxBox box;
        static readonly Brush[] colors = new[] { Colors.Orange, Colors.ForestGreen, Colors.DodgerBlue }.Select(c => { var b = new SolidColorBrush(Color.FromArgb(32, c.R, c.G, c.B)); b.Freeze(); return (Brush)b; }).ToArray();
        public SyntaxAdorner(SyntaxBox box) : base(box) { this.box = box; IsHitTestVisible = false; Visibility = box.IsVisible ? Visibility.Visible : Visibility.Collapsed; }
        protected override void OnRender(DrawingContext dc)
        {
            box.LastGeometryQueries = 0;
            if (!box.IsVisible || !box.IsLoaded || box.stale || box.Text.Length == 0 || (box.Highlights.Count == 0 && box.Diagnostics.Count == 0)) return;
            Rect visible = new Rect(box.RenderSize);
            // The adorner lives above the card tree. Clip it against every ancestor,
            // including collapsed Expander content and clipped scroll presenters.
            for (DependencyObject? parent = VisualTreeHelper.GetParent(box); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not UIElement element) continue;
                if (!element.IsVisible) return;
                if (element.ClipToBounds || element.Clip is not null || element is ScrollContentPresenter)
                {
                    Rect bounds = element.Clip?.Bounds ?? new Rect(element.RenderSize);
                    visible.Intersect(element.TransformToVisual(box).TransformBounds(bounds));
                    if (visible.IsEmpty) return;
                }
            }
            if (box.outerScroll is { } scroll)
            {
                var viewport = scroll.TranslatePoint(new Point(), box);
                visible.Intersect(new Rect(viewport, new Size(scroll.ActualWidth, scroll.ActualHeight)));
            }
            if (visible.IsEmpty || visible.Height <= 0) return;
            int first = box.GetCharacterIndexFromPoint(new Point(visible.Left + box.Padding.Left + 1, visible.Top + 1), true);
            int last = box.GetCharacterIndexFromPoint(new Point(visible.Right - 1, visible.Bottom - 1), true);
            if (first < 0 || last < 0) return;
            int firstLine = box.GetLineIndexFromCharacterIndex(first), lastLine = box.GetLineIndexFromCharacterIndex(last);
            if (firstLine < 0 || lastLine < firstLine) return;
            var lines = new List<(int Start, int End)>();
            for (int line = firstLine; line <= lastLine; line++)
            {
                int start = box.GetCharacterIndexFromLineIndex(line), end = Math.Min(box.Text.Length, start + box.GetLineLength(line));
                while (end > start && box.Text[end - 1] is '\r' or '\n') end--;
                lines.Add((start, end));
            }
            dc.PushClip(new RectangleGeometry(visible));
            foreach (var span in box.Highlights)
            {
                if (span.End <= lines[0].Start || span.Start >= lines[^1].End) continue;
                foreach (var line in lines)
                {
                    int start = Math.Max(span.Start, line.Start), end = Math.Min(span.End, line.End);
                    if (start >= end) continue;
                    Rect a = box.GetRectFromCharacterIndex(start), b = box.GetRectFromCharacterIndex(end - 1, true); box.LastGeometryQueries += 2;
                    if (!a.IsEmpty && !b.IsEmpty) dc.DrawRectangle(colors[span.Color], null, new Rect(a.X, a.Y, Math.Max(1, b.Right - a.X), a.Height));
                }
            }
            foreach (var diagnostic in box.Diagnostics)
            {
                if (diagnostic.Start + diagnostic.Length <= lines[0].Start || diagnostic.Start >= lines[^1].End) continue;
                foreach (var line in lines)
                {
                    int start = Math.Max(diagnostic.Start, line.Start), end = Math.Min(diagnostic.Start + diagnostic.Length, line.End);
                    if (start >= end) continue;
                    Rect a = box.GetRectFromCharacterIndex(start), b = box.GetRectFromCharacterIndex(end - 1, true); box.LastGeometryQueries += 2;
                    if (a.IsEmpty || b.IsEmpty) continue;
                    var geometry = new StreamGeometry();
                    using (var context = geometry.Open()) { context.BeginFigure(new Point(a.X, a.Bottom - 1), false, false); for (double x = a.X + 2; x <= Math.Max(a.X + 2, b.Right); x += 2) context.LineTo(new Point(x, a.Bottom - (((int)((x-a.X)/2) % 2 == 0) ? 1 : 3)), true, false); }
                    geometry.Freeze(); dc.DrawGeometry(null, new Pen(diagnostic.Warning ? Brushes.DarkGoldenrod : Brushes.Crimson, 1), geometry);
                }
            }
            dc.Pop();
        }
    }
}

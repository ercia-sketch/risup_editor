using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RisupEditor;
public sealed partial class MainWindow
{
    public async void RunPerformanceTest(string folder)
    {
        try
        {
            string sample = "{{#if " + string.Concat(Enumerable.Repeat("긴 프롬프트 테스트 abcdefghijklmnopqrstuvwxyz ", 300)) + "}}";
            var box = new SyntaxBox { Text = sample, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Width = 550 };
            var scroll = new ScrollViewer { Content = box, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Content = new AdornerDecorator { Child = scroll }; UpdateLayout(); await Task.Delay(180);
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < sample.Length; i++) { box.GetRectFromCharacterIndex(i); box.GetRectFromCharacterIndex(i, true); }
            timer.Stop(); double legacy = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            var surface = (FrameworkElement)Content;
            var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
            timer.Stop(); double current = timer.Elapsed.TotalMilliseconds; int queries = box.LastGeometryQueries;
            if (queries <= 0 || queries >= sample.Length / 4) throw new Exception($"Geometry not bounded: {queries}");
            if (box.Text != sample) throw new Exception("Original text changed");
            scroll.ScrollToVerticalOffset(800); await Task.Delay(80); UpdateLayout(); bitmap.Render(surface);
            if (box.LastGeometryQueries <= 0) throw new Exception("Scrolled highlight missing");
            var edit = Stopwatch.StartNew(); box.AppendText(" 입력"); edit.Stop(); await Task.Delay(160);
            if (box.Text != sample + " 입력") throw new Exception("Edited text changed");
            File.WriteAllText(Path.Combine(folder, "performance-result.txt"), $"PASS\nCharacters: {sample.Length}\nLegacy geometry queries: {sample.Length * 2}\nVisible geometry queries: {queries}\nLegacy geometry loop ms: {legacy:F2}\nOptimized bitmap render ms: {current:F2}\nAppendText ms: {edit.Elapsed.TotalMilliseconds:F2}\nScroll highlight and original text preservation: PASS");
            dirty = false; Application.Current.Shutdown(0);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "performance-result.txt"), ex.ToString()); dirty = false; Application.Current.Shutdown(1); }
    }
}

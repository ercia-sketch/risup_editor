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
    public async void RunDiagnosticTest(string folder)
    {
        try
        {
            DiagnosticTests.Run();
            var box = new SyntaxBox { EnableDiagnostics = true, Text = "{{#when::1}}\n검사", TextWrapping = TextWrapping.Wrap, Width = 550 };
            Content = new AdornerDecorator { Child = new ScrollViewer { Content = box } };
            UpdateLayout(); await Task.Delay(600);
            if (box.Diagnostics.Count == 0) throw new Exception("missing async diagnostic");
            box.Text = "{{char}}"; if (box.Diagnostics.Count != 0) throw new Exception("stale diagnostic"); await Task.Delay(400);
            if (box.Diagnostics.Count != 0) throw new Exception("valid CBS flagged");
            box.Text = "<example>"; await Task.Delay(400); if (box.Diagnostics.Count != 0) throw new Exception("default tag warning");
            SyntaxBox.AdditionalChecks = true; await Task.Delay(400); if (box.Diagnostics.Count != 1 || !box.Diagnostics[0].Warning) throw new Exception("additional tag warning missing");
            var sample = "{{#when::1}}\n" + string.Concat(Enumerable.Repeat("{{char}} 긴 문법 검사 테스트\n", 4000));
            var timer = Stopwatch.StartNew(); box.Text = sample; timer.Stop(); double input = timer.Elapsed.TotalMilliseconds;
            for (int wait = 0; wait < 100 && box.Diagnostics.Count != 1; wait++) await Task.Delay(50);
            UpdateLayout();
            if (box.Text != sample || box.Diagnostics.Count != 1) throw new Exception($"long document diagnostics: {box.Diagnostics.Count}");
            var surface = (FrameworkElement)Content; var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            timer.Restart(); bitmap.Render(surface); timer.Stop();
            if (box.LastGeometryQueries <= 0 || box.LastGeometryQueries > 2000) throw new Exception("unbounded or missing paint");
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(folder, "diagnostic-preview.png"))) png.Save(file);
            File.WriteAllText(Path.Combine(folder, "diagnostic-result.txt"), $"PASS: rules, async refresh, stale result removal, optional tags, original text preservation\nCharacters: {sample.Length}\nText assignment ms: {input:F2}\nBitmap render ms: {timer.Elapsed.TotalMilliseconds:F2}\nVisible geometry queries: {box.LastGeometryQueries}");
            dirty = false; Application.Current.Shutdown(0);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "diagnostic-result.txt"), ex.ToString()); dirty = false; Application.Current.Shutdown(1); }
    }
}

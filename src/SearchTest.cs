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
    public async void RunSearchTest(string folder)
    {
        try
        {
            OpenPaths([Path.Combine(folder, "reference.risup")]); Copy(true);
            int inner = blocks.FindIndex(Inner); var block = blocks[inner]; block.Set("innerFormat", Value.String("<section>검색대상_숨김 {{slot}}</section>"));
            var nested = Value.Map(); var inside = Value.Map(); inside.Set("value", Value.String("검색대상_중첩")); nested.Set("inside", Value.Array([inside])); basis!.Data.Set("search_test", nested);
            collapsedWork.Add(block); workSections = null; RenderWork(); UpdateLayout();
            var card = workCards[block]; var view = (BlockView)card.Tag;
            if (view.Details.Visibility != Visibility.Collapsed) throw new Exception("fixture not collapsed");
            var workPane = searchPanes[1]; workPane.Bar.Visibility = Visibility.Visible; workPane.Query.Text = "검색대상_숨김"; workPane.Timer.Stop(); await RefreshSearch(1); UpdateLayout();
            if (workPane.Hits.Count != 1 || view.Details.Visibility != Visibility.Visible) throw new Exception("hidden block navigation failed");
            var editor = LogicalDescendants<SyntaxBox>(card).First(t => t.Tag is FieldAddress a && a.Key == "innerFormat");
            if (editor.SelectedText != "검색대상_숨김") throw new Exception("search selection failed");
            await Task.Delay(150); view.Details.Visibility = Visibility.Collapsed; UpdateLayout(); await Task.Delay(80);
            var layer = AdornerLayer.GetAdornerLayer(editor); var adorners = layer?.GetAdorners(editor) ?? [];
            if (editor.IsVisible || adorners.Any(a => a.Visibility == Visibility.Visible)) throw new Exception("collapsed decoration still visible");
            view.Details.Visibility = Visibility.Visible; UpdateLayout(); await Task.Delay(100);
            if (!editor.IsVisible || !(layer?.GetAdorners(editor)?.Any(a => a.Visibility == Visibility.Visible) ?? false)) throw new Exception("expanded decoration missing");
            workPane.Query.Text = "검색대상_중첩"; workPane.Timer.Stop(); await RefreshSearch(1); UpdateLayout();
            if (workPane.Hits.Count != 1 || !LogicalDescendants<TextBox>(work).Any(t => t.SelectedText == "검색대상_중첩" && t.IsVisible)) throw new Exception("nested data search failed");
            customToggleText = "test=토글검색이름=select=첫째,선택지검색\nnote=입력=text"; SetToggleEditor(); toggleValues["note"] = "입력값검색"; RenderTogglePreview();
            var togglePane = searchPanes[2]; togglePane.Bar.Visibility = Visibility.Visible; togglePane.Query.Text = "입력값검색"; togglePane.Timer.Stop(); await RefreshSearch(2);
            if (togglePane.Hits.Count != 1 || !LogicalDescendants<TextBox>(togglePreview).Any(t => t.SelectedText == "입력값검색")) throw new Exception("toggle input search");
            string before = toggleValues.GetValueOrDefault("test", ""); togglePane.Query.Text = "선택지검색"; togglePane.Timer.Stop(); await RefreshSearch(2); UpdateLayout();
            if (togglePane.Hits.Count != 1 || toggleValues.GetValueOrDefault("test", "") != before) throw new Exception("toggle choice search mutated value");
            CloseSearch(2); CloseSearch(1);
            var referenceBlock = references[0].Blocks!.First(Inner); referenceBlock.Set("innerFormat", Value.String("참조검색_숨김")); collapsedReferences[0].Add(referenceBlock); RenderReferences(); UpdateLayout();
            var referencePane = searchPanes[0]; referencePane.Bar.Visibility = Visibility.Visible; referencePane.Query.Text = "참조검색_숨김"; referencePane.Timer.Stop(); await RefreshSearch(0); UpdateLayout();
            if (referencePane.Hits.Count != 1 || !LogicalDescendants<TextBox>(SearchRoot(0)).Any(t => t.SelectedText == "참조검색_숨김" && t.IsVisible)) throw new Exception("reference search failed");
            string large = string.Concat(Enumerable.Repeat("검색 긴 텍스트 abcdefghijklmnopqrstuvwxyz\n", 4000)); blocks[0].Set("text", Value.String(large)); RenderWork();
            workPane.Bar.Visibility = Visibility.Visible; workPane.Query.Text = "긴 텍스트"; workPane.Timer.Stop(); var timer = Stopwatch.StartNew(); await RefreshSearch(1); timer.Stop();
            if (workPane.Hits.Count != 4000) throw new Exception($"large result count: {workPane.Hits.Count}");
            NavigateSearch(1, 1); if (workPane.Index != 1) throw new Exception("next match"); NavigateSearch(1, -1); if (workPane.Index != 0) throw new Exception("previous match");
            CloseSearch(1); CloseSearch(0);
            var surface = (FrameworkElement)Content; UpdateLayout(); var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(folder, "search-preview.png"))) png.Save(file);
            File.WriteAllText(Path.Combine(folder, "search-result.txt"), $"PASS: collapsed decoration hidden, expanded restored, reference/work/nested/toggle search, selection preserved, next/previous, no toggle mutation\nCharacters: {large.Length}\nMatches: 4000\nSearch and first navigation ms: {timer.Elapsed.TotalMilliseconds:F2}");
            dirty = false; Application.Current.Shutdown(0);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "search-result.txt"), ex.ToString()); dirty = false; Application.Current.Shutdown(1); }
    }
}

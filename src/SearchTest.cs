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
            SetSearchScope(1, SearchSection.Blocks); var workPane = searchPanes[1]; workPane.Bar.Visibility = Visibility.Visible; workPane.Query.Text = "검색대상_숨김"; workPane.Timer.Stop(); await RefreshSearch(1); UpdateLayout();
            if (workPane.Hits.Count != 1 || view.Details.Visibility != Visibility.Visible) throw new Exception("hidden block navigation failed");
            var editor = LogicalDescendants<SyntaxBox>(card).First(t => t.Tag is FieldAddress a && a.Key == "innerFormat");
            if (editor.SelectedText != "검색대상_숨김") throw new Exception("search selection failed");
            await Task.Delay(150); view.Details.Visibility = Visibility.Collapsed; UpdateLayout(); await Task.Delay(80);
            var layer = AdornerLayer.GetAdornerLayer(editor); var adorners = layer?.GetAdorners(editor) ?? [];
            if (editor.IsVisible || adorners.Any(a => a.Visibility == Visibility.Visible)) throw new Exception("collapsed decoration still visible");
            view.Details.Visibility = Visibility.Visible; UpdateLayout(); await Task.Delay(100);
            if (!editor.IsVisible || !(layer?.GetAdorners(editor)?.Any(a => a.Visibility == Visibility.Visible) ?? false)) throw new Exception("expanded decoration missing");
            SetSearchScope(1, SearchSection.Other); workPane.Query.Text = "검색대상_중첩"; workPane.Timer.Stop(); await RefreshSearch(1); UpdateLayout();
            if (workPane.Hits.Count != 1 || !LogicalDescendants<TextBox>(work).Any(t => t.SelectedText == "검색대상_중첩" && t.IsVisible)) throw new Exception("nested data search failed");
            customToggleText = "test=토글검색이름=select=첫째,선택지검색\nnote=입력=text"; SetToggleEditor(); toggleValues["note"] = "입력값검색"; RenderTogglePreview();
            SetSearchScope(1, SearchSection.Toggles); workPane.Query.Text = "선택지검색"; workPane.Timer.Stop(); string before = toggleValues.GetValueOrDefault("test", ""); await RefreshSearch(1); UpdateLayout();
            if (workPane.Hits.Count != 1 || toggleEditor.SelectedText != "선택지검색" || toggleValues.GetValueOrDefault("test", "") != before) throw new Exception("toggle source search failed or mutated value");
            SetSearchScope(1, SearchSection.Other); workPane.Query.Text = "최대 컨텍스트 크기"; workPane.Timer.Stop(); await RefreshSearch(1);
            if (workPane.Hits.Count != 0) throw new Exception("other field label leaked into search");
            CloseSearch(1); DisableSearch(); OpenSearch();
            if (activeSearchPane != -1 || workPane.Bar.Visibility != Visibility.Collapsed) throw new Exception("chat toggle Ctrl+F was not disabled");
            var referenceBlock = references[0].Blocks!.First(Inner); referenceBlock.Set("innerFormat", Value.String("참조검색_숨김")); collapsedReferences[0].Add(referenceBlock); RenderReferences(); UpdateLayout();
            SetSearchScope(0, SearchSection.Blocks); var referencePane = searchPanes[0]; referencePane.Bar.Visibility = Visibility.Visible; referencePane.Query.Text = "참조검색_숨김"; referencePane.Timer.Stop(); await RefreshSearch(0); UpdateLayout();
            if (referencePane.Hits.Count != 1 || !LogicalDescendants<TextBox>(SearchRoot(0)).Any(t => t.SelectedText == "참조검색_숨김" && t.IsVisible)) throw new Exception("reference search failed");
            if (!referencePane.Label.Text.Contains(Current!.Name, StringComparison.Ordinal)) throw new Exception("reference preset name missing from search scope");
            referenceBlock.Set("innerFormat", Value.String("참조검색_숨김 {{slot}} 참조미리보기검색값")); showingReferencePreview = true; ApplyReferencePreviewVisibility(); RefreshReferencePromptPreview(); SetSearchScope(0, SearchSection.Preview);
            referencePane.Query.Text = "참조미리보기검색값"; referencePane.Timer.Stop(); await RefreshSearch(0); UpdateLayout();
            if (referencePane.Hits.Count != 1 || !(CurrentReferenceView?.TextSurfaces.Any(surface => surface.Box.Selection.Text == "참조미리보기검색값") ?? false)) throw new Exception("reference preview scope search failed");
            if (!referencePane.Label.Text.Contains("미리보기", StringComparison.Ordinal) || !referencePane.Label.Text.Contains(Current!.Name, StringComparison.Ordinal)) throw new Exception("reference preview search label failed");
            showingReferencePreview = false; ApplyReferencePreviewVisibility(); SetSearchScope(0, SearchSection.Blocks);
            var script = Value.Map(); script.Set("comment", Value.String("정규식검색값")); script.Set("type", Value.String("editoutput")); script.Set("in", Value.String("찾을정규식")); script.Set("out", Value.String("바꿀정규식")); regexScripts.Add(script); workSections = null; RenderWork();
            SetSearchScope(1, SearchSection.Regex); workPane.Bar.Visibility = Visibility.Visible; workPane.Query.Text = "찾을정규식"; workPane.Timer.Stop(); await RefreshSearch(1); UpdateLayout();
            if (workPane.Hits.Count != 1 || !LogicalDescendants<TextBox>(work).Any(t => t.SelectedText == "찾을정규식" && t.IsVisible)) throw new Exception("regex value search failed");
            workPane.Query.Text = "Modification Type"; workPane.Timer.Stop(); await RefreshSearch(1); if (workPane.Hits.Count != 0) throw new Exception("regex field label leaked into search");
            blocks[0].Set("text", Value.String("미리보기검색값")); showingPreview = true; workScroll.Content = promptPreview; RefreshPromptPreview(); SetSearchScope(1, SearchSection.Preview);
            workPane.Query.Text = "미리보기검색값"; workPane.Timer.Stop(); await RefreshSearch(1); UpdateLayout();
            if (workPane.Hits.Count != 1 || !previewTextSurfaces.Any(surface => surface.Box.Selection.Text == "미리보기검색값")) throw new Exception("preview scope search failed");
            showingPreview = false; workScroll.Content = work;
            string large = string.Concat(Enumerable.Repeat("검색 긴 텍스트 abcdefghijklmnopqrstuvwxyz\n", 4000)); blocks[0].Set("text", Value.String(large)); RenderWork();
            SetSearchScope(1, SearchSection.Blocks); workPane.Bar.Visibility = Visibility.Visible; workPane.Query.Text = "긴 텍스트"; workPane.Timer.Stop(); var timer = Stopwatch.StartNew(); await RefreshSearch(1); timer.Stop();
            if (workPane.Hits.Count != 4000) throw new Exception($"large result count: {workPane.Hits.Count}");
            NavigateSearch(1, 1); if (workPane.Index != 1) throw new Exception("next match"); NavigateSearch(1, -1); if (workPane.Index != 0) throw new Exception("previous match");
            CloseSearch(1); CloseSearch(0);
            var surface = (FrameworkElement)Content; UpdateLayout(); var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(folder, "search-preview.png"))) png.Save(file);
            File.WriteAllText(Path.Combine(folder, "search-result.txt"), $"PASS: section-scoped reference/work/block/toggle/regex/other previews, labels excluded, chat toggle disabled, selection preserved, next/previous\nCharacters: {large.Length}\nMatches: 4000\nSearch and first navigation ms: {timer.Elapsed.TotalMilliseconds:F2}");
            dirty = false; Application.Current.Shutdown(0);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "search-result.txt"), ex.ToString()); dirty = false; Application.Current.Shutdown(1); }
    }
}

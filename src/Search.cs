using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RisupEditor;

public sealed partial class MainWindow
{
    record FieldAddress(Value Owner, string Key);
    record ValueRow(int Index);
    record OtherRoot(Value Data);
    record RegexRoot(List<Value> Items);
    record ToggleAddress(int Line);
    record BlockView(Value Block, Panel Details, Panel Settings, Expander SettingsExpander, HashSet<Value> Collapsed);
    record SearchEntry(string Text, Action<int, int> Locate);
    record SearchHit(SearchEntry Entry, int Offset, int Length);
    sealed class SearchPane
    {
        public required string Name;
        public required DockPanel Host;
        public required Border Bar;
        public required TextBox Query;
        public required TextBlock Count;
        public List<SearchHit> Hits = new();
        public int Index = -1, Revision;
        public readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    }
    readonly List<SearchPane> searchPanes = new();
    int activeSearchPane;
    Action? clearSearchMark;
    bool searchNavigating;

    void AttachSearch(DockPanel host, string name)
    {
        int scope = searchPanes.Count;
        var query = new TextBox { MinWidth = 50, Padding = new Thickness(4), FontSize = 12 };
        var count = Text("0 / 0", 11, Muted); count.VerticalAlignment = VerticalAlignment.Center;
        var barContent = new DockPanel();
        var bar = new Border { Visibility = Visibility.Collapsed, Padding = new Thickness(2, 4, 4, 6) };
        var pane = new SearchPane { Name = name, Host = host, Bar = bar, Query = query, Count = count };
        searchPanes.Add(pane);
        var label = Text(name, 10, Muted); label.Margin = new Thickness(0, 0, 0, 3);
        var wrap = new DockPanel(); DockPanel.SetDock(label, Dock.Top); wrap.Children.Add(label); wrap.Children.Add(barContent); bar.Child = wrap;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(count); buttons.Children.Add(Button("↑", () => NavigateSearch(scope, -1), compact: true)); buttons.Children.Add(Button("↓", () => NavigateSearch(scope, 1), compact: true)); buttons.Children.Add(Button("×", () => CloseSearch(scope), compact: true));
        DockPanel.SetDock(buttons, Dock.Right); barContent.Children.Add(buttons); barContent.Children.Add(query);
        DockPanel.SetDock(bar, Dock.Top); host.Children.Insert(1, bar);
        host.PreviewMouseDown += (_, _) => activeSearchPane = scope;
        host.GotKeyboardFocus += (_, _) => { if (!searchNavigating) activeSearchPane = scope; };
        pane.Timer.Tick += async (_, _) => { pane.Timer.Stop(); await RefreshSearch(scope); };
        query.TextChanged += (_, _) => ScheduleSearch(scope);
        query.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { NavigateSearch(scope, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); e.Handled = true; }
            if (e.Key == Key.Escape) { CloseSearch(scope); e.Handled = true; }
        };
        host.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, e) => { if (e.OriginalSource != query && !searchNavigating) ScheduleSearch(scope); }));
    }
    void OpenSearch()
    {
        var pane = searchPanes[activeSearchPane]; pane.Bar.Visibility = Visibility.Visible; pane.Query.Focus(); pane.Query.SelectAll(); ScheduleSearch(activeSearchPane);
    }
    void CloseSearch(int scope)
    {
        var pane = searchPanes[scope]; pane.Revision++; pane.Timer.Stop(); pane.Bar.Visibility = Visibility.Collapsed; pane.Hits.Clear(); pane.Index = -1;
        clearSearchMark?.Invoke(); clearSearchMark = null; Keyboard.ClearFocus();
    }
    void ScheduleSearch(int scope)
    {
        if (scope < 0 || scope >= searchPanes.Count) return;
        var pane = searchPanes[scope]; if (pane.Bar.Visibility != Visibility.Visible) return;
        pane.Revision++; pane.Timer.Stop(); pane.Timer.Start();
    }
    void SearchDataChanged() { for (int i = 0; i < searchPanes.Count; i++) ScheduleSearch(i); }
    async Task RefreshSearch(int scope, int move = 0)
    {
        var pane = searchPanes[scope]; int revision = pane.Revision; string query = pane.Query.Text;
        if (query.Length == 0) { pane.Hits.Clear(); pane.Index = -1; pane.Count.Text = "0 / 0"; clearSearchMark?.Invoke(); clearSearchMark = null; return; }
        var entries = SearchEntries(scope).ToArray();
        var hits = await Task.Run(() =>
        {
            var found = new List<SearchHit>();
            foreach (var entry in entries)
                for (int index = 0; index <= entry.Text.Length - query.Length;)
                {
                    int match = entry.Text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase); if (match < 0) break;
                    found.Add(new(entry, match, query.Length)); index = match + query.Length;
                }
            return found;
        });
        if (revision != pane.Revision || pane.Bar.Visibility != Visibility.Visible) return;
        pane.Hits = hits; pane.Index = hits.Count == 0 ? -1 : move < 0 ? hits.Count - 1 : 0;
        ShowSearchHit(scope);
    }
    async void NavigateSearch(int scope, int step)
    {
        var pane = searchPanes[scope];
        if (pane.Timer.IsEnabled) { pane.Timer.Stop(); await RefreshSearch(scope, step); return; }
        if (pane.Hits.Count == 0) return;
        pane.Index = (pane.Index + step + pane.Hits.Count) % pane.Hits.Count; ShowSearchHit(scope);
    }
    void ShowSearchHit(int scope)
    {
        var pane = searchPanes[scope]; pane.Count.Text = $"{pane.Index + 1} / {pane.Hits.Count}";
        clearSearchMark?.Invoke(); clearSearchMark = null;
        if (pane.Index < 0) return;
        searchNavigating = true;
        try { var hit = pane.Hits[pane.Index]; hit.Entry.Locate(hit.Offset, hit.Length); }
        finally { searchNavigating = false; }
    }
    IEnumerable<SearchEntry> SearchEntries(int scope)
    {
        if (scope == 1 && showingPreview)
        {
            foreach (var surface in previewTextSurfaces)
            {
                var target = surface;
                yield return new(target.Text, (start, length) => MarkPreviewSearch(target, start, length));
            }
            foreach (var item in previewAuxiliaryText)
            {
                var target = item;
                yield return new(target.Text, (start, length) => MarkSearch(target.Element, start, length));
            }
            yield break;
        }
        if (scope == 2)
        {
            foreach (var def in ParseToggles(out _))
            {
                int line = def.Line;
                yield return new(def.Name, (start, length) => LocateToggle(line, null, false, start, length));
                for (int i = 0; i < def.Options.Length; i++) { int option = i; yield return new(def.Options[i], (start, length) => LocateToggle(line, option, false, start, length)); }
                if (def.Type is "text" or "textarea") yield return new(toggleValues.GetValueOrDefault(def.Key, ""), (start, length) => LocateToggle(line, null, true, start, length));
            }
            yield break;
        }
        Preset? preset = scope == 0 ? Current : basis;
        var sourceBlocks = scope == 0 ? Current?.Blocks : blocks;
        foreach (var block in sourceBlocks ?? [])
        {
            if (string.IsNullOrEmpty(block.Str("name")))
                yield return new(Label(block), (start, length) =>
                {
                    EnsureWorkEditor(scope);
                    var card = LogicalDescendants<Border>(SearchRoot(scope)).FirstOrDefault(b => b.Tag is BlockView view && ReferenceEquals(view.Block, block));
                    if (card?.Tag is BlockView view) { view.Collapsed.Remove(block); view.Details.Visibility = Visibility.Visible; var title = LogicalDescendants<TextBlock>(card).FirstOrDefault(t => t.Text.EndsWith("   " + Label(block))); if (title is not null) MarkSearch(title, Math.Max(0, title.Text.Length - Label(block).Length) + start, length); }
                });
            foreach (var item in ValueEntries(block))
            {
                var path = item.Path; bool key = item.Key;
                yield return new(item.Text, (start, length) => LocateBlock(scope, block, path, key, start, length));
            }
        }
        var regex = scope == 0 ? Current?.Data.Get("regex")?.Items : regexScripts;
        if (regex is not null)
        {
            var regexValue = new Value { Items = regex };
            foreach (var item in ValueEntries(regexValue))
            {
                var path = item.Path; bool key = item.Key;
                yield return new(item.Text, (start, length) =>
                {
                    EnsureWorkEditor(scope); var root = LogicalDescendants<Panel>(SearchRoot(scope)).FirstOrDefault(p => p.Tag is RegexRoot tag && ReferenceEquals(tag.Items, regex));
                    if (root is not null) LocateValue(root, path, key, start, length);
                });
            }
        }
        yield return new(scope == 0 ? Current?.Data.Str("customPromptTemplateToggle") ?? "" : customToggleText, (start, length) =>
        {
            EnsureWorkEditor(scope); var root = SearchRoot(scope);
            var editor = scope == 1 ? toggleEditor : LogicalDescendants<TextBox>(root).FirstOrDefault(t => Equals(t.Tag, "SearchToggleConfig"));
            if (editor is not null) MarkSearch(editor, start, length);
        });
        if (preset is not null) foreach (var item in ValueEntries(preset.Data, true))
        {
            var path = item.Path; bool key = item.Key;
            yield return new(item.Text, (start, length) =>
            {
                EnsureWorkEditor(scope); var root = LogicalDescendants<Panel>(SearchRoot(scope)).FirstOrDefault(p => p.Tag is OtherRoot tag && ReferenceEquals(tag.Data, preset.Data));
                if (root is not null) LocateValue(root, path, key, start, length);
            });
        }
    }
    static IEnumerable<(string Text, int[] Path, bool Key)> ValueEntries(Value value, bool otherOnly = false, int[]? prefix = null)
    {
        prefix ??= [];
        int count = value.Fields?.Count ?? value.Items?.Count ?? 0;
        for (int i = 0; i < count; i++)
        {
            var name = value.Fields?[i].Key.Text(); if (otherOnly && !OtherKey(name)) continue;
            var path = prefix.Append(i).ToArray(); var child = value.Fields is not null ? value.Fields[i].Item : value.Items![i];
            if (name is not null) yield return (name, path, true);
            if (child.IsMap || child.IsArray) { foreach (var nested in ValueEntries(child, false, path)) yield return nested; }
            else { string? text = child.Raw?[0] is 0xc2 or 0xc3 ? child.Boolean().ToString() : Scalar(child); if (text is not null) yield return (text, path, false); }
        }
    }
    DependencyObject SearchRoot(int scope) => scope == 0 ? (tabs.SelectedItem as TabItem)?.Content as DependencyObject ?? tabs : scope == 1 && showingPreview ? promptPreview : work;
    void EnsureWorkEditor(int scope) { }
    void LocateBlock(int scope, Value block, int[] path, bool key, int start, int length)
    {
        EnsureWorkEditor(scope);
        var card = LogicalDescendants<Border>(SearchRoot(scope)).FirstOrDefault(b => b.Tag is BlockView view && ReferenceEquals(view.Block, block));
        if (card?.Tag is not BlockView view) return;
        view.Collapsed.Remove(block); view.Details.Visibility = Visibility.Visible;
        string? field = path.Length == 1 ? block.Fields?[path[0]].Key.Text() : null;
        var target = LogicalDescendants<FrameworkElement>(card).FirstOrDefault(e => e.Tag is FieldAddress a && ReferenceEquals(a.Owner, block) && a.Key == field);
        if (target is not null && !key)
        {
            view.SettingsExpander.IsExpanded = field != "text";
            MarkSearch(target, start, length); return;
        }
        view.SettingsExpander.IsExpanded = true;
        var extra = view.Settings.Children.OfType<Expander>().FirstOrDefault(e => Equals(e.Tag, "SearchBlockFields"));
        if (extra is null) { var body = new StackPanel(); extra = new Expander { Header = "추가 블록 필드", Tag = "SearchBlockFields", Content = body }; view.Settings.Children.Add(extra); }
        ((Panel)extra.Content).Children.Clear(); BuildValues((Panel)extra.Content, block, false);
        extra.IsExpanded = true; LocateValue((Panel)extra.Content, path, key, start, length);
    }
    void LocateValue(Panel root, int[] path, bool key, int start, int length)
    {
        Panel current = root;
        for (int depth = 0; depth < path.Length; depth++)
        {
            var row = current.Children.OfType<Panel>().FirstOrDefault(p => p.Tag is ValueRow tag && tag.Index == path[depth]); if (row is null) return;
            if (depth == path.Length - 1)
            {
                FrameworkElement target = key ? LogicalDescendants<TextBlock>(row).First() : row.Children.OfType<FrameworkElement>().FirstOrDefault(e => e is TextBox or CheckBox) ?? row;
                MarkSearch(target, start, length); return;
            }
            var expander = row.Children.OfType<Expander>().FirstOrDefault(); if (expander is null) return;
            expander.IsExpanded = true; current = (Panel)expander.Content;
        }
    }
    void LocateToggle(int line, int? option, bool value, int start, int length)
    {
        var node = LogicalDescendants<FrameworkElement>(togglePreview).FirstOrDefault(e => e.Tag is ToggleAddress a && a.Line == line); if (node is null) return;
        for (DependencyObject? parent = LogicalTreeHelper.GetParent(node); parent is not null; parent = LogicalTreeHelper.GetParent(parent)) if (parent is Expander expander) expander.IsExpanded = true;
        if (option is int index && LogicalDescendants<ComboBox>(node).FirstOrDefault() is { } combo)
        {
            combo.IsDropDownOpen = true; combo.UpdateLayout();
            if (combo.ItemContainerGenerator.ContainerFromIndex(index) is ComboBoxItem item) MarkSearch(item, start, length); else MarkSearch(combo, start, length);
            return;
        }
        FrameworkElement target = value ? LogicalDescendants<TextBox>(node).FirstOrDefault() ?? node : (FrameworkElement?)LogicalDescendants<TextBlock>(node).FirstOrDefault() ?? LogicalDescendants<CheckBox>(node).FirstOrDefault() ?? node;
        MarkSearch(target, start, length);
    }
    void MarkSearch(FrameworkElement element, int start, int length)
    {
        element.UpdateLayout();
        if (element is TextBox box)
        {
            var old = box.SelectionBrush; double oldOpacity = box.SelectionOpacity; bool inactive = box.IsInactiveSelectionHighlightEnabled; object oldInactive = box.Resources[SystemColors.InactiveSelectionHighlightBrushKey]; object oldText = box.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey];
            box.SelectionBrush = Brushes.Yellow; box.SelectionOpacity = 1; box.IsInactiveSelectionHighlightEnabled = true; box.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Brushes.Yellow; box.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Brushes.Black;
            box.Select(Math.Min(start, box.Text.Length), Math.Min(length, Math.Max(0, box.Text.Length - start)));
            Rect rect = box.GetRectFromCharacterIndex(Math.Min(start, box.Text.Length));
            if (!rect.IsEmpty) { box.BringIntoView(rect); CenterSearchResult(box, rect); } else box.BringIntoView();
            clearSearchMark = () => { box.Select(box.SelectionStart, 0); box.SelectionBrush = old; box.SelectionOpacity = oldOpacity; box.IsInactiveSelectionHighlightEnabled = inactive; RestoreResource(box, SystemColors.InactiveSelectionHighlightBrushKey, oldInactive); RestoreResource(box, SystemColors.InactiveSelectionHighlightTextBrushKey, oldText); };
        }
        else
        {
            element.BringIntoView();
            if (element is TextBlock text)
            {
                string original = text.Text; var oldBackground = text.Background; var oldForeground = text.Foreground; int safeStart = Math.Clamp(start, 0, original.Length), safeLength = Math.Clamp(length, 0, original.Length - safeStart);
                text.Inlines.Clear(); if (safeStart > 0) text.Inlines.Add(new Run(original[..safeStart])); text.Inlines.Add(new Run(original.Substring(safeStart, safeLength)) { Background = Brushes.Yellow, Foreground = Brushes.Black }); if (safeStart + safeLength < original.Length) text.Inlines.Add(new Run(original[(safeStart + safeLength)..]));
                CenterSearchResult(text, new Rect(0, 0, text.ActualWidth, text.ActualHeight));
                clearSearchMark = () => { text.Inlines.Clear(); text.Text = original; text.Background = oldBackground; text.Foreground = oldForeground; };
            }
            else if (element is Control control) { var old = control.Background; control.Background = Brushes.Yellow; clearSearchMark = () => { control.Background = old; if (control is ComboBoxItem) { var owner = ItemsControl.ItemsControlFromItemContainer(control) as ComboBox; if (owner is not null) owner.IsDropDownOpen = false; } }; }
        }
    }
    void MarkPreviewSearch(PreviewTextSurface surface, int start, int length)
    {
        var begin = surface.PointerAt(start); var end = surface.PointerAt(start + length); if (begin is null || end is null) return;
        var box = surface.Box; var oldBrush = box.SelectionBrush; double oldOpacity = box.SelectionOpacity; bool oldEnabled = box.IsInactiveSelectionHighlightEnabled; object oldInactive = box.Resources[SystemColors.InactiveSelectionHighlightBrushKey]; object oldText = box.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey];
        box.SelectionBrush = Brushes.Yellow; box.SelectionOpacity = 1; box.IsInactiveSelectionHighlightEnabled = true; box.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Brushes.Yellow; box.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Brushes.Black; box.Selection.Select(begin, end);
        Rect rect = begin.GetCharacterRect(LogicalDirection.Forward); if (!rect.IsEmpty) { box.BringIntoView(rect); CenterSearchResult(box, rect); } else box.BringIntoView();
        clearSearchMark = () => { box.Selection.Select(box.Document.ContentStart, box.Document.ContentStart); box.SelectionBrush = oldBrush; box.SelectionOpacity = oldOpacity; box.IsInactiveSelectionHighlightEnabled = oldEnabled; RestoreResource(box, SystemColors.InactiveSelectionHighlightBrushKey, oldInactive); RestoreResource(box, SystemColors.InactiveSelectionHighlightTextBrushKey, oldText); };
    }
    static void RestoreResource(FrameworkElement element, object key, object value)
    {
        if (value == DependencyProperty.UnsetValue || value is null) element.Resources.Remove(key); else element.Resources[key] = value;
    }
    void CenterSearchResult(FrameworkElement element, Rect rect)
    {
        Dispatcher.BeginInvoke(() =>
        {
            element.UpdateLayout();
            for (DependencyObject? parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollViewer scroll && scroll.ScrollableHeight > 0)
                {
                    Point point = element.TranslatePoint(new Point(rect.X, rect.Y), scroll); scroll.ScrollToVerticalOffset(Math.Clamp(scroll.VerticalOffset + point.Y - scroll.ViewportHeight / 2, 0, scroll.ScrollableHeight)); break;
                }
        }, DispatcherPriority.Loaded);
    }
    static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var item in LogicalTreeHelper.GetChildren(root)) if (item is DependencyObject child)
        { if (child is T match) yield return match; foreach (var nested in LogicalDescendants<T>(child)) yield return nested; }
    }
}

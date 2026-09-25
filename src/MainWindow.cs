using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace RisupEditor;

public sealed partial class MainWindow : Window
{
    readonly List<Preset> references = new();
    readonly List<HashSet<Value>> collapsedReferences = new();
    readonly List<HashSet<string>> collapsedReferenceSections = new();
    readonly List<Value> blocks = new();
    readonly List<Value> regexScripts = new();
    readonly HashSet<Value> collapsedWork = new();
    readonly HashSet<string> collapsedWorkSections = new();
    readonly Stack<Snapshot> undo = new(), redo = new();
    readonly Dictionary<Value, Border> workCards = new();
    readonly Dictionary<Value, Action> refreshHeaders = new();
    readonly Dictionary<string, string> toggleValues = new();
    readonly HashSet<string> customOtherFields = new(StringComparer.Ordinal);
    Value draftOther = Value.Map();
    readonly TabControl tabs = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    readonly StackPanel work = new(), togglePreview = new(), promptPreview = new() { Margin = new Thickness(0, 0, 6, 0) };
    readonly System.Windows.Threading.DispatcherTimer toggleDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    StackPanel? workSections;
    Preset? sectionsBasis;
    readonly ScrollViewer workScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly TextBox toggleEditor;
    readonly TextBlock status = new(), workHint = new(), toggleErrors = new();
    readonly Button copyTab, copyBlock, undoButton, redoButton, previewButton, importToggles, importRegex;
    readonly CheckBox withJoinToggle;
    readonly ColumnDefinition leftColumn, workColumn, toggleColumn;
    Preset? basis;
    string customToggleText = "", projectPath = "";
    int referenceIndex = -1, selected = -1;
    bool dirty, showingPreview, previewWithJoin, settingToggleEditor, toggleEditCaptured;

    record Snapshot(List<Value> Blocks, List<Value> Regex, Preset? Basis, Value DraftOther, string ToggleText, Dictionary<string, string> ToggleValues, HashSet<string> CustomOtherFields, HashSet<int> Collapsed, int Selected);
    record ToggleDef(string Key, string Name, string? Type, string[] Options, int Line);
    record ToggleNode(ToggleDef Def, List<ToggleDef>? Children);
    record ChoiceItem(string Value, string Label);

    static Brush Ink => new SolidColorBrush(Color.FromRgb(36, 51, 69));
    static Brush Muted => new SolidColorBrush(Color.FromRgb(105, 121, 140));
    static Brush Accent => new SolidColorBrush(Color.FromRgb(15, 118, 110));
    static Brush Line => new SolidColorBrush(Color.FromRgb(220, 228, 236));
    static TextBlock Text(string value, double size = 13, Brush? color = null) => new() { Text = value, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap };
    static Button Button(string text, Action action, bool accent = false, bool compact = false)
    {
        var button = new Button { Content = text, FontSize = compact ? 11 : 13, Margin = new Thickness(1), Padding = compact ? new Thickness(4, 5, 4, 5) : new Thickness(12, 7, 12, 7), MinHeight = compact ? 29 : 34, BorderThickness = new Thickness(1), FocusVisualStyle = null };
        if (accent) { button.Background = Accent; button.Foreground = Brushes.White; button.BorderBrush = Accent; }
        button.Click += (_, _) => { Keyboard.ClearFocus(); action(); };
        return button;
    }
    static Border Card(UIElement child) => new() { Child = child, Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 8, 12) };
    Preset? Current => tabs.SelectedIndex >= 0 && tabs.SelectedIndex < references.Count ? references[tabs.SelectedIndex] : null;

    public MainWindow()
    {
        Title = "Risup Editor"; Background = new SolidColorBrush(Color.FromRgb(243, 245, 247)); FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
        Width = 1540; Height = 900; MinWidth = 900; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Background = Background }; Content = root;
        var bottom = new Border { Padding = new Thickness(24, 10, 24, 10), Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = CreateFooter() };
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);

        var columns = new Grid { Margin = new Thickness(20, 16, 20, 16) };
        leftColumn = new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star), MinWidth = 240 };
        workColumn = new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star), MinWidth = 270 };
        toggleColumn = new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 160 };
        columns.ColumnDefinitions.Add(leftColumn); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        columns.ColumnDefinitions.Add(workColumn); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); columns.ColumnDefinitions.Add(toggleColumn);
        root.Children.Add(columns);

        var left = new DockPanel(); Grid.SetColumn(left, 0); columns.Children.Add(left);
        var leftHeader = new StackPanel(); DockPanel.SetDock(leftHeader, Dock.Top); left.Children.Add(leftHeader);
        leftHeader.Children.Add(Text("참조 프리셋", 20)); leftHeader.Children.Add(Text("읽기 전용 · 여러 파일을 탭으로 열어 비교할 수 있습니다.", 12, Muted));
        var referenceTools = new WrapPanel { Margin = new Thickness(0, 9, 0, 7) };
        referenceTools.Children.Add(Button("불러오기", OpenDialog, compact: true));
        copyTab = Button("블록 전체 복사 →", () => Copy(true), compact: true); copyBlock = Button("블록 복사 →", () => Copy(false), compact: true);
        referenceTools.Children.Add(copyTab); referenceTools.Children.Add(copyBlock); leftHeader.Children.Add(referenceTools);
        importToggles = Button("토글 복사 →", CopyCurrentToggles, compact: true); referenceTools.Children.Add(importToggles);
        importRegex = Button("정규식 복사 →", CopyCurrentRegex, compact: true); referenceTools.Children.Add(importRegex);
        referenceTools.Children.Add(Button("기타 정보 덮어쓰기 →", OverwriteOther, compact: true));
        tabs.SizeChanged += (_, _) => ResizeTabs();
        tabs.SelectionChanged += (_, e) => { if (e.Source == tabs) { if (tabs.SelectedItem is TabItem selectedTab && selectedTab.Tag is Action build) build(); if (basis is null && Current is not null) { workSections = null; RenderWork(); } referenceIndex = -1; ClearReferenceSelection(); Update(); } };
        left.Children.Add(tabs);

        var split1 = Splitter(); Grid.SetColumn(split1, 1); columns.Children.Add(split1);
        var middle = new DockPanel(); Grid.SetColumn(middle, 2); columns.Children.Add(middle);
        var middleHeader = new StackPanel { MinHeight = 150 }; DockPanel.SetDock(middleHeader, Dock.Top); middle.Children.Add(middleHeader);
        middleHeader.Children.Add(Text("작업 중", 20)); workHint.Foreground = Muted; workHint.FontSize = 12; middleHeader.Children.Add(workHint);
        var row1 = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        undoButton = Button("↶ 실행 취소", Undo, compact: true); redoButton = Button("↷ 다시 실행", Redo, compact: true);
        row1.Children.Add(undoButton); row1.Children.Add(redoButton); middleHeader.Children.Add(row1);
        var row2 = new Grid(); row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var workCommands = new WrapPanel();
        workCommands.Children.Add(Button("현재 작업 삭제", NewWork, compact: true)); workCommands.Children.Add(Button("프로젝트 불러오기", LoadProjectDialog, compact: true));
        workCommands.Children.Add(Button("저장", SaveProject, compact: true)); workCommands.Children.Add(Button("내보내기", Export, true, true)); row2.Children.Add(workCommands);
        var previewControls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), ToolTip = "연속된 같은 역할의 메시지를 합쳐 미리봅니다." };
        var withJoinLabel = Text("With Join", 11); withJoinLabel.VerticalAlignment = VerticalAlignment.Center; previewControls.Children.Add(withJoinLabel);
        withJoinToggle = new CheckBox { Width = 18, Height = 18, Margin = new Thickness(6, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = "연속된 같은 역할의 메시지를 합칩니다." };
        withJoinToggle.Click += (_, _) => { previewWithJoin = withJoinToggle.IsChecked == true; dirty = true; RefreshPromptPreview(); Update(); };
        previewControls.Children.Add(withJoinToggle);
        previewButton = Button("미리보기", TogglePromptPreview, compact: true); previewButton.BorderThickness = new Thickness(2); previewButton.BorderBrush = Line; previewControls.Children.Add(previewButton);
        Grid.SetColumn(previewControls, 1); row2.Children.Add(previewControls); middleHeader.Children.Add(row2);
        workScroll.Content = work; middle.Children.Add(workScroll);

        var split2 = Splitter(); Grid.SetColumn(split2, 3); columns.Children.Add(split2);
        var toggles = new DockPanel(); Grid.SetColumn(toggles, 4); columns.Children.Add(toggles);
        var toggleHeader = new StackPanel(); DockPanel.SetDock(toggleHeader, Dock.Top); toggles.Children.Add(toggleHeader);
        toggleHeader.Children.Add(Text("채팅 화면 토글", 20));
        var toggleGrid = new Grid(); toggleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 130 }); toggleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); toggleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 130 }); toggles.Children.Add(toggleGrid);
        var editorPanel = new DockPanel(); Grid.SetRow(editorPanel, 0); toggleGrid.Children.Add(editorPanel);
        var editorLabel = Text("토글 정의", 12, Muted); editorLabel.Margin = new Thickness(0, 0, 0, 5); DockPanel.SetDock(editorLabel, Dock.Top); editorPanel.Children.Add(editorLabel);
        toggleErrors.FontSize = 11; toggleErrors.Foreground = new SolidColorBrush(Color.FromRgb(166, 112, 20)); toggleErrors.TextWrapping = TextWrapping.Wrap; DockPanel.SetDock(toggleErrors, Dock.Bottom); editorPanel.Children.Add(toggleErrors);
        toggleEditor = new TextBox { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas, Malgun Gothic"), VerticalContentAlignment = VerticalAlignment.Top };
        toggleDelay.Tick += (_, _) => { toggleDelay.Stop(); RenderTogglePreview(); RefreshPromptPreview(); };
        toggleEditor.TextChanged += (_, _) => { if (settingToggleEditor) return; if (!toggleEditCaptured) { Remember(); toggleEditCaptured = true; } customToggleText = NormalizeToggleNewlines(toggleEditor.Text); dirty = true; toggleDelay.Stop(); toggleDelay.Start(); Update(); };
        toggleEditor.LostKeyboardFocus += (_, _) => toggleEditCaptured = false; editorPanel.Children.Add(toggleEditor);
        var horizontal = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, Background = Line, ResizeDirection = GridResizeDirection.Rows, ShowsPreview = true }; Grid.SetRow(horizontal, 1); toggleGrid.Children.Add(horizontal);
        var previewPanel = new DockPanel(); Grid.SetRow(previewPanel, 2); toggleGrid.Children.Add(previewPanel);
        previewPanel.Margin = new Thickness(0, 12, 0, 0);
        previewPanel.Children.Add(new ScrollViewer { Content = togglePreview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        editorPanel.Children.Remove(toggleEditor); editorPanel.Children.Remove(toggleErrors);
        toggleGrid.Children.Remove(previewPanel); toggles.Children.Remove(toggleGrid); toggles.Children.Add(previewPanel);

        AttachSearch(left, "참조 프리셋"); AttachSearch(middle, "작업 중"); AttachSearch(toggles, "채팅 화면 토글");
        Closing += (_, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { OpenSearch(); e.Handled = true; }
            else if (e.Key == Key.Escape && searchPanes[activeSearchPane].Bar.Visibility == Visibility.Visible) { CloseSearch(activeSearchPane); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) { OpenDialog(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { SaveProject(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z) { Undo(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y) { Redo(); e.Handled = true; }
        };
        RenderReferences(); RenderWork(); RenderTogglePreview(); Update();
    }

    static GridSplitter Splitter() => new() { Width = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Background = Line, ResizeDirection = GridResizeDirection.Columns, ShowsPreview = true };
    void Update(string? message = null)
    {
        if (!searchNavigating) SearchDataChanged();
        if (selected >= 0 && selected < blocks.Count && refreshHeaders.TryGetValue(blocks[selected], out var refresh)) refresh();
        Title = $"{(dirty ? "● " : "")}Risup Editor"; workHint.Text = $"{blocks.Count}개 블록 · {(showingPreview ? "미리보기" : dirty ? "저장하지 않은 변경 사항이 있습니다" : "변경 사항 없음")}";
        copyTab.IsEnabled = Current?.Blocks is { Count: > 0 }; copyBlock.IsEnabled = Current?.Blocks is { } b && referenceIndex >= 0 && referenceIndex < b.Count; importToggles.IsEnabled = Current is not null; importRegex.IsEnabled = Current is not null;
        previewButton.BorderBrush = showingPreview ? Accent : Line;
        undoButton.IsEnabled = undo.Count > 0; redoButton.IsEnabled = redo.Count > 0;
        if (message is not null) status.Text = message; else if (string.IsNullOrEmpty(status.Text)) status.Text = ".risup 파일을 불러와 시작하세요.  ·  Ctrl+O 불러오기  /  Ctrl+S 프로젝트 저장";
    }

    Snapshot Capture() => new(blocks.Select(v => v.Clone()).ToList(), regexScripts.Select(v => v.Clone()).ToList(), basis?.Clone(), draftOther.Clone(), customToggleText, new(toggleValues), new(customOtherFields, StringComparer.Ordinal), collapsedWork.Select(v => blocks.IndexOf(v)).Where(i => i >= 0).ToHashSet(), selected);
    void Remember() { undo.Push(Capture()); redo.Clear(); }
    void Restore(Snapshot state)
    {
        blocks.Clear(); blocks.AddRange(state.Blocks.Select(v => v.Clone())); regexScripts.Clear(); regexScripts.AddRange(state.Regex.Select(v => v.Clone())); basis = state.Basis?.Clone(); draftOther = state.DraftOther.Clone(); selected = state.Selected;
        customOtherFields.Clear(); foreach (string key in state.CustomOtherFields) customOtherFields.Add(key);
        collapsedWork.Clear(); foreach (int i in state.Collapsed) if (i >= 0 && i < blocks.Count) collapsedWork.Add(blocks[i]);
        customToggleText = state.ToggleText; toggleValues.Clear(); foreach (var pair in state.ToggleValues) toggleValues[pair.Key] = pair.Value;
        workSections = null; sectionsBasis = null; SetToggleEditor(); RenderWork(); RenderTogglePreview(); RefreshPromptPreview(); dirty = true; Update();
    }
    void Undo() { if (undo.Count == 0) return; redo.Push(Capture()); Restore(undo.Pop()); Update("변경을 취소했습니다."); }
    void Redo() { if (redo.Count == 0) return; undo.Push(Capture()); Restore(redo.Pop()); Update("변경을 다시 적용했습니다."); }
    bool ConfirmDiscard() => !dirty || MessageBox.Show(this, "저장하지 않은 프로젝트 변경 사항이 있습니다. 버리고 계속할까요?", "변경 사항 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    void NewWork()
    {
        if (!ConfirmDiscard()) return;
        Remember(); blocks.Clear(); regexScripts.Clear(); collapsedWork.Clear(); collapsedWorkSections.Clear(); toggleValues.Clear(); customOtherFields.Clear(); customToggleText = ""; basis = null; draftOther = Value.Map(); workSections = null; sectionsBasis = null; selected = referenceIndex = -1; projectPath = ""; showingPreview = previewWithJoin = false; withJoinToggle.IsChecked = false; dirty = true; workScroll.Content = work;
        SetToggleEditor(); RenderReferences(); RenderWork(); RenderTogglePreview(); Update("현재 작업을 비웠습니다.");
    }
    void OpenDialog() { var d = new OpenFileDialog { Filter = "RisuAI 프리셋|*.risup;*.risupreset", Multiselect = true }; if (d.ShowDialog(this) == true) OpenPaths(d.FileNames); }
    public void OpenPaths(IEnumerable<string> paths)
    {
        var errors = new List<string>(); int added = 0;
        foreach (string path in paths) try { references.Add(RisupCodec.Load(path)); collapsedReferences.Add(new()); collapsedReferenceSections.Add(new()); added++; } catch (Exception ex) { errors.Add(Path.GetFileName(path) + ": " + ex.Message); }
        if (added > 0) { dirty = true; RenderReferences(); tabs.SelectedIndex = references.Count - 1; Update($"참조 프리셋 {added}개를 불러왔습니다."); }
        if (errors.Count > 0) MessageBox.Show(this, string.Join("\n\n", errors), "불러오기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    void RenderReferences()
    {
        int old = tabs.SelectedIndex; tabs.Items.Clear();
        for (int pIndex = 0; pIndex < references.Count; pIndex++)
        {
            int presetIndex = pIndex; var preset = references[pIndex]; var collapsed = collapsedReferences[pIndex]; var collapsedSections = collapsedReferenceSections[pIndex];
            var tab = new TabItem { HorizontalContentAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
            var head = new Grid(); head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tabTitle = Text(preset.Name, 12); tabTitle.TextWrapping = TextWrapping.NoWrap; tabTitle.TextTrimming = TextTrimming.CharacterEllipsis; tabTitle.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(tabTitle);
            var close = Button("×", () => { references.RemoveAt(presetIndex); collapsedReferences.RemoveAt(presetIndex); collapsedReferenceSections.RemoveAt(presetIndex); dirty = true; RenderReferences(); Update("참조 탭을 닫았습니다."); }, compact: true); close.Padding = new Thickness(2, 0, 2, 0); close.Margin = new Thickness(2, 0, 0, 0); close.ToolTip = "참조 탭 닫기"; Grid.SetColumn(close, 1); head.Children.Add(close); tab.Header = head; tab.ToolTip = preset.Path;
            void BuildReference()
            {
            if (tab.Content is not null) return;
            var body = new StackPanel { Margin = new Thickness(0, 12, 6, 0) };
            var blockSection = AddSection(body, "blocks", "프롬프트 블록", collapsedSections, true); var blockBody = blockSection.Body;
            if (preset.Blocks is null) blockBody.Children.Add(Card(Text("이 프리셋에는 블록형 promptTemplate이 없습니다.", 14, Muted)));
            else for (int i = 0; i < preset.Blocks.Count; i++)
            {
                int index = i; var value = preset.Blocks[i]; var card = BuildBlock(value, i, false, collapsed);
                card.PreviewMouseLeftButtonDown += (_, e) => { referenceIndex = index; foreach (var child in LogicalDescendants<Border>(body).Where(b => b.Tag is BlockView)) child.BorderBrush = Line; card.BorderBrush = Accent; Update($"참조 블록 {index + 1} 선택: {Label(value)}"); };
                blockBody.Children.Add(card);
            }
            AppendSections(body, preset, false, collapsedSections);
            tab.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            }
            tab.Tag = (Action)BuildReference; tabs.Items.Add(tab);
        }
        tabs.SelectedIndex = references.Count == 0 ? -1 : Math.Clamp(old, 0, references.Count - 1); referenceIndex = -1; Dispatcher.BeginInvoke(ResizeTabs); Update();
    }
    void ClearReferenceSelection() { foreach (TabItem tab in tabs.Items) if (tab.Content is ScrollViewer v && v.Content is StackPanel p) foreach (var c in LogicalDescendants<Border>(p).Where(b => b.Tag is BlockView)) c.BorderBrush = Line; }
    void ResizeTabs()
    {
        if (references.Count == 0 || tabs.ActualWidth <= 0) return;
        double width = Math.Max(1, Math.Floor((tabs.ActualWidth - 20) / references.Count) - 8);
        foreach (var tab in tabs.Items.OfType<TabItem>()) { tab.Width = width; tab.Padding = new Thickness(references.Count > 6 ? 4 : 12, 8, references.Count > 6 ? 4 : 12, 8); }
    }
    void Copy(bool all)
    {
        var preset = Current; if (preset?.Blocks is null) return;
        var incoming = all ? preset.Blocks : referenceIndex >= 0 && referenceIndex < preset.Blocks.Count ? [preset.Blocks[referenceIndex]] : [];
        if (incoming.Count == 0) return; Remember(); EnsureBasis(preset); int at = selected >= 0 ? selected + 1 : blocks.Count; blocks.InsertRange(at, incoming.Select(v => v.Clone())); selected = at; dirty = true; RenderWork(); FocusSelected(); Update($"{incoming.Count}개 블록을 작업 중에 복사했습니다.");
    }

    static readonly Dictionary<string, string> Types = new() { ["plain"] = "순수 프롬프트", ["jailbreak"] = "탈옥 프롬프트", ["chat"] = "챗", ["persona"] = "페르소나 프롬프트", ["description"] = "캐릭터 설명", ["authornote"] = "작가의 노트", ["lorebook"] = "로어북", ["memory"] = "장기 기억", ["postEverything"] = "최종 삽입 프롬프트", ["chatML"] = "ChatML", ["cache"] = "캐시 포인트", ["cot"] = "생각의 사슬" };
    static string TypeName(Value b) => Types.GetValueOrDefault(b.Str("type"), b.Str("type"));
    static string Label(Value b) => string.IsNullOrEmpty(b.Str("name")) ? TypeName(b) : b.Str("name");
    static bool Plain(Value b) => b.Str("type") is "plain" or "jailbreak" or "cot" or "chatML";
    static bool Inner(Value b) => b.Str("type") is "description" or "persona" or "authornote" or "memory";
    void Select(int i) { if (selected >= 0 && selected < blocks.Count && workCards.TryGetValue(blocks[selected], out var previous)) previous.BorderBrush = Line; selected = i; if (i >= 0 && i < blocks.Count && workCards.TryGetValue(blocks[i], out var current)) current.BorderBrush = Accent; }
    void RenderWork()
    {
        work.Children.Clear(); workCards.Clear(); refreshHeaders.Clear();
        var blockSection = AddSection(work, "blocks", "프롬프트 블록", collapsedWorkSections, true); var blockBody = blockSection.Body;
        if (blocks.Count == 0) { blockBody.Children.Add(Card(Text("아직 블록이 없습니다.", 14, Muted))); }
        else for (int i = 0; i < blocks.Count; i++) { var block = blocks[i]; var card = BuildBlock(block, i, true, collapsedWork); workCards[block] = card; blockBody.Children.Add(card); }
        blockBody.Children.Add(Button("+ 블록", AddBlock, compact: true));
        if (workSections is null || !ReferenceEquals(sectionsBasis, basis)) { workSections = new StackPanel(); AppendSections(workSections, basis, true, collapsedWorkSections); sectionsBasis = basis; }
        work.Children.Add(workSections); Select(selected); RefreshPromptPreview();
    }
    void FocusSelected() => Dispatcher.BeginInvoke(() => { if (selected >= 0 && selected < blocks.Count && workCards.TryGetValue(blocks[selected], out var card)) card.BringIntoView(); });
    Border BuildBlock(Value block, int index, bool editable, HashSet<Value> collapsed)
    {
        var stack = new StackPanel(); var card = Card(stack); var top = new DockPanel(); stack.Children.Add(top);
        if (editable)
        {
            card.PreviewMouseDown += (_, _) => Select(blocks.IndexOf(block)); card.GotKeyboardFocus += (_, _) => Select(blocks.IndexOf(block));
            var commands = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(commands, Dock.Right); top.Children.Add(commands);
            foreach (var cmd in new[] { ("↑", (Action)(() => Move(block, -1)), "위로 이동"), ("↓", (Action)(() => Move(block, 1)), "아래로 이동"), ("⧉", (Action)(() => Duplicate(block)), "블록 복제"), ("×", (Action)(() => Delete(block)), "블록 삭제") }) { var button = Button(cmd.Item1, cmd.Item2, compact: true); button.Padding = new Thickness(5, 2, 5, 2); button.ToolTip = cmd.Item3; commands.Children.Add(button); }
            var handle = Text("⠿", 20, Muted); handle.Cursor = Cursors.SizeAll; handle.ToolTip = "드래그하여 이동"; handle.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(handle, Dock.Left); top.Children.Add(handle);
            Point? start = null; handle.MouseLeftButtonDown += (_, e) => start = e.GetPosition(handle); handle.MouseMove += (_, e) => { if (start is { } p && e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(handle) - p).Length > 5) { start = null; DragDrop.DoDragDrop(handle, new DataObject("RisupEditor.Block", block), DragDropEffects.Move); } };
            card.AllowDrop = true; card.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent("RisupEditor.Block") ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            card.Drop += (_, e) => { if (e.Data.GetData("RisupEditor.Block") is Value source) MoveDrop(source, block, e.GetPosition(card).Y > card.ActualHeight / 2); e.Handled = true; };
        }
        var title = Text($"{index + 1:00}   {Label(block)}", 15); title.FontWeight = FontWeights.SemiBold; top.Children.Add(title);
        var details = new StackPanel { Visibility = collapsed.Contains(block) ? Visibility.Collapsed : Visibility.Visible };
        var subtitle = new TextBlock { Text = Subtitle(block), Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 5, 0, 10) }; details.Children.Add(subtitle);
        if (editable) refreshHeaders[block] = () => { title.Text = $"{blocks.IndexOf(block) + 1:00}   {Label(block)}"; subtitle.Text = Subtitle(block); };
        AddField(details, "이름", block, "name", editable);
        AddTypeChoice(details, block, editable);
        string blockType = block.Str("type");
        if (blockType is "plain" or "jailbreak" or "cot")
        {
            AddMappedChoice(details, "특수 타입", block, "type2", [new("normal", "없음"), new("main", "메인 프롬프트"), new("globalNote", "글로벌 노트")], editable);
            AddField(details, "본문", block, "text", editable, true);
            AddMappedChoice(details, "역할", block, "role", PromptRoles(), editable);
        }
        else if (blockType == "chatML") AddField(details, "본문", block, "text", editable, true);
        else if (Inner(block))
        {
            if (blockType == "authornote") AddField(details, "기본 작가의 노트", block, "defaultText", editable, true);
            AddField(details, "본문 · 첫 {{slot}} 자리에 해당 내용을 삽입", block, "innerFormat", editable, true);
            AddMappedChoice(details, "역할", block, "role2", PromptRoles(), editable);
        }
        else if (blockType == "chat")
        {
            AddNumber(details, "시작 범위 · -1000은 전체 기록", block, "rangeStart", editable); AddNumber(details, "끝 범위 · end는 마지막까지", block, "rangeEnd", editable, true); AddBool(details, "시스템 채팅에서 원래 역할 유지", block, "chatAsOriginalOnSystem", editable);
        }
        else if (blockType == "cache")
        {
            AddNumber(details, "깊이", block, "depth", editable); AddMappedChoice(details, "역할", block, "role", [new("all", "전체"), new("user", "사용자"), new("assistant", "캐릭터"), new("system", "시스템")], editable);
        }
        else details.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(239, 246, 246)), Padding = new Thickness(14), CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 8, 0, 0), Child = Text(Placeholder(block), 13, Accent) });
        var settings = new StackPanel(); var settingsExpander = new Expander { Header = "원본 추가 필드", Content = settings, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed }; details.Children.Add(settingsExpander); stack.Children.Add(details);
        card.Tag = new BlockView(block, details, settings, settingsExpander, collapsed);
        card.PreviewMouseLeftButtonDown += (_, e) => { if (IsInteractive(e.OriginalSource as DependencyObject, card) || LogicalDescendants<ComboBox>(card).Any(combo => combo.IsDropDownOpen)) return; if (collapsed.Remove(block)) details.Visibility = Visibility.Visible; else { collapsed.Add(block); details.Visibility = Visibility.Collapsed; } dirty = editable || dirty; Update(); };
        var badge = Text("", 11, Muted); badge.Margin = new Thickness(6, 0, 0, 0); top.Children.Add(badge);
        var editors = Descendants<SyntaxBox>(stack).Where(t => t.EnableDiagnostics).ToArray();
        void RefreshBadge() { int count = editors.Sum(t => t.Diagnostics.Count); badge.Text = count == 0 ? "" : $"진단 {count}"; }
        foreach (var editor in editors) editor.DiagnosticsChanged += (_, _) => RefreshBadge();
        RefreshBadge();
        return card;
    }
    static string Subtitle(Value b) => TypeName(b) + (b.Str("role", b.Str("role2")) is { Length: > 0 } role ? "  ·  " + role : "");
    static string Placeholder(Value b) => b.Str("type") switch { "chat" => $"이 자리에 채팅 기록이 들어갑니다.\n범위: {Display(b.Get("rangeStart"))} → {Display(b.Get("rangeEnd"))}", "cache" => "이 위치에 캐시 지점이 설정됩니다.", "postEverything" => "이 자리에 마지막 삽입 영역의 내용이 들어갑니다.", _ => Types.ContainsKey(b.Str("type")) ? $"이 자리에 {TypeName(b)} 내용이 들어갑니다." : "알 수 없는 블록 유형입니다. 원본 설정을 보존합니다." };
    static bool IsInteractive(DependencyObject? source, DependencyObject card) { for (var p = source; p is not null && p != card; p = VisualTreeHelper.GetParent(p)) if (p is TextBox or ButtonBase or ComboBox or ComboBoxItem or Expander or ScrollBar or Thumb) return true; return false; }
    static string Display(Value? v) => v?.Text() ?? v?.Number()?.ToString() ?? "미지정";
    TextBox Field(Value b, string key, bool edit, bool multi)
    {
        var t = new SyntaxBox { Tag = new FieldAddress(b, key), EnableDiagnostics = multi, Text = b.Str(key), IsReadOnly = !edit, AcceptsReturn = multi, AcceptsTab = multi, TextWrapping = TextWrapping.Wrap, MinHeight = multi ? 92 : 32, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, FontFamily = multi ? new FontFamily("Consolas, Malgun Gothic") : FontFamily, FontSize = 13, VerticalContentAlignment = multi ? VerticalAlignment.Top : VerticalAlignment.Center, IsUndoEnabled = false };
        bool captured = false; t.LostKeyboardFocus += (_, _) => captured = false;
        if (edit) t.TextChanged += (_, _) => { if (!captured) { Remember(); captured = true; } b.Set(key, Value.String(t.Text)); dirty = true; RefreshPromptPreview(); Update(); }; return t;
    }
    void AddField(Panel p, string label, Value b, string key, bool edit, bool multi = false) { p.Children.Add(new TextBlock { Text = label, Foreground = Muted, Margin = new Thickness(0, 7, 0, 4), FontSize = 11 }); p.Children.Add(Field(b, key, edit, multi)); }
    static ChoiceItem[] PromptRoles() => [new("user", "사용자"), new("bot", "캐릭터"), new("system", "시스템")];
    void AddTypeChoice(Panel p, Value b, bool edit)
    {
        p.Children.Add(new TextBlock { Text = "타입", Foreground = Muted, Margin = new Thickness(0, 7, 0, 4), FontSize = 11 });
        string current = b.Str("type"); var options = Types.Select(pair => new ChoiceItem(pair.Key, pair.Value)).ToList();
        if (!Types.ContainsKey(current)) options.Insert(0, new(current, current));
        var c = new ComboBox { Tag = new FieldAddress(b, "type"), ItemsSource = options, DisplayMemberPath = nameof(ChoiceItem.Label), SelectedValuePath = nameof(ChoiceItem.Value), SelectedValue = current, IsEnabled = edit };
        c.SelectionChanged += (_, _) =>
        {
            if (!edit || c.SelectedValue is not string value || value == b.Str("type")) return;
            Remember(); b.Set("type", Value.String(value)); InitializeBlockType(b, value, false); dirty = true;
            selected = blocks.IndexOf(b); RenderWork(); FocusSelected(); Update($"블록 타입을 {Types.GetValueOrDefault(value, value)}(으)로 변경했습니다.");
        };
        p.Children.Add(c);
    }
    void AddMappedChoice(Panel p, string label, Value b, string key, IEnumerable<ChoiceItem> source, bool edit)
    {
        p.Children.Add(new TextBlock { Text = label, Foreground = Muted, Margin = new Thickness(0, 7, 0, 4), FontSize = 11 });
        string current = b.Str(key); var options = source.ToList(); if (!options.Any(item => item.Value == current)) options.Insert(0, new(current, current));
        var c = new ComboBox { Tag = new FieldAddress(b, key), ItemsSource = options, DisplayMemberPath = nameof(ChoiceItem.Label), SelectedValuePath = nameof(ChoiceItem.Value), SelectedValue = current, IsEnabled = edit };
        c.SelectionChanged += (_, _) => { if (c.SelectedValue is string value && value != b.Str(key)) { Remember(); b.Set(key, Value.String(value)); dirty = true; RefreshPromptPreview(); Update(); } }; p.Children.Add(c);
    }
    void AddChoice(Panel p, string label, Value b, string key, string[] values, bool edit) { p.Children.Add(Text(label, 11, Muted)); string current = b.Str(key); var options = values.ToList(); if (!options.Contains(current)) options.Insert(0, current); var c = new ComboBox { Tag = new FieldAddress(b, key), ItemsSource = options, SelectedItem = current, IsEnabled = edit }; c.SelectionChanged += (_, _) => { if (c.SelectedItem is string value && value != b.Str(key)) { Remember(); b.Set(key, Value.String(value)); dirty = true; RefreshPromptPreview(); Update(); } }; p.Children.Add(c); }
    void AddNumber(Panel p, string label, Value b, string key, bool edit, bool allowEnd = false) { p.Children.Add(Text(label, 11, Muted)); var t = new TextBox { Tag = new FieldAddress(b, key), Text = Display(b.Get(key)), IsReadOnly = !edit, Margin = new Thickness(0, 3, 0, 6) }; void Commit() { if (!edit || t.Text == Display(b.Get(key))) return; if (allowEnd && t.Text == "end") { Remember(); b.Set(key, Value.String("end")); } else if (long.TryParse(t.Text, out long n) && n is >= -1000000 and <= 1000000) { Remember(); b.Set(key, Value.Int(n)); } else { t.Text = Display(b.Get(key)); return; } dirty = true; RefreshPromptPreview(); Update(); } t.LostKeyboardFocus += (_, _) => Commit(); t.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); }; p.Children.Add(t); }
    void AddBool(Panel p, string label, Value b, string key, bool edit) { var c = new CheckBox { Tag = new FieldAddress(b, key), Content = label, IsChecked = b.Get(key)?.Boolean() ?? false, IsEnabled = edit, Margin = new Thickness(0, 6, 0, 6) }; c.Click += (_, _) => { Remember(); b.Set(key, Value.Bool(c.IsChecked == true)); dirty = true; RefreshPromptPreview(); Update(); }; p.Children.Add(c); }
    void Move(Value b, int offset) { int from = blocks.IndexOf(b), to = from + offset; if (from < 0 || to < 0 || to >= blocks.Count) return; Remember(); blocks.RemoveAt(from); blocks.Insert(to, b); selected = to; dirty = true; RenderWork(); FocusSelected(); Update("블록을 이동했습니다."); }
    void MoveDrop(Value source, Value target, bool after) { int from = blocks.IndexOf(source), to = blocks.IndexOf(target); if (from < 0 || to < 0 || from == to) return; Remember(); blocks.RemoveAt(from); to = blocks.IndexOf(target) + (after ? 1 : 0); blocks.Insert(to, source); selected = to; dirty = true; RenderWork(); FocusSelected(); Update("블록 순서를 변경했습니다."); }
    void Duplicate(Value b) { int i = blocks.IndexOf(b); Remember(); blocks.Insert(i + 1, b.Clone()); selected = i + 1; dirty = true; RenderWork(); FocusSelected(); Update("블록을 복제했습니다."); }
    void Delete(Value b) { Remember(); int i = blocks.IndexOf(b); collapsedWork.Remove(b); blocks.Remove(b); selected = Math.Min(i, blocks.Count - 1); dirty = true; RenderWork(); Update("블록을 삭제했습니다."); }
    void AddBlock()
    {
        const string type = "plain"; var block = Value.Map(); block.Set("type", Value.String(type)); InitializeBlockType(block, type, true);
        Remember(); int at = selected >= 0 && selected < blocks.Count ? selected + 1 : blocks.Count; blocks.Insert(at, block); selected = at; dirty = true; RenderWork(); FocusSelected(); Update("순수 프롬프트 블록을 추가했습니다.");
    }

    static void InitializeBlockType(Value block, string type, bool isNew)
    {
        if (type is "plain" or "jailbreak" or "cot")
        {
            block.Set("text", Value.String("")); block.Set("role", Value.String("system")); if (isNew || string.IsNullOrEmpty(block.Str("type2"))) block.Set("type2", Value.String("normal"));
        }
        else if (type == "chatML" && isNew) block.Set("text", Value.String(""));
        if (type is "description" or "persona" or "authornote" or "memory")
        {
            if (block.Str("role2") is not ("system" or "user" or "bot" or "assistant")) block.Set("role2", Value.String("system"));
            if (isNew) block.Set("innerFormat", Value.String("{{slot}}"));
        }
        if (type == "authornote" && isNew) block.Set("defaultText", Value.String(""));
        if (type == "chat") { block.Set("rangeStart", Value.Int(-1000)); block.Set("rangeEnd", Value.String("end")); }
        if (type == "cache") { block.Set("depth", Value.Int(1)); block.Set("role", Value.String("all")); if (isNew) block.Set("name", Value.String("캐시 지점")); }
    }

    void CopyCurrentToggles()
    {
        if (Current is null) return; string incoming = NormalizeToggleNewlines(Current.Data.Str("customPromptTemplateToggle")); if (string.IsNullOrWhiteSpace(incoming)) { Update("현재 참조에는 복사할 토글이 없습니다."); return; }
        Remember(); EnsureBasis(Current); customToggleText = string.IsNullOrWhiteSpace(customToggleText) ? incoming : customToggleText.TrimEnd() + "\n" + incoming.TrimStart(); SetToggleEditor(); RenderTogglePreview(); RefreshPromptPreview(); dirty = true; Update("현재 참조의 토글을 작업 중에 추가했습니다.");
    }
    static string NormalizeToggleNewlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    void SetToggleEditor() { toggleDelay.Stop(); settingToggleEditor = true; toggleEditor.Text = customToggleText; settingToggleEditor = false; toggleEditCaptured = false; }
    List<ToggleDef> ParseToggles(out List<string> notices)
    {
        notices = new(); var result = new List<ToggleDef>(); string[] lines = NormalizeToggleNewlines(customToggleText).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue; string[] parts = lines[i].Split('='); string key = parts.ElementAtOrDefault(0) ?? "", name = parts.ElementAtOrDefault(1) ?? "", type = parts.ElementAtOrDefault(2) ?? "", option = parts.ElementAtOrDefault(3) ?? "";
            string[] options = parts.Length > 3 ? option.Split(',') : [];
            if (type is "group" or "groupEnd" or "divider") result.Add(new(key, name, type, [], i + 1));
            else if (type == "caption" && name.Length > 0) result.Add(new(key, name, type, [], i + 1));
            else if (key.Length > 0 && name.Length > 0) result.Add(new(key, name, type is "select" or "text" or "textarea" ? type : null, options, i + 1));
            else notices.Add($"{i + 1}행: RisuAI에서 표시되지 않는 줄입니다.");
        }
        return result;
    }
    static List<ToggleNode> GroupToggles(IEnumerable<ToggleDef> definitions)
    {
        var result = new List<ToggleNode>(); ToggleNode? open = null;
        foreach (var def in definitions)
        {
            if (def.Type == "group") { open = new(def, new()); result.Add(open); }
            else if (def.Type == "groupEnd") open = null;
            else if (open is not null) open.Children!.Add(def);
            else result.Add(new(def, null));
        }
        return result;
    }
    IEnumerable<ToggleDef> VisibleToggles()
    {
        ToggleDef? previousTop = null;
        foreach (var node in GroupToggles(ParseToggles(out _)))
        {
            if (node.Def.Type == "group")
            {
                if (node.Children is { Count: > 0 })
                {
                    yield return node.Def;
                    ToggleDef? previous = null;
                    foreach (var child in node.Children)
                    {
                        if (child.Type == "divider" && previous?.Type == "divider" && previous.Name == child.Name) { previous = child; continue; }
                        yield return child; previous = child;
                    }
                }
                else yield return node.Def with { Type = null };
                previousTop = node.Def;
                continue;
            }
            if (node.Def.Type == "divider" && previousTop?.Type == "divider" && previousTop.Name == node.Def.Name) { previousTop = node.Def; continue; }
            yield return node.Def; previousTop = node.Def;
        }
    }
    void RenderTogglePreview()
    {
        togglePreview.Children.Clear(); var definitions = ParseToggles(out var notices); toggleErrors.Text = string.Join("  ", notices);
        ToggleDef? RenderItems(Panel target, IReadOnlyList<ToggleDef> items, ToggleDef? previous = null)
        {
            foreach (var def in items)
            {
                if (def.Type == "divider" && previous?.Type == "divider" && previous.Name == def.Name) { previous = def; continue; }
                if (def.Type == "caption") { var caption = Text(def.Name, 11, Muted); caption.Tag = new ToggleAddress(def.Line); target.Children.Add(caption); previous = def; continue; }
                if (def.Type == "divider") { var row = new StackPanel { Tag = new ToggleAddress(def.Line), Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 3) }; if (def.Name.Length > 0) row.Children.Add(Text(def.Name, 11, Muted)); row.Children.Add(new Separator { Width = 80, Margin = new Thickness(6, 0, 0, 0) }); target.Children.Add(row); previous = def; continue; }
                var line = new StackPanel { Tag = new ToggleAddress(def.Line), Margin = new Thickness(0, 4, 4, 4) }; line.Children.Add(Text(def.Name, 12));
                string currentValue = toggleValues.GetValueOrDefault(def.Key, "");
                if (def.Type == "select") { var c = new ComboBox { ItemsSource = def.Options, SelectedIndex = int.TryParse(currentValue, out int n) && n >= 0 && n < def.Options.Length ? n : -1 }; c.SelectionChanged += (_, _) => { toggleValues[def.Key] = c.SelectedIndex < 0 ? "" : c.SelectedIndex.ToString(); ToggleValueChanged(); }; line.Children.Add(c); }
                else if (def.Type is "text" or "textarea") { var t = new TextBox { Text = currentValue, AcceptsReturn = def.Type == "textarea", TextWrapping = TextWrapping.Wrap, MinHeight = def.Type == "textarea" ? 70 : 30, VerticalContentAlignment = VerticalAlignment.Top }; t.TextChanged += (_, _) => { toggleValues[def.Key] = t.Text; ToggleValueChanged(); }; line.Children.Add(t); }
                else { var c = new CheckBox { Content = def.Name, IsChecked = currentValue == "1", Margin = new Thickness(0, 4, 0, 4) }; line.Children.Clear(); line.Children.Add(c); c.Click += (_, _) => { toggleValues[def.Key] = c.IsChecked == true ? "1" : "0"; ToggleValueChanged(); }; }
                target.Children.Add(line); previous = def;
            }
            return previous;
        }
        ToggleDef? previousTop = null;
        foreach (var node in GroupToggles(definitions))
        {
            if (node.Def.Type == "group")
            {
                if (node.Children is { Count: > 0 })
                {
                    var content = new StackPanel(); RenderItems(content, node.Children);
                    togglePreview.Children.Add(new Expander { Tag = new ToggleAddress(node.Def.Line), Header = node.Def.Name, Content = content, IsExpanded = true, Margin = new Thickness(0, 3, 0, 3) });
                }
                else RenderItems(togglePreview, [node.Def with { Type = null }], previousTop);
                previousTop = node.Def;
            }
            else previousTop = RenderItems(togglePreview, [node.Def], previousTop);
        }
    }
    void ToggleValueChanged() { dirty = true; RefreshPromptPreview(); Update(); }

    void TogglePromptPreview() { showingPreview = !showingPreview; workScroll.Content = showingPreview ? promptPreview : work; RefreshPromptPreview(); dirty = true; Update(); }
    void RefreshPromptPreview() { if (showingPreview) RenderPromptPreview(PromptPreviewEngine.Build(blocks, basis, toggleValues, previewWithJoin)); }

    EditorProject CaptureProject()
    {
        var p = new EditorProject { Basis = basis?.Clone(), DraftOther = draftOther.Clone(), ToggleText = NormalizeToggleNewlines(customToggleText), SelectedTab = tabs.SelectedIndex, SelectedBlock = selected, Preview = showingPreview, PreviewWithJoin = previewWithJoin, AdditionalChecks = SyntaxBox.AdditionalChecks, Ratios = [leftColumn.ActualWidth, workColumn.ActualWidth, toggleColumn.ActualWidth] };
        p.References.AddRange(references.Select(v => v.Clone())); p.Blocks.AddRange(blocks.Select(v => v.Clone())); p.Regex.AddRange(regexScripts.Select(v => v.Clone())); foreach (var pair in toggleValues) p.ToggleValues[pair.Key] = pair.Value; foreach (var b in collapsedWork) { int i = blocks.IndexOf(b); if (i >= 0) p.CollapsedWork.Add(i); }
        foreach (string key in customOtherFields) p.CustomOtherFields.Add(key);
        foreach (string key in collapsedWorkSections) p.CollapsedWorkSections.Add(key);
        for (int n = 0; n < references.Count; n++)
        {
            var indexes = new HashSet<int>();
            foreach (var block in collapsedReferences[n]) { int i = references[n].Blocks?.IndexOf(block) ?? -1; if (i >= 0) indexes.Add(i); }
            p.CollapsedReferences.Add(indexes);
            p.CollapsedReferenceSections.Add(new HashSet<string>(collapsedReferenceSections[n]));
        }
        return p;
    }
    void SaveProject()
    {
        if (string.IsNullOrEmpty(projectPath)) { var d = new SaveFileDialog { Filter = "Risup Editor 프로젝트|*.risupproj", DefaultExt = ".risupproj", AddExtension = true, FileName = "프롬프트 작업.risupproj", OverwritePrompt = true }; if (d.ShowDialog(this) != true) return; projectPath = d.FileName; }
        try { ProjectFile.Save(CaptureProject(), projectPath); dirty = false; Update("프로젝트 저장 완료: " + projectPath); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "프로젝트 저장 실패", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    void LoadProjectDialog() { var d = new OpenFileDialog { Filter = "Risup Editor 프로젝트|*.risupproj" }; if (d.ShowDialog(this) == true && ConfirmDiscard()) LoadProject(d.FileName); }
    void LoadProject(string path)
    {
        try
        {
            var p = ProjectFile.Load(path); references.Clear(); references.AddRange(p.References); collapsedReferences.Clear(); collapsedReferenceSections.Clear();
            for (int n = 0; n < references.Count; n++) { var set = new HashSet<Value>(); foreach (int i in p.CollapsedReferences.ElementAtOrDefault(n) ?? []) if (i >= 0 && i < (references[n].Blocks?.Count ?? 0)) set.Add(references[n].Blocks![i]); collapsedReferences.Add(set); collapsedReferenceSections.Add(new HashSet<string>(p.CollapsedReferenceSections.ElementAtOrDefault(n) ?? [])); }
            blocks.Clear(); blocks.AddRange(p.Blocks); collapsedWork.Clear(); foreach (int i in p.CollapsedWork) if (i >= 0 && i < blocks.Count) collapsedWork.Add(blocks[i]); collapsedWorkSections.Clear(); foreach (string key in p.CollapsedWorkSections) collapsedWorkSections.Add(key);
            basis = p.Basis; draftOther = p.DraftOther.Clone(); workSections = null; sectionsBasis = null; regexScripts.Clear(); regexScripts.AddRange(p.Regex.Select(v => v.Clone())); customToggleText = NormalizeToggleNewlines(p.ToggleText); toggleValues.Clear(); foreach (var pair in p.ToggleValues) toggleValues[pair.Key] = pair.Value; customOtherFields.Clear(); foreach (string key in p.CustomOtherFields) customOtherFields.Add(key); selected = p.SelectedBlock; showingPreview = p.Preview; previewWithJoin = p.PreviewWithJoin; withJoinToggle.IsChecked = previewWithJoin; projectPath = path;
            SyntaxBox.AdditionalChecks = p.AdditionalChecks; additionalCheckBox.IsChecked = p.AdditionalChecks;
            double total = p.Ratios.Sum(); leftColumn.Width = new GridLength(p.Ratios[0] / total, GridUnitType.Star); workColumn.Width = new GridLength(p.Ratios[1] / total, GridUnitType.Star); toggleColumn.Width = new GridLength(p.Ratios[2] / total, GridUnitType.Star);
            SetToggleEditor(); RenderReferences(); tabs.SelectedIndex = Math.Clamp(p.SelectedTab, -1, references.Count - 1); RenderWork(); RenderTogglePreview(); workScroll.Content = showingPreview ? promptPreview : work; RefreshPromptPreview(); undo.Clear(); redo.Clear(); dirty = false; Update("프로젝트를 불러왔습니다: " + path);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "프로젝트 불러오기 실패", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    void Export()
    {
        Keyboard.ClearFocus(); if (basis is null && (Current ?? references.FirstOrDefault()) is { } source) EnsureBasis(source); if (basis is null) { MessageBox.Show(this, "내보내기 전에 참조 프리셋을 하나 불러오세요.", "내보내기"); return; }
        var d = Dialog("프리셋 내보내기", 540, 290, out var p); p.Children.Add(Text("RisuAI에 표시할 프리셋 이름", 14)); var name = new TextBox { Text = basis.Name + " · 작업본", Margin = new Thickness(0, 10, 0, 14) }; p.Children.Add(name); p.Children.Add(Button("파일 이름과 위치 선택", () => { if (!string.IsNullOrWhiteSpace(name.Text)) d.DialogResult = true; }, true)); if (d.ShowDialog() != true) return;
        string safe = string.Concat(name.Text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)); var save = new SaveFileDialog { Filter = "RisuAI 프리셋|*.risup", DefaultExt = ".risup", AddExtension = true, FileName = safe + ".risup", OverwritePrompt = true }; if (save.ShowDialog(this) != true) return;
        try { var output = basis.Clone(); output.Data.Set("name", Value.String(name.Text.Trim())); output.Data.Set("promptTemplate", Value.Array(blocks.Select(v => v.Clone()))); output.Data.Set("customPromptTemplateToggle", Value.String(NormalizeToggleNewlines(customToggleText))); output.Data.Set("regex", Value.Array(regexScripts.Select(v => v.Clone()))); PresetSecurity.Scrub(output.Data); RisupCodec.Save(output, save.FileName); Update("내보내기 완료: " + save.FileName); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "내보내기 실패", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    Window Dialog(string title, int width, int height, out StackPanel panel) { panel = new StackPanel { Margin = new Thickness(24) }; return new Window { Title = title, Width = width, Height = height, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Content = panel }; }
    void About() { var d = Dialog("Risup Editor 정보", 690, 550, out var p); p.Children.Add(Text("Risup Editor 0.7.4", 23)); p.Children.Add(Text("로컬 프리셋·커스텀 토글·정규식 편집기 · AGPL-3.0", 13, Muted)); p.Children.Add(Text("설정 스키마 기준: " + PresetSchema.ReferenceVersion, 11, Muted)); using var s = typeof(MainWindow).Assembly.GetManifestResourceStream("RisupEditor.LICENSE-AGPL.txt")!; using var reader=new StreamReader(s); p.Children.Add(new TextBox { Text = reader.ReadToEnd(), IsReadOnly=true, TextWrapping=TextWrapping.Wrap, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Height=360, Margin=new Thickness(0,15,0,0) }); d.ShowDialog(); }

    public async void RunSelfTest(string folder)
    {
        try
        {
            if (tabs.Items.Count != 0) throw new Exception("empty reference tabs");
            string fixture = Path.Combine(folder, "reference.risup"); for (int i = 0; i < 10; i++) { references.Add(RisupCodec.Load(fixture)); collapsedReferences.Add(new()); collapsedReferenceSections.Add(new()); } RenderReferences(); tabs.SelectedIndex = 0; Copy(true); basis = OtherPreset(references[0]); workSections = null; RenderWork();
            if (blocks.Count != references[0].Blocks!.Count) throw new Exception("tab copy"); string original = references[0].Blocks![0].Str("text"); var textbox = LogicalDescendants<SyntaxBox>(workCards[blocks[0]]).First(t => t.Tag is FieldAddress a && a.Key == "text"); textbox.Text = "UI 편집 테스트";
            if (blocks[0].Str("text") != "UI 편집 테스트" || references[0].Blocks![0].Str("text") != original) throw new Exception("editing/reference isolation"); Undo(); Redo();
            var moved = blocks[0]; Move(moved, 1); Undo(); referenceIndex = 0; int count = blocks.Count; Copy(false); Undo(); Duplicate(blocks[0]); Delete(blocks[selected]); Undo(); Undo();
            customToggleText = "cot=COT 토글\nstyle=문체=select=간결,상세\nnote=메모=text\narea=지시=textarea"; SetToggleEditor(); RenderTogglePreview(); toggleValues["cot"] = "1"; blocks[0].Set("text", Value.String("A{{#when::toggle::cot}}ON{{:else}}OFF{{/}}B")); showingPreview = true; workScroll.Content = promptPreview; RefreshPromptPreview(); string previewText = PromptPreviewEngine.Build(blocks, basis, toggleValues).PlainText; if (!previewText.Contains("AONB") || previewText.Contains("OFF")) throw new Exception("toggle prompt preview"); blocks[0].Set("text", Value.String("UI 편집 테스트")); RefreshPromptPreview();
            await Task.Delay(100); UpdateLayout(); var previewSurface = (FrameworkElement)Content; var previewBitmap = new RenderTargetBitmap((int)previewSurface.ActualWidth, (int)previewSurface.ActualHeight, 96, 96, PixelFormats.Pbgra32); previewBitmap.Render(previewSurface); var previewPng = new PngBitmapEncoder(); previewPng.Frames.Add(BitmapFrame.Create(previewBitmap)); using (var file = File.Create(Path.Combine(folder, "prompt-preview.png"))) previewPng.Save(file);
            string project = Path.Combine(folder, "ui-project.risupproj"); ProjectFile.Save(CaptureProject(), project); var loaded = ProjectFile.Load(project); if (loaded.ToggleText != customToggleText || loaded.References.Count != 10 || loaded.Blocks.Count != blocks.Count || loaded.ToggleValues["cot"] != "1") throw new Exception("project roundtrip");
            var output = basis!.Clone(); output.Data.Set("promptTemplate", Value.Array(blocks.Select(v => v.Clone()))); output.Data.Set("customPromptTemplateToggle", Value.String(customToggleText)); RisupCodec.Save(output, Path.Combine(folder, "ui-output.risup")); var reopened = RisupCodec.Load(Path.Combine(folder, "ui-output.risup")); if (reopened.Data.Str("customPromptTemplateToggle") != customToggleText) throw new Exception("toggle export");
            showingPreview = false; workScroll.Content = work; RenderWork(); collapsedWork.Add(blocks[1]); RenderWork(); if (Descendants<TextBox>(workCards[blocks[0]]).First().VerticalScrollBarVisibility != ScrollBarVisibility.Disabled) throw new Exception("block textbox scrollbar");
            await Task.Delay(400); UpdateLayout(); var tops = tabs.Items.OfType<TabItem>().Select(t => Math.Round(t.TranslatePoint(new Point(0, 0), tabs).Y)).ToArray(); if (tops.Max() - tops.Min() > 3) throw new Exception("reference tabs wrapped");
            var referenceBody = (StackPanel)((ScrollViewer)((TabItem)tabs.SelectedItem).Content).Content; var referenceCard = LogicalDescendants<Border>(referenceBody).First(b => b.Tag is BlockView); double referenceTop = referenceCard.TranslatePoint(new Point(), (UIElement)Content).Y; double workTop = workCards[blocks[0]].TranslatePoint(new Point(), (UIElement)Content).Y; if (Math.Abs(referenceTop - workTop) > 2) throw new Exception($"block top alignment: {referenceTop} / {workTop}");
            if (Math.Abs(leftColumn.ActualWidth - workColumn.ActualWidth) > 2 || Math.Abs(toggleColumn.ActualWidth * 2 - leftColumn.ActualWidth) > 4) throw new Exception($"initial pane ratio: {leftColumn.ActualWidth}:{workColumn.ActualWidth}:{toggleColumn.ActualWidth}");
            var surface = (FrameworkElement)Content; var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(folder, "ui-preview.png"))) png.Save(file);
            File.WriteAllText(Path.Combine(folder, "ui-test-result.txt"), "PASS: 40:40:20 panes, two splitters, empty tabs, block collapse, full-height text, project roundtrip, custom toggle UI, live prompt preview, toggle export, copy/move/undo"); dirty = false; Application.Current.Shutdown(0);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "ui-test-result.txt"), ex.ToString()); dirty = false; Application.Current.Shutdown(1); }
    }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t; foreach (var x in Descendants<T>(child)) yield return x; } }
}

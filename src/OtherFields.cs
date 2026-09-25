using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace RisupEditor;

public sealed partial class MainWindow
{
    sealed record SectionView(string Key, StackPanel Body, Button Toggle, TextBlock Title, HashSet<string> Collapsed);
    sealed record RegexCard(Value Script, Expander Expander);
    sealed record OtherFieldAddress(Value Data, string Key, FrameworkElement Editor);
    sealed record ModeChoice(string Value, string Label);

    SectionView AddSection(Panel panel, string key, string title, HashSet<string> collapsed, bool first = false)
    {
        var outer = new StackPanel { Margin = new Thickness(0, first ? 0 : 14, 8, 12) };
        if (!first) outer.Children.Add(new Separator { Background = Line, Margin = new Thickness(0, 0, 0, 10) });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; outer.Children.Add(header);
        var body = new StackPanel();
        Button? toggle = null;
        void Apply()
        {
            bool isCollapsed = collapsed.Contains(key); body.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (toggle is not null) toggle.Content = isCollapsed ? "펼치기" : "접기";
        }
        toggle = Button("접기", () => { if (!collapsed.Add(key)) collapsed.Remove(key); Apply(); dirty = true; Update(); }, compact: true);
        toggle.Width = 48; toggle.MinHeight = 25; toggle.Padding = new Thickness(3, 2, 3, 2); toggle.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(toggle, Dock.Left); header.Children.Add(toggle);
        var heading = Text(title, 14); heading.FontWeight = FontWeights.SemiBold; heading.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(heading);
        var view = new SectionView(key, body, toggle, heading, collapsed); outer.Tag = view; outer.Children.Add(body); panel.Children.Add(outer); Apply(); return view;
    }

    static bool OtherKey(string? key) => key is not null
        && key is not "name" and not "promptTemplate" and not "customPromptTemplateToggle" and not "regex"
        && !PresetSchema.SensitiveKeys.Contains(key);
    void EnsureBasis(Preset source)
    {
        basis ??= new Preset { Path = source.Path, Envelope = source.Envelope.Clone(), Data = Value.Map() };
    }
    static Preset OtherPreset(Preset source)
    {
        var copy = source.Clone(); copy.Data.Remove("promptTemplate"); copy.Data.Remove("customPromptTemplateToggle"); copy.Data.Remove("regex"); return copy;
    }
    void CopyCurrentRegex()
    {
        if (Current is null) return;
        var incoming = Current.Data.Get("regex")?.Items;
        if (incoming is not { Count: > 0 }) { Update("현재 참조에는 복사할 정규식이 없습니다."); return; }
        Remember(); EnsureBasis(Current); regexScripts.AddRange(incoming.Select(v => v.Clone())); workSections = null; dirty = true; RenderWork(); Update($"정규식 {incoming.Count}개를 작업 중에 추가했습니다.");
    }
    void OverwriteOther()
    {
        if (Current is null) return;
        if (MessageBox.Show(this, "현재 작업의 기타 정보를 선택한 참조 프리셋 값으로 덮어씁니다.\n\n프롬프트 블록, 토글, 정규식은 유지됩니다. 계속하시겠습니까?", "기타 정보 덮어쓰기", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Remember(); basis = OtherPreset(Current); customOtherFields.Clear(); workSections = null; dirty = true; RenderWork(); Update("기타 정보를 덮어썼습니다. 블록, 토글, 정규식은 유지되었습니다.");
    }
    void AppendSections(Panel panel, Preset? preset, bool editable, HashSet<string> collapsed)
    {
        var toggleSection = AddSection(panel, "toggles", "토글", collapsed); var toggles = toggleSection.Body;
        if (editable)
        {
            if (toggleEditor.Parent is Panel previous) previous.Children.Remove(toggleEditor);
            if (toggleErrors.Parent is Panel previousErrors) previousErrors.Children.Remove(toggleErrors);
            toggleEditor.MinHeight = 100; toggleEditor.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            toggles.Children.Add(toggleEditor); toggles.Children.Add(toggleErrors);
        }
        else toggles.Children.Add(new SyntaxBox { Tag = "SearchToggleConfig", Text = preset?.Data.Str("customPromptTemplateToggle") ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 40 });
        var sourceRegex = editable ? new Value { Items = regexScripts } : preset?.Data.Get("regex") is { IsArray: true } value ? value : new Value { Items = new() };
        var regexSection = AddSection(panel, "regex", $"정규식 · {sourceRegex.Items?.Count ?? 0}개", collapsed); var regex = regexSection.Body;
        BuildRegexEditor(regex, sourceRegex.Items!, editable, regexSection.Title);
        var otherSection = AddSection(panel, "other", "기타", collapsed); var other = otherSection.Body;
        if (editable && preset is null && Current is not null) { EnsureBasis(Current); preset = basis; sectionsBasis = basis; }
        var data = preset?.Data ?? Value.Map();
        var otherBody = new StackPanel { Tag = new OtherRoot(data) }; other.Children.Add(otherBody);
        BuildOtherEditor(otherBody, data, editable && preset is not null);
    }

    void BuildRegexEditor(Panel target, List<Value> items, bool editable, TextBlock sectionTitle)
    {
        void Rebuild()
        {
            target.Children.Clear(); BuildRegexEditor(target, items, editable, sectionTitle);
            sectionTitle.Text = $"정규식 · {items.Count}개"; ChangedOther();
        }
        var root = new StackPanel { Tag = new RegexRoot(items) }; target.Children.Add(root);
        if (items.Count == 0) root.Children.Add(Text("정규식이 없습니다.", 12, Muted));
        for (int index = 0; index < items.Count; index++)
        {
            int itemIndex = index; Value script = items[index];
            if (!script.IsMap)
            {
                var raw = Card(Text($"{index + 1:00} · RisuAI 정규식 객체 형식이 아닙니다. 원본은 보존됩니다.", 12, Muted));
                root.Children.Add(raw); continue;
            }
            var body = new StackPanel { Margin = new Thickness(2, 10, 2, 2) };
            var header = new DockPanel();
            var commands = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(commands, Dock.Right); header.Children.Add(commands);
            if (editable)
            {
                foreach (var command in new[] { ("↑", -1), ("↓", 1) })
                {
                    int step = command.Item2; var button = Button(command.Item1, () => { int next = itemIndex + step; if (next < 0 || next >= items.Count) return; Remember(); (items[itemIndex], items[next]) = (items[next], items[itemIndex]); Rebuild(); }, compact: true);
                    button.Padding = new Thickness(5, 2, 5, 2); commands.Children.Add(button);
                }
                var remove = Button("×", () => { Remember(); items.RemoveAt(itemIndex); Rebuild(); }, compact: true); remove.Padding = new Thickness(5, 2, 5, 2); remove.ToolTip = "정규식 삭제"; commands.Children.Add(remove);
            }
            string CardName() => string.IsNullOrWhiteSpace(script.Str("comment")) ? "Unnamed Script" : script.Str("comment");
            var title = Text($"{index + 1:00}   {CardName()}", 13); title.FontWeight = FontWeights.SemiBold; title.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(title);
            var expander = new Expander { Header = header, Content = body, Margin = new Thickness(0, 4, 8, 4), Padding = new Thickness(8), BorderBrush = Line, BorderThickness = new Thickness(1), Background = Brushes.White };
            expander.Tag = new RegexCard(script, expander); root.Children.Add(expander);

            TextBox AddText(string label, string key, bool multiline = false)
            {
                body.Children.Add(Text(label, 11, Muted));
                var box = new SyntaxBox { Tag = new FieldAddress(script, key), Text = script.Str(key), IsReadOnly = !editable, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 74 : 28, Margin = new Thickness(0, 3, 0, 8) };
                bool captured = false;
                box.TextChanged += (_, _) => { if (!editable || box.Text == script.Str(key)) return; if (!captured) { Remember(); captured = true; } script.Set(key, Value.String(box.Text)); if (key == "comment") title.Text = $"{itemIndex + 1:00}   {CardName()}"; ChangedOther(); };
                box.LostKeyboardFocus += (_, _) => captured = false; body.Children.Add(box); return box;
            }

            AddText("이름", "comment");
            body.Children.Add(Text("Modification Type", 11, Muted));
            var types = new[] { new ChoiceItem("editinput", "입력문 수정"), new("editoutput", "출력문 수정"), new("editprocess", "리퀘스트 데이터 수정"), new("editdisplay", "디스플레이 수정"), new("edittrans", "번역문 수정"), new("disabled", "비활성화됨") }.ToList();
            string currentType = script.Str("type", "editinput"); if (!types.Any(v => v.Value == currentType)) types.Insert(0, new(currentType, $"알 수 없는 값: {currentType}"));
            var type = new ComboBox { Tag = new FieldAddress(script, "type"), ItemsSource = types, DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = currentType, IsEnabled = editable, Margin = new Thickness(0, 3, 0, 8) };
            type.SelectionChanged += (_, _) => { if (!editable || type.SelectedValue is not string value || value == script.Str("type")) return; Remember(); script.Set("type", Value.String(value)); ChangedOther(); }; body.Children.Add(type);
            AddText("IN", "in"); AddText("OUT", "out", true);

            var flagBody = new StackPanel { Margin = new Thickness(0, 5, 0, 8) };
            var custom = new CheckBox { Tag = new FieldAddress(script, "ableFlag"), Content = "Custom Flag", IsChecked = script.Get("ableFlag")?.Boolean() == true, IsEnabled = editable, Margin = new Thickness(0, 5, 0, 5) };
            body.Children.Add(custom); body.Children.Add(flagBody);
            void BuildFlags()
            {
                flagBody.Children.Clear(); flagBody.Visibility = custom.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; if (custom.IsChecked != true) return;
                flagBody.Children.Add(Text("Normal Flag", 11, Muted));
                var flags = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) }; flagBody.Children.Add(flags);
                foreach (var entry in new[] { ("Global (g)", "g"), ("Case Insensitive (i)", "i"), ("Multi Line (m)", "m"), ("Unicode (u)", "u"), ("Dot All (s)", "s"), ("Move Top", "<move_top>"), ("Move Bottom", "<move_bottom>"), ("Repeat Back", "<repeat_back>"), ("IN CBS Parsing", "<cbs>"), ("No Newline Subfix", "<no_end_nl>") })
                {
                    string token = entry.Item2; var check = new CheckBox { Content = entry.Item1, IsChecked = FlagContains(script.Str("flag"), token), IsEnabled = editable, Margin = new Thickness(0, 2, 12, 2), Tag = new FieldAddress(script, "flag") };
                    check.Click += (_, _) => { if (!editable) return; Remember(); string flag = script.Str("flag"); script.Set("flag", Value.String(SetFlag(flag, token, check.IsChecked == true))); ChangedOther(); }; flags.Children.Add(check);
                }
                string extra = UnknownFlagPart(script.Str("flag")); if (extra.Length > 0) flagBody.Children.Add(Text("추가 원본 플래그: " + extra, 10, Muted));
                flagBody.Children.Add(Text("Order Flag", 11, Muted));
                string savedOrder = Regex.Match(script.Str("flag"), @"<order (-?\d+)>") is { Success: true } match ? match.Groups[1].Value : "0";
                var order = new TextBox { Tag = new FieldAddress(script, "flag"), Text = savedOrder, IsReadOnly = !editable, Margin = new Thickness(0, 3, 0, 4) };
                void CommitOrder() { if (!editable || order.Text == savedOrder) return; if (!int.TryParse(order.Text, out int value)) { order.BorderBrush = Brushes.IndianRed; return; } string flag = script.Str("flag"); string next = Regex.IsMatch(flag, @"<order -?\d+>") ? new Regex(@"<order -?\d+>").Replace(flag, $"<order {value}>", 1) : flag + $"<order {value}>"; if (next != flag) { Remember(); script.Set("flag", Value.String(next)); ChangedOther(); } savedOrder = value.ToString(CultureInfo.InvariantCulture); order.BorderBrush = Line; }
                order.LostKeyboardFocus += (_, _) => CommitOrder(); order.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitOrder(); }; flagBody.Children.Add(order);
            }
            custom.Click += (_, _) => { if (!editable) return; Remember(); bool enabled = custom.IsChecked == true; script.Set("ableFlag", Value.Bool(enabled)); if (enabled && string.IsNullOrEmpty(script.Str("flag"))) script.Set("flag", Value.String("g")); BuildFlags(); ChangedOther(); };
            BuildFlags();
        }
        if (editable) root.Children.Add(Button("+ 정규식", () => { var value = Value.Map(); value.Set("comment", Value.String("")); value.Set("in", Value.String("")); value.Set("out", Value.String("")); value.Set("type", Value.String("editinput")); Remember(); items.Add(value); Rebuild(); }, compact: true));
    }

    static bool FlagContains(string flag, string token)
    {
        if (token.Length == 1) return Regex.Replace(flag, "<(.+?)>", "").Contains(token, StringComparison.Ordinal);
        return flag.Contains(token, StringComparison.Ordinal);
    }
    static string SetFlag(string flag, string token, bool enabled)
    {
        if (enabled) return FlagContains(flag, token) ? flag : flag + token;
        int index = flag.IndexOf(token, StringComparison.Ordinal); return index < 0 ? flag : flag.Remove(index, token.Length);
    }
    static string UnknownFlagPart(string flag)
    {
        var known = new HashSet<string>(new[] { "<move_top>", "<move_bottom>", "<repeat_back>", "<cbs>", "<no_end_nl>" }, StringComparer.Ordinal);
        var unknownTokens = Regex.Matches(flag, @"<[^>]*>").Select(match => match.Value).Where(token => !known.Contains(token) && !Regex.IsMatch(token, @"^<order -?\d+>$"));
        string plain = Regex.Replace(flag, @"<[^>]*>", ""); string unknownPlain = new(plain.Where(c => !"gimus".Contains(c)).ToArray());
        return string.Concat(unknownTokens) + unknownPlain;
    }

    void BuildOtherEditor(Panel target, Value data, bool editable)
    {
        target.Children.Add(Text($"기준: {PresetSchema.ReferenceVersion}", 10, Muted));
        foreach (var group in PresetSchema.Fields.GroupBy(v => v.Group))
        {
            var content = new StackPanel { Margin = new Thickness(6, 4, 2, 8) };
            var expander = new Expander { Header = $"{group.Key} · {group.Count()}개", Content = content, IsExpanded = group.Key == "파라미터", Margin = new Thickness(0, 5, 0, 2) };
            target.Children.Add(expander);
            foreach (var field in group) BuildOtherField(content, data, field, editable);
        }
        var unknown = data.Fields?.Where(p => p.Key.Text() is string key && OtherKey(key) && !PresetSchema.KnownKeys.Contains(key)).ToList() ?? [];
        if (unknown.Count > 0)
        {
            var content = new StackPanel { Margin = new Thickness(6, 4, 2, 8) };
            target.Children.Add(new Expander { Header = $"알 수 없는 확장 항목 · {unknown.Count}개", Content = content, Margin = new Thickness(0, 8, 0, 2) });
            foreach (var pair in unknown) BuildUnknownField(content, data, pair.Key.Text()!, pair.Item, editable);
        }
    }

    void BuildOtherField(Panel target, Value data, PresetField field, bool editable)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 7, 0, 9) }; target.Children.Add(panel);
        var header = new WrapPanel(); panel.Children.Add(header);
        var label = Text($"{field.Label} ({field.Key}) · {FieldTypeLabel(field)}", 12); label.FontWeight = FontWeights.SemiBold; header.Children.Add(label);
        if (field.Help is not null) { var help = Button("설명", () => MessageBox.Show(this, field.Help, $"{field.Label} ({field.Key})", MessageBoxButton.OK, MessageBoxImage.Information), compact: true); help.Margin = new Thickness(7, 0, 0, 0); help.Padding = new Thickness(4, 1, 4, 1); help.MinHeight = 22; header.Children.Add(help); }

        string mode = FieldMode(data.Get(field.Key), field, editable && customOtherFields.Contains(field.Key));
        var modes = FieldModes(field);
        var modeBox = new ComboBox { ItemsSource = modes, DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = mode, IsEnabled = editable, Margin = new Thickness(0, 4, 0, 4), Tag = new OtherFieldAddress(data, field.Key, panel) };
        panel.Children.Add(modeBox);
        bool changing = false;
        modeBox.SelectionChanged += (_, _) =>
        {
            if (!editable || changing || modeBox.SelectedValue is not string selected || selected == FieldMode(data.Get(field.Key), field, customOtherFields.Contains(field.Key))) return;
            Remember();
            if (selected == "custom") customOtherFields.Add(field.Key); else customOtherFields.Remove(field.Key);
            if (selected is "keep" or "remove") data.Remove(field.Key);
            else if (selected == "disabled") data.Set(field.Key, Value.Int(-1000));
            else if (selected == "default" && field.Default is not null) data.Set(field.Key, field.Default.Clone());
            else if (selected == "custom" && data.Get(field.Key) is null) data.Set(field.Key, field.Default?.Clone() ?? InitialValue(field));
            int position = target.Children.IndexOf(panel);
            target.Children.Remove(panel); BuildOtherField(target, data, field, editable);
            UIElement replacement = target.Children[^1]; target.Children.RemoveAt(target.Children.Count - 1); target.Children.Insert(Math.Max(0, position), replacement);
            ChangedOther();
        };

        if (mode == "custom") BuildCustomField(panel, data, field, editable);
        else
        {
            string summary = mode switch { "default" => "RisuAI 프리셋 기본값: " + DisplayValue(field.Default), "disabled" => "비활성화 값: -1000", "keep" => "프리셋에 이 필드를 기록하지 않습니다.", _ => "프리셋에 이 필드를 기록하지 않습니다." };
            panel.Children.Add(Text(summary, 11, Muted));
        }
    }

    void BuildCustomField(Panel panel, Value data, PresetField field, bool editable)
    {
        var warning = Text("", 11, Brushes.IndianRed); warning.Margin = new Thickness(0, 3, 0, 0);
        Value current = data.Get(field.Key) ?? InitialValue(field);
        void Set(Value value) { customOtherFields.Add(field.Key); data.Set(field.Key, value); warning.Text = Validate(value, field); ChangedOther(); }
        if (field.Kind == PresetFieldKind.Boolean)
        {
            var check = new CheckBox { IsChecked = current.Raw?[0] is 0xc2 or 0xc3 ? current.Boolean() : null, IsThreeState = current.Raw?[0] is not (0xc2 or 0xc3), IsEnabled = editable, Tag = new FieldAddress(data, field.Key), Margin = new Thickness(0, 4, 0, 2) };
            check.Click += (_, _) => { if (!editable) return; Remember(); check.IsThreeState = false; Set(Value.Bool(check.IsChecked == true)); }; panel.Children.Add(check);
        }
        else if (field.Kind == PresetFieldKind.Select)
        {
            string raw = Scalar(current) ?? ""; var choices = field.Choices.ToList(); if (!choices.Any(v => v.Value == raw)) choices.Insert(0, new(raw, $"알 수 없는 값: {raw}"));
            var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = raw, IsEnabled = editable, Tag = new FieldAddress(data, field.Key), Margin = new Thickness(0, 4, 0, 2) };
            combo.SelectionChanged += (_, _) => { if (!editable || combo.SelectedValue is not string value || value == (Scalar(data.Get(field.Key)!) ?? "")) return; Remember(); Set(field.Choices.Any(v => v.Value == value) && field.Choices.First(v => v.Value == value).Value.All(c => char.IsDigit(c) || c == '-') && long.TryParse(value, out long number) ? Value.Int(number) : Value.String(value)); }; panel.Children.Add(combo);
        }
        else
        {
            string text = field.Kind switch { PresetFieldKind.StringArray when current.Items is not null => string.Join("\n", current.Items.Select(v => v.Text() ?? Scalar(v) ?? "")), PresetFieldKind.Json => ValueJson.Format(current) ?? current.Text() ?? "", _ => Scalar(current) ?? "" };
            var box = new SyntaxBox { Text = text, IsReadOnly = !editable, AcceptsReturn = field.Kind is PresetFieldKind.Multiline or PresetFieldKind.StringArray or PresetFieldKind.Json, TextWrapping = TextWrapping.Wrap, MinHeight = field.Kind is PresetFieldKind.Multiline or PresetFieldKind.StringArray or PresetFieldKind.Json ? 70 : 28, Tag = new FieldAddress(data, field.Key), Margin = new Thickness(0, 4, 0, 2) };
            bool captured = false;
            box.TextChanged += (_, _) =>
            {
                if (!editable) return; if (!captured) { Remember(); captured = true; }
                Value value = field.Kind switch
                {
                    PresetFieldKind.Number => ParseScalar(box.Text) ?? Value.String(box.Text),
                    PresetFieldKind.StringArray => Value.Array(string.IsNullOrEmpty(box.Text) ? [] : box.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(Value.String)),
                    PresetFieldKind.Json => ValueJson.Parse(box.Text) ?? Value.String(box.Text),
                    _ => Value.String(box.Text)
                };
                Set(value);
            };
            box.LostKeyboardFocus += (_, _) => captured = false; panel.Children.Add(box);
        }
        warning.Text = Validate(current, field); panel.Children.Add(warning);
    }

    void BuildUnknownField(Panel panel, Value data, string key, Value value, bool editable)
    {
        var row = new StackPanel { Margin = new Thickness(0, 6, 0, 7) }; panel.Children.Add(row);
        var head = new DockPanel(); row.Children.Add(head); var title = Text(key, 12, Muted); head.Children.Add(title);
        if (editable) { var remove = Button("×", () => { Remember(); data.Remove(key); workSections = null; RenderWork(); ChangedOther(); }, compact: true); DockPanel.SetDock(remove, Dock.Right); head.Children.Insert(0, remove); }
        string? json = ValueJson.Format(value); if (json is null) { row.Children.Add(Text($"원본 MessagePack 값 보존 · {value.Raw?.Length ?? 0} bytes", 11, Muted)); return; }
        var box = new SyntaxBox { Text = json, IsReadOnly = !editable, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 58, Tag = new FieldAddress(data, key) }; bool captured = false;
        box.TextChanged += (_, _) => { if (!editable) return; var parsed = ValueJson.Parse(box.Text); box.BorderBrush = parsed is null ? Brushes.IndianRed : Line; if (parsed is null) return; if (!captured) { Remember(); captured = true; } data.Set(key, parsed); ChangedOther(); }; box.LostKeyboardFocus += (_, _) => captured = false; row.Children.Add(box);
    }

    static List<ModeChoice> FieldModes(PresetField field)
    {
        var result = new List<ModeChoice>();
        if (field.Default is not null) result.Add(new("default", "RisuAI 프리셋 기본값"));
        else result.Add(field.MissingMode == MissingFieldMode.KeepUserSetting ? new("keep", "사용자 RisuAI 설정 유지 (공식 고정값 없음)") : new("remove", "미지정 시 값 제거"));
        if (field.Disableable) result.Add(new("disabled", "비활성화"));
        result.Add(new("custom", "사용자 지정")); return result;
    }
    static string FieldTypeLabel(PresetField field) => field.Kind switch
    {
        PresetFieldKind.Number => "숫자",
        PresetFieldKind.Boolean => "불리언",
        PresetFieldKind.Select => field.Default?.Text() is not null ? "문자열 선택" : "숫자 선택",
        PresetFieldKind.StringArray => "문자열 배열",
        PresetFieldKind.Json => "객체/배열(JSON)",
        _ => "문자열"
    };
    static string FieldMode(Value? value, PresetField field, bool forceCustom = false)
    {
        if (forceCustom) return "custom";
        if (value is null) return field.Default is not null ? "default" : field.MissingMode == MissingFieldMode.KeepUserSetting ? "keep" : "remove";
        if (field.Disableable && value.Number() == -1000) return "disabled";
        if (field.Default is not null && value.Encode().SequenceEqual(field.Default.Encode())) return "default";
        return "custom";
    }
    static Value InitialValue(PresetField field) => field.Kind switch { PresetFieldKind.Boolean => Value.Bool(false), PresetFieldKind.Number => Value.Int(0), PresetFieldKind.StringArray => Value.Array([]), PresetFieldKind.Json => Value.Map(), _ => Value.String("") };
    static string DisplayValue(Value? value)
    {
        string text = value is null ? "없음" : value.Text() ?? Scalar(value) ?? ValueJson.Format(value) ?? "원본 값";
        text = text.Replace("\r", "").Replace("\n", " "); return text.Length > 120 ? text[..117] + "..." : text;
    }
    static string Validate(Value value, PresetField field)
    {
        bool validType = field.Kind switch
        {
            PresetFieldKind.Boolean => value.Raw?[0] is 0xc2 or 0xc3,
            PresetFieldKind.Number => IsNumeric(value),
            PresetFieldKind.Select => Scalar(value) is string selected && field.Choices.Any(v => v.Value == selected) && (field.Default?.Text() is not null ? value.Text() is not null : IsNumeric(value)),
            PresetFieldKind.StringArray => value.Items is not null && value.Items.All(v => v.Text() is not null),
            PresetFieldKind.Json => value.IsMap || value.IsArray,
            _ => value.Text() is not null
        };
        bool range = true;
        if (validType && field.Kind == PresetFieldKind.Number && double.TryParse(Scalar(value), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) range = (field.Min is null || number >= field.Min) && (field.Max is null || number <= field.Max);
        if (validType && range) return "";
        string constraint = field.Min is not null || field.Max is not null ? $" 허용 범위: {field.Min?.ToString(CultureInfo.InvariantCulture) ?? "제한 없음"}~{field.Max?.ToString(CultureInfo.InvariantCulture) ?? "제한 없음"}." : "";
        return "잘못된 값입니다. RisuAI에서 정상적으로 작동하지 않을 수 있습니다." + constraint;
    }
    static bool IsNumeric(Value value) => value.Number() is not null || value.Raw?[0] is 0xca or 0xcb;
    void ChangedOther() { dirty = true; RefreshPromptPreview(); Update(); }
    void BuildValues(Panel target, Value container, bool editable, bool root = false, Action? afterRebuild = null, bool regexRoot = false)
    {
        void Rebuild() { target.Children.Clear(); BuildValues(target, container, editable, root, afterRebuild, regexRoot); afterRebuild?.Invoke(); ChangedOther(); }
        int count = container.Fields?.Count ?? container.Items?.Count ?? 0;
        for (int i = 0; i < count; i++)
        {
            int index = i; var key = container.Fields?[i].Key; if (root && !OtherKey(key?.Text())) continue;
            if (key?.Text() is string sensitive && PresetSchema.SensitiveKeys.Contains(sensitive)) continue;
            var value = container.Fields is not null ? container.Fields[i].Item : container.Items![i];
            void Replace(Value next) { if (container.Fields is not null) container.Fields[index] = (container.Fields[index].Key, next); else container.Items![index] = next; }
            var row = new StackPanel { Tag = new ValueRow(i), Margin = new Thickness(0, 7, 0, 5) }; target.Children.Add(row);
            var header = new WrapPanel(); row.Children.Add(header);
            string label = key?.Text() ?? (key is null ? value.IsMap ? value.Str("name", value.Str("comment", $"정규식 {i + 1}")) : $"[{i}]" : "[비문자열 키]");
            header.Children.Add(Text(label, 12, Muted));
            if (editable)
            {
                header.Children.Add(Button("×", () => { Remember(); if (container.Fields is not null) container.Fields.RemoveAt(index); else container.Items!.RemoveAt(index); Rebuild(); }, compact: true));
                if (key?.Text() is not null) header.Children.Add(Button("이름", () => { string? name = AskName(label); if (name is null || name == label || (root && !OtherKey(name)) || container.Get(name) is not null) return; Remember(); container.Fields![index] = (Value.String(name), value); Rebuild(); }, compact: true));
                if (container.Items is not null) foreach (int step in new[] { -1, 1 }) header.Children.Add(Button(step < 0 ? "↑" : "↓", () => { int next = index + step; if (next < 0 || next >= container.Items.Count) return; Remember(); (container.Items[index], container.Items[next]) = (container.Items[next], container.Items[index]); Rebuild(); }, compact: true));
            }
            if (value.IsMap || value.IsArray)
            {
                var children = new StackPanel { Margin = new Thickness(12, 0, 0, 0) }; var expander = new Expander { Header = value.IsMap ? "객체" : $"배열 ({value.Items!.Count})", Content = children };
                bool built = false; expander.Expanded += (_, _) => { if (!built) { built = true; BuildValues(children, value, editable); } }; row.Children.Add(expander); continue;
            }
            byte kind = value.Raw?[0] ?? 0xc0;
            if (kind is 0xc2 or 0xc3)
            {
                var check = new CheckBox { IsChecked = value.Boolean(), IsEnabled = editable }; check.Click += (_, _) => { Remember(); Replace(Value.Bool(check.IsChecked == true)); ChangedOther(); }; row.Children.Add(check); continue;
            }
            string? scalar = Scalar(value);
            if (scalar is null) { row.Children.Add(Text($"원본 보존 · {value.Raw?.Length ?? 0} bytes", 11, Muted)); continue; }
            bool textValue = value.Text() is not null;
            var box = new SyntaxBox { Text = scalar, IsReadOnly = !editable, AcceptsReturn = textValue, TextWrapping = TextWrapping.Wrap, MinHeight = 28 };
            bool captured = false; box.LostKeyboardFocus += (_, _) => captured = false;
            box.TextChanged += (_, _) =>
            {
                if (!editable) return;
                Value? next = textValue ? Value.String(box.Text) : ParseScalar(box.Text);
                box.BorderBrush = next is null ? Brushes.IndianRed : Line;
                if (next is null) return;
                if (!captured) { Remember(); captured = true; } Replace(next); ChangedOther();
            };
            row.Children.Add(box);
        }
        if (editable && regexRoot) target.Children.Add(Button("+ 정규식", () =>
        {
            var value = Value.Map(); value.Set("comment", Value.String("")); value.Set("in", Value.String("")); value.Set("out", Value.String("")); value.Set("type", Value.String("editinput"));
            Remember(); container.Items!.Add(value); Rebuild();
        }, compact: true));
        else if (editable) target.Children.Add(Button("+ 항목", () =>
        {
            string? name = container.Fields is null ? "" : AskName(""); if (name is null || (root && !OtherKey(name)) || (container.Fields is not null && container.Get(name) is not null)) return;
            var dialog = Dialog("항목 추가", 340, 200, out var body); var types = new ComboBox { ItemsSource = new[] { "문자열", "숫자", "불리언", "객체", "배열", "null" }, SelectedIndex = 0 }; body.Children.Add(types); body.Children.Add(Button("추가", () => dialog.DialogResult = true)); if (dialog.ShowDialog() != true) return;
            Value value = types.SelectedIndex switch { 1 => Value.Int(0), 2 => Value.Bool(false), 3 => Value.Map(), 4 => Value.Array([]), 5 => new Value { Raw = [0xc0] }, _ => Value.String("") };
            Remember(); if (container.Fields is not null) container.Set(name, value); else container.Items!.Add(value); Rebuild();
        }, compact: true));
    }
    string? AskName(string current)
    {
        var dialog = Dialog("필드 이름", 400, 200, out var body); var input = new TextBox { Text = current }; body.Children.Add(input); body.Children.Add(Button("확인", () => dialog.DialogResult = true)); return dialog.ShowDialog() == true ? input.Text : null;
    }
    static string? Scalar(Value value)
    {
        if (value.Text() is string text) return text; var bytes = value.Raw; if (bytes is null) return null;
        if (bytes[0] == 0xc0) return "null";
        if (bytes[0] == 0xcf) return System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(1)).ToString(CultureInfo.InvariantCulture);
        if (bytes[0] == 0xca) return System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(1)).ToString("R", CultureInfo.InvariantCulture);
        if (bytes[0] == 0xcb) return System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(bytes.AsSpan(1)).ToString("R", CultureInfo.InvariantCulture);
        return value.Number()?.ToString(CultureInfo.InvariantCulture);
    }
    static Value? ParseScalar(string text)
    {
        if (text == "null") return new Value { Raw = [0xc0] };
        if (long.TryParse(text, out long integer)) return Value.Int(integer);
        if (ulong.TryParse(text, out ulong unsigned)) { byte[] raw = new byte[9]; raw[0] = 0xcf; System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(raw.AsSpan(1), unsigned); return new Value { Raw = raw }; }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)) { byte[] raw = new byte[9]; raw[0] = 0xcb; System.Buffers.Binary.BinaryPrimitives.WriteDoubleBigEndian(raw.AsSpan(1), number); return new Value { Raw = raw }; }
        return null;
    }
}

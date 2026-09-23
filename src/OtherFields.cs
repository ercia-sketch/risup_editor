using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RisupEditor;

public sealed partial class MainWindow
{
    static bool OtherKey(string? key) => key is not "promptTemplate" and not "customPromptTemplateToggle";
    void OverwriteOther()
    {
        if (Current is null) return;
        Remember(); basis = Current.Clone(); dirty = true; RenderWork(); Update("기타 정보를 덮어썼습니다.");
    }
    void AppendSections(Panel panel, Preset? preset, bool editable)
    {
        StackPanel Section(string name)
        {
            var section = new StackPanel { Margin = new Thickness(0, 14, 8, 12) };
            section.Children.Add(new Separator { Background = Line, Margin = new Thickness(0, 0, 0, 10) });
            section.Children.Add(Text(name, 14)); panel.Children.Add(section); return section;
        }
        var toggles = Section("Toggle Config");
        if (editable)
        {
            if (toggleEditor.Parent is Panel previous) previous.Children.Remove(toggleEditor);
            if (toggleErrors.Parent is Panel previousErrors) previousErrors.Children.Remove(toggleErrors);
            toggleEditor.MinHeight = 100; toggleEditor.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            toggles.Children.Add(toggleEditor); toggles.Children.Add(toggleErrors);
        }
        else toggles.Children.Add(new SyntaxBox { Tag = "SearchToggleConfig", Text = preset?.Data.Str("customPromptTemplateToggle") ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 40 });
        var other = Section("Other Fields");
        if (preset is null) { other.Children.Add(Text("기타 정보가 없습니다.", 12, Muted)); return; }
        other.Tag = new OtherRoot(preset.Data);
        BuildValues(other, preset.Data, editable, true);
    }
    void ChangedOther() { dirty = true; RefreshPromptPreview(); Update(); }
    void BuildValues(Panel target, Value container, bool editable, bool root = false)
    {
        void Rebuild() { target.Children.Clear(); BuildValues(target, container, editable, root); ChangedOther(); }
        int count = container.Fields?.Count ?? container.Items?.Count ?? 0;
        for (int i = 0; i < count; i++)
        {
            int index = i; var key = container.Fields?[i].Key; if (root && !OtherKey(key?.Text())) continue;
            var value = container.Fields is not null ? container.Fields[i].Item : container.Items![i];
            void Replace(Value next) { if (container.Fields is not null) container.Fields[index] = (container.Fields[index].Key, next); else container.Items![index] = next; }
            var row = new StackPanel { Tag = new ValueRow(i), Margin = new Thickness(0, 7, 0, 5) }; target.Children.Add(row);
            var header = new WrapPanel(); row.Children.Add(header);
            string label = key?.Text() ?? (key is null ? $"[{i}]" : "[비문자열 키]");
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
        if (editable) target.Children.Add(Button("+ 항목", () =>
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

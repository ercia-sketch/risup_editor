using System.IO;

namespace RisupEditor;

internal static class PreviewTests
{
    public static int Run(string folder)
    {
        string resultPath = Path.Combine(folder, "preview-result.txt");
        try
        {
            var settings = Value.Map(); settings.Set("sendChatAsSystem", Value.Bool(false)); settings.Set("postEndInnerFormat", Value.String("END"));
            var data = Value.Map(); data.Set("name", Value.String("preview")); data.Set("aiModel", Value.String("example-model"));
            data.Set("jailbreakToggle", Value.Bool(true)); data.Set("chainOfThought", Value.Bool(false)); data.Set("promptSettings", settings);
            var preset = new Preset { Path = "preview.risup", Envelope = Value.Map(), Data = data };
            var blocks = new List<Value>
            {
                Block("plain", ("role", "system"), ("text", "A{{#when::toggle::flag}}ON{{:else}}OFF{{/}}-{{getglobalvar::toggle_note}}")),
                Block("plain", ("role", "user"), ("text", "{{#when::style::tis::1}}DETAIL{{:else}}SHORT{{/}}")),
                Block("plain", ("role", "assistant"), ("text", "{{#if {{getglobalvar::toggle_flag}}}}NESTED{{:else}}WRONG{{/if}}")),
                Block("description", ("role2", "system"), ("innerFormat", "<d>{{slot}}</d>{{slot}}")),
                Block("chatML", ("text", "<|im_start|>system\nCHATML-S\n<|im_end|><|im_start|>assistant\nCHATML-A\n<|im_end|>")),
                Cache(1, "assistant"),
                Block("cot", ("role", "system"), ("text", "HIDDEN-COT")),
                Block("postEverything")
            };
            var toggles = new Dictionary<string, string> { ["flag"] = "1", ["note"] = "hello", ["style"] = "1" };
            var result = PromptPreviewEngine.Build(blocks, preset, toggles);
            string plain = result.PlainText;
            Require(plain.Contains("AON-hello") && !plain.Contains("OFF"), "checkbox/text toggle");
            Require(plain.Contains("DETAIL") && !plain.Contains("SHORT"), "select tis toggle");
            Require(plain.Contains("NESTED") && !plain.Contains("WRONG"), "nested toggle if");
            Require(result.Entries.Any(e => e.Role == "system" && e.Content.Contains("CHATML-S")), "ChatML system");
            Require(result.Entries.Any(e => e.Role == "assistant" && e.Content.Contains("CHATML-A") && e.CachePoint), "ChatML assistant cache");
            var description = result.Entries.First(e => e.Content.Contains("<d>"));
            Require(description.Content.Count(c => c == PromptPreviewEngine.MarkerStart) == 1 && description.Content.Contains("{{slot}}"), "first slot only");
            Require(!plain.Contains("HIDDEN-COT"), "disabled COT");
            Require(plain.Contains("END"), "post end format");

            var unset = PromptPreviewEngine.Build([Block("plain", ("role", "system"), ("text", "{{getglobalvar::toggle_missing}}"))], preset, new Dictionary<string, string>());
            Require(unset.PlainText.Contains("null"), "unset toggle null");

            const string dynamic = "{{#if {{getvar::runtime}}}}YES{{:else}}NO{{/if}}";
            var unresolved = PromptPreviewEngine.Build([Block("plain", ("role", "system"), ("text", dynamic))], preset, toggles);
            Require(unresolved.PlainText.Contains(dynamic) && unresolved.Warnings.Count > 0, "runtime condition preservation");

            data.Set("aiModel", Value.String("gpt-4"));
            var merged = PromptPreviewEngine.Build([
                Block("plain", ("role", "system"), ("text", "ONE")),
                Block("plain", ("role", "system"), ("text", "TWO"))
            ], preset, toggles);
            Require(merged.Entries.Count == 1 && merged.Entries[0].Content == "ONE\n\nTWO", "system merge");

            File.WriteAllText(resultPath, "PASS: toggles, nested CBS, unresolved runtime, ChatML, slot marker, cache, COT, post-end, system merge");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(resultPath, ex.ToString()); return 1;
        }
    }

    static Value Block(string type, params (string Key, string Value)[] fields)
    {
        var block = Value.Map(); block.Set("type", Value.String(type));
        foreach (var field in fields) block.Set(field.Key, Value.String(field.Value));
        return block;
    }

    static Value Cache(long depth, string role)
    {
        var block = Block("cache", ("role", role)); block.Set("depth", Value.Int(depth)); return block;
    }

    static void Require(bool condition, string name) { if (!condition) throw new Exception("Preview test failed: " + name); }
}

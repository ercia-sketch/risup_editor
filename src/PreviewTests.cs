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
                Block("plain", ("role", "assistant"), ("text", "{{#when {{getglobalvar::toggle_flag}}}}NESTED{{:else}}WRONG{{/if}}")),
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

            string Eval(string text, IReadOnlyDictionary<string, string>? values = null)
                => new ToggleCbsEvaluator(values ?? toggles, false, new HashSet<string>()).Evaluate(text);
            Require(Eval("{{#if_pure 1}} \n  KEEP  \n{{/if}}") == " \n  KEEP  \n", "if_pure whitespace and /if close");
            Require(Eval("{{#if 1}} \n  A  \n    B  \n{{/anything}}") == "A  \nB", "if legacy whitespace and generic close");
            Require(Eval("X{{#if 0}}{{#if 1}}BAD{{/}}{{/if}}Y") == "XY", "false if skips nested blocks");
            Require(Eval("{{#if 1}}A{{:else}}B{{/if}}") == "A{{:else}}B", "if does not implement when else");
            Require(Eval("{{#when::1}}A{{:else}}B{{/when}}") == "A", "when true else");
            Require(Eval("{{#when::0}}A{{:else}}B{{/anything}}") == "B", "when false else and generic close");
            Require(Eval("{{#when::keep::1}}\n  A  \n{{:else}}\n  B  \n{{/}}") == "\n  A  ", "when keep true whitespace");
            Require(Eval("{{#when::keep::0}}\n  A  \n{{:else}}\n  B  \n{{/}}") == "  B  \n", "when keep false whitespace");
            Require(Eval("{{#when::legacy::1}} \n  A  \n{{/}}") == "A", "when legacy whitespace");
            Require(Eval("{{#pure}} {{user}} {{/}}") == "{{user}}", "pure raw block");
            Require(Eval("{{#puredisplay}} {{user}} {{/}}") == "\\{\\{user\\}\\}", "puredisplay reparse prevention");
            Require(Eval("{{#escape::keep}} ({{user}}) {{/}}") == " ({{user}}) ", "escape keep whitespace");
            Require(Eval("{{#code}} A\\nB\t {{/}}") == "A\nB", "code normalization");
            Require(Eval("{{#each [1, 2, 3] as n}}{{slot::n}}{{/}}") == "123", "each JSON array");
            Require(Eval("{{#each a,b,c as n}}{{slot::n}}{{/}}") == "a,b,c", "each non-array fallback");
            Require(Eval("A{{#each invalid}}X{{/}}B") == "AB", "each invalid header discard");
            Require(Eval("{{#each::keep [1, 2] as x}}{{#each::keep [3, 4] as y}}{{slot::x}}{{slot::y}}\n{{/}}{{/}}") == "13\n14\n23\n24\n", "nested each keep");
            Require(Eval("{{#each [1] as n}}{{br}}X{{/}}") == "\nX", "each trims before reparsing");
            Require(Eval("{{#func greet who}}Hello {{arg::1}}{{/}}{{call::greet::World}}") == "Hello World", "function definition and call");
            Require(PromptPreviewEngine.VisibleText(Eval("{{#unknown}}A{{/unknown}}")) == "{{#unknown}}A{{/unknown}}", "unknown block preservation");
            Require(PromptPreviewEngine.VisibleText(Eval("{{ #if 1}}A{{/}}")) == "{{ #if 1}}A{{/}}", "block tokens are not trimmed");

            const string reported = "{{#if_pure {{all::{{less::{{getglobalvar::toggle_사칭}}::4}}::{{not_equal::{{getglobalvar::toggle_사칭}}::null}}}}}}{{#if_pure {{all::{{less::{{getglobalvar::toggle_사칭}}::3}}::{{not_equal::{{getglobalvar::toggle_사칭}}::null}}}}}}PASSIVE{{/if}}{{#if_pure {{? {{getglobalvar::toggle_사칭}}=3}}}}DECENTRALIZED{{/if}}\n{{slot}}\n{{/if}}";
            Require(SyntaxDiagnostics.Analyze(reported, false).Count == 0, "reported nested if_pure syntax diagnostics");
            string Reported(string? value) => Eval(reported, value is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["사칭"] = value });
            Require(Reported("2").Contains("PASSIVE") && !Reported("2").Contains("DECENTRALIZED") && Reported("2").Contains("{{slot}}"), "reported nested if_pure below 3");
            Require(!Reported("3").Contains("PASSIVE") && Reported("3").Contains("DECENTRALIZED") && Reported("3").Contains("{{slot}}"), "reported nested if_pure equals 3");
            Require(Reported("4") == "" && Reported(null) == "", "reported outer if_pure false/null");

            const string dynamic = "{{#if {{getvar::runtime}}}}YES{{:else}}NO{{/if}}";
            var unresolved = PromptPreviewEngine.Build([Block("plain", ("role", "system"), ("text", dynamic))], preset, toggles);
            Require(unresolved.PlainText.Contains(dynamic) && unresolved.Warnings.Count > 0, "runtime condition preservation");
            const string dynamicUnknownBlock = "{{#if {{getvar::runtime}}}}{{#future}}X{{/if}}TAIL";
            var unresolvedUnknownBlock = PromptPreviewEngine.Build([Block("plain", ("role", "system"), ("text", dynamicUnknownBlock))], preset, toggles);
            Require(unresolvedUnknownBlock.PlainText.Contains(dynamicUnknownBlock) && unresolvedUnknownBlock.PlainText.EndsWith("TAIL"), "runtime condition boundary preservation");

            data.Set("aiModel", Value.String("gpt-4"));
            var merged = PromptPreviewEngine.Build([
                Block("plain", ("role", "system"), ("text", "ONE")),
                Block("plain", ("role", "system"), ("text", "TWO"))
            ], preset, toggles);
            Require(merged.Entries.Count == 1 && merged.Entries[0].Content == "ONE\n\nTWO", "system merge");

            File.WriteAllText(resultPath, "PASS: RisuAI block semantics, reported nested if_pure, toggles, unresolved runtime, ChatML, slot marker, cache, COT, post-end, system merge");
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

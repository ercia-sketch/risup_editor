using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace RisupEditor;

internal enum PromptPreviewEntryKind { Message, Runtime }

internal sealed class PromptPreviewEntry(PromptPreviewEntryKind kind, string role, string content, string? note = null)
{
    public PromptPreviewEntryKind Kind { get; } = kind;
    public string Role { get; } = role;
    public string Content { get; set; } = content;
    public string? Note { get; set; } = note;
    public bool CachePoint { get; set; }
}

internal sealed class PromptPreviewResult(List<PromptPreviewEntry> entries, IReadOnlyList<string> warnings)
{
    public List<PromptPreviewEntry> Entries { get; } = entries;
    public IReadOnlyList<string> Warnings { get; } = warnings;
    public string PlainText => string.Join("\n\n", Entries.Select(e =>
        $"[{e.Role.ToUpperInvariant()}]\n{PromptPreviewEngine.VisibleText(e.Content)}"));
}

internal static class PromptPreviewEngine
{
    internal const char MarkerStart = '\uFFF0';
    internal const char MarkerEnd = '\uFFF1';

    internal static string Marker(string label, char kind = 's') => $"{MarkerStart}{kind}{label.Replace(MarkerEnd, ' ')}{MarkerEnd}";
    internal static string VisibleText(string text)
    {
        var output = new StringBuilder(text.Length);
        for (int position = 0; position < text.Length;)
        {
            int start = text.IndexOf(MarkerStart, position);
            if (start < 0) { output.Append(text.AsSpan(position)); break; }
            output.Append(text.AsSpan(position, start - position));
            int end = text.IndexOf(MarkerEnd, start + 1);
            if (end < 0) { output.Append(text.AsSpan(start)); break; }
            int labelStart = start + 1; if (labelStart < end && "sucmrew".Contains(text[labelStart])) labelStart++;
            output.Append(text.AsSpan(labelStart, end - labelStart));
            position = end + 1;
        }
        return output.ToString();
    }

    public static PromptPreviewResult Build(IReadOnlyList<Value> blocks, Preset? basis, IReadOnlyDictionary<string, string> toggles, bool withJoin = false)
    {
        bool jailbreak = basis?.Data.Get("jailbreakToggle")?.Boolean() ?? false;
        bool chainOfThought = basis?.Data.Get("chainOfThought")?.Boolean() ?? false;
        string model = basis?.Data.Str("aiModel") ?? "";
        var settings = basis?.Data.Get("promptSettings");
        bool sendChatAsSystem = settings?.Get("sendChatAsSystem")?.Boolean() ?? false;
        string postEndInnerFormat = settings?.Str("postEndInnerFormat") ?? "";
        var warningSet = new HashSet<string>();
        var evaluator = new ToggleCbsEvaluator(toggles, jailbreak, warningSet);
        var entries = new List<PromptPreviewEntry>();

        foreach (var block in blocks)
        {
            string type = block.Str("type");
            if (type == "jailbreak" && !jailbreak) continue;
            if (type == "cot" && !chainOfThought) continue;

            switch (type)
            {
                case "plain":
                case "jailbreak":
                case "cot":
                    AddMessage(entries, Role(block.Str("role", "system")), evaluator.Evaluate(block.Str("text")));
                    break;
                case "chatML":
                    AddChatMl(entries, block.Str("text"), evaluator, warningSet);
                    break;
                case "description":
                    AddMessage(entries, Role(block.Str("role2", "system")), ApplySlot(evaluator.Evaluate(block.Str("innerFormat")), Marker("캐릭터 설명이 이 위치에 들어갑니다")));
                    break;
                case "persona":
                    AddMessage(entries, Role(block.Str("role2", "system")), ApplySlot(evaluator.Evaluate(block.Str("innerFormat")), Marker("페르소나가 이 위치에 들어갑니다")));
                    break;
                case "memory":
                    AddMessage(entries, Role(block.Str("role2", "system")), ApplySlot(evaluator.Evaluate(block.Str("innerFormat")), Marker("메모리가 이 위치에 들어갑니다")));
                    break;
                case "authornote":
                {
                    string label = string.IsNullOrEmpty(block.Str("defaultText"))
                        ? "작가의 노트가 이 위치에 들어갑니다"
                        : $"작가의 노트가 이 위치에 들어갑니다 · 기본값: {block.Str("defaultText")}";
                    AddMessage(entries, Role(block.Str("role2", "system")), ApplySlot(evaluator.Evaluate(block.Str("innerFormat")), Marker(label)));
                    break;
                }
                case "lorebook":
                    entries.Add(Runtime("로어북", "활성화된 로어북 항목이 이 위치에 역할별 메시지로 들어갑니다"));
                    break;
                case "chat":
                {
                    string start = Display(block.Get("rangeStart"), "0"), end = Display(block.Get("rangeEnd"), "end");
                    string system = sendChatAsSystem && !(block.Get("chatAsOriginalOnSystem")?.Boolean() ?? false) ? " · system 역할로 변환" : "";
                    entries.Add(Runtime("채팅 기록", $"채팅 기록 {start} → {end} 범위가 이 위치에 들어갑니다{system}"));
                    break;
                }
                case "postEverything":
                    entries.Add(Runtime("마지막 삽입 영역", "로어북·메모리·보조 지시 등 마지막 삽입 내용이 이 위치에 들어갑니다"));
                    if (!string.IsNullOrWhiteSpace(postEndInnerFormat)) AddMessage(entries, "system", postEndInnerFormat);
                    break;
                case "cache":
                    ApplyCache(entries, (int)(block.Get("depth")?.Number() ?? 1), block.Str("role", "all"));
                    break;
                default:
                    warningSet.Add($"알 수 없는 블록 유형 '{type}'은 RisuAI의 정적 미리보기에서 결과를 확정할 수 없습니다.");
                    entries.Add(Runtime("알 수 없는 블록", $"{type} 블록은 원본 설정을 보존하지만 여기서는 실행하지 않습니다"));
                    break;
            }
        }

        bool mergeSystem = model.StartsWith("gpt", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("claude", StringComparison.OrdinalIgnoreCase)
            || model is "openrouter" or "reverse_proxy";
        if (mergeSystem) entries = MergeSystemMessages(entries);
        if (withJoin) entries = JoinConsecutiveRoles(entries);
        foreach (var entry in entries) entry.Content = entry.Content.Trim();
        return new(entries, warningSet.ToArray());
    }

    static string Display(Value? value, string fallback) => value?.Text() ?? value?.Number()?.ToString(CultureInfo.InvariantCulture) ?? fallback;
    static string Role(string role) => role == "bot" ? "assistant" : role is "user" or "assistant" or "system" ? role : "system";
    static PromptPreviewEntry Runtime(string role, string label) => new(PromptPreviewEntryKind.Runtime, role, Marker(label));

    static string ApplySlot(string innerFormat, string marker)
    {
        if (string.IsNullOrEmpty(innerFormat)) return marker;
        int slot = innerFormat.IndexOf("{{slot}}", StringComparison.Ordinal);
        return slot < 0 ? innerFormat : innerFormat[..slot] + marker + innerFormat[(slot + 8)..];
    }

    static void AddMessage(List<PromptPreviewEntry> entries, string role, string content, string? note = null)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        entries.Add(new(PromptPreviewEntryKind.Message, role, content, note));
    }

    static void AddChatMl(List<PromptPreviewEntry> entries, string data, ToggleCbsEvaluator evaluator, HashSet<string> warnings)
    {
        const string starter = "<|im_start|>", separator = "<|im_sep|>", ender = "<|im_end|>";
        string trimmed = data.Trim();
        if (!trimmed.StartsWith(starter, StringComparison.Ordinal))
        {
            warnings.Add("ChatML 블록이 <|im_start|>로 시작하지 않아 역할별 메시지로 해석하지 못했습니다.");
            entries.Add(Runtime("ChatML 오류", "유효하지 않은 ChatML 원문은 이 위치에서 메시지로 변환되지 않습니다"));
            return;
        }
        foreach (string source in trimmed.Split(starter, StringSplitOptions.RemoveEmptyEntries))
        {
            string value = source; string role = "user";
            foreach (string candidate in new[] { "user", "system", "assistant" })
            {
                string compact = candidate + separator;
                if (value.StartsWith(compact, StringComparison.Ordinal)) { role = candidate; value = value[compact.Length..]; break; }
                if (value.StartsWith(candidate + " ", StringComparison.Ordinal) || value.StartsWith(candidate + "\n", StringComparison.Ordinal)) { role = candidate; value = value[(candidate.Length + 1)..]; break; }
            }
            value = value.Trim(); if (value.EndsWith(ender, StringComparison.Ordinal)) value = value[..^ender.Length];
            int thoughts = 0;
            value = Regex.Replace(value, @"<Thoughts>(.+)</Thoughts>", _ => { thoughts++; return ""; }, RegexOptions.Singleline);
            AddMessage(entries, role, evaluator.Evaluate(value), thoughts == 0 ? null : $"{thoughts}개 thought 포함");
        }
    }

    static void ApplyCache(List<PromptPreviewEntry> entries, int depth, string role)
    {
        int remaining = Math.Max(0, depth);
        for (int i = entries.Count - 1; i >= 0 && remaining > 0; i--)
        {
            var entry = entries[i];
            if (entry.Kind != PromptPreviewEntryKind.Message || (role != "all" && entry.Role != Role(role))) continue;
            entry.CachePoint = true; remaining--;
        }
    }

    static List<PromptPreviewEntry> MergeSystemMessages(List<PromptPreviewEntry> source)
    {
        var result = new List<PromptPreviewEntry>();
        foreach (var entry in source)
        {
            if (entry.Kind == PromptPreviewEntryKind.Message && entry.Role == "system" && result.LastOrDefault() is { Kind: PromptPreviewEntryKind.Message, Role: "system" } previous)
            {
                previous.Content += "\n\n" + entry.Content;
                previous.CachePoint |= entry.CachePoint;
                if (!string.IsNullOrEmpty(entry.Note)) previous.Note = string.IsNullOrEmpty(previous.Note) ? entry.Note : previous.Note + " · " + entry.Note;
            }
            else result.Add(entry);
        }
        return result;
    }

    static List<PromptPreviewEntry> JoinConsecutiveRoles(List<PromptPreviewEntry> source)
    {
        var result = new List<PromptPreviewEntry>();
        foreach (var entry in source)
        {
            if (entry.Kind == PromptPreviewEntryKind.Message && result.LastOrDefault() is { Kind: PromptPreviewEntryKind.Message } previous && previous.Role == entry.Role)
            {
                previous.Content += "\n" + entry.Content; previous.CachePoint |= entry.CachePoint;
                if (!string.IsNullOrEmpty(entry.Note)) previous.Note = string.IsNullOrEmpty(previous.Note) ? entry.Note : previous.Note + " · " + entry.Note;
            }
            else result.Add(entry);
        }
        return result;
    }
}

internal sealed class ToggleCbsEvaluator(IReadOnlyDictionary<string, string> toggles, bool jailbreak, HashSet<string> warnings)
{
    sealed record InlineResult(string Text, bool Unknown);
    readonly Dictionary<string, (string Data, string[] Args)> functions = new(StringComparer.Ordinal);
    int callDepth;
    static readonly HashSet<string> RuntimeFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "addvar", "asset", "assetlist", "audio", "authornote", "axmodel", "bg", "bgm", "bkspc", "button", "calc", "char",
        "chardisplayasset", "charhistory", "chatindex", "date", "declare", "description", "dice", "emotion", "emotionlist", "erase",
        "exampledialogue", "file", "firstmsgindex", "getvar", "globalnote", "hash", "history", "idleduration", "image", "img", "inlay",
        "inlayed", "inlayeddata", "isfirstmsg", "isodate", "isotime", "jb", "lastmessage", "lastmessageid", "lorebook", "mainprompt",
        "maxcontext", "messagedate", "messageidleduration", "messagetime", "messageunixtimearray", "metadata", "model", "moduleassetlist",
        "moduleenabled", "path", "persona", "personality", "pick", "position", "prefillsupported", "previouscharchat", "previouschatlog",
        "previoususerchat", "randint", "random", "return", "risu", "role", "roll", "rollp", "scenario", "screenheight", "screenwidth",
        "setdefaultvar", "settempvar", "setvar", "source", "tempvar", "time", "triggerid", "unixtime", "user", "userhistory", "video", "videoimg",
        "bot", "chardesc", "charmessages", "charpersona", "datetimeformat", "examplemessage", "firstmessageindex", "gettempvar",
        "isfirstmessage", "jailbreak", "lastcharmessage", "lastmessageindex", "lastusermessage", "messages", "prefill", "raw",
        "systemnote", "systemprompt", "ujb", "usermessages", "userpersona", "worldinfo"
    };

    public string Evaluate(string text)
    {
        functions.Clear(); callDepth = 0;
        return EvaluateNested(text);
    }

    string EvaluateNested(string text)
    {
        int position = 0; string result = ParseSequence(text, ref position, false, out _);
        return result;
    }

    string ParseSequence(string source, ref int position, bool stopOnClose, out string? stop)
    {
        var output = new StringBuilder(); stop = null;
        while (position < source.Length)
        {
            int open = source.IndexOf("{{", position, StringComparison.Ordinal);
            if (open < 0) { output.Append(source.AsSpan(position)); position = source.Length; break; }
            output.Append(source.AsSpan(position, open - position));
            if (!ReadToken(source, open, out string token, out int after)) { output.Append(source.AsSpan(open)); position = source.Length; break; }
            position = after; string syntax = token;
            if (syntax.StartsWith('/') && !syntax.StartsWith("//"))
            {
                if (stopOnClose) { stop = syntax; return output.ToString(); }
                output.Append("{{").Append(token).Append("}}"); continue;
            }
            if (syntax == ":else") { output.Append("{{").Append(token).Append("}}"); continue; }

            if (syntax is "#pure" or "#puredisplay" or "#pure_display" || syntax.StartsWith("#escape", StringComparison.Ordinal))
            {
                int originalStart = open;
                if (!TryReadRawBlock(source, ref position, out string body))
                {
                    warnings.Add($"닫히지 않은 CBS '{{{{{syntax}}}}}' 블록은 원문으로 보존했습니다."); output.Append(PromptPreviewEngine.Marker(source[originalStart..position], 'w')); continue;
                }
                if (syntax is "#puredisplay" or "#pure_display") output.Append(body.Trim().Replace("{{", "\\{\\{", StringComparison.Ordinal).Replace("}}", "\\}\\}", StringComparison.Ordinal));
                else
                {
                    bool keep = syntax.StartsWith("#escape", StringComparison.Ordinal) && syntax[7..].Trim() == "::keep";
                    output.Append(keep ? body : body.Trim());
                }
                continue;
            }

            if (syntax.StartsWith("#each", StringComparison.Ordinal))
            {
                int originalStart = open;
                if (!TryReadRawBlock(source, ref position, out string body))
                {
                    warnings.Add("닫히지 않은 CBS #each 블록은 원문으로 보존했습니다."); output.Append(PromptPreviewEngine.Marker(source[originalStart..position], 'w')); continue;
                }
                string header = syntax[5..].Trim(); bool keep = false;
                if (header.StartsWith("::keep ", StringComparison.Ordinal)) { keep = true; header = header[7..].Trim(); }
                if (header.StartsWith("as ", StringComparison.Ordinal)) header = header[3..].Trim();
                int asIndex = header.LastIndexOf(" as ", StringComparison.Ordinal); if (asIndex < 0) asIndex = header.LastIndexOf(' ');
                if (asIndex <= 0) continue;
                string arraySource = header[..asIndex], slot = header[(asIndex + (header.AsSpan(asIndex).StartsWith(" as ") ? 4 : 1))..].Trim();
                var resolvedArray = ResolveInlineText(arraySource);
                if (resolvedArray.Unknown || !RisuStaticCbs.TryParseArrayValues(resolvedArray.Text, out var values))
                {
                    output.Append(PreserveSource(source[originalStart..position], "CBS #each의 배열은 현재 프리셋만으로 계산할 수 없습니다.").Text); continue;
                }
                string template = keep ? body : TrimLines(body);
                string expanded = string.Concat(values.Select(value => template.Replace($"{{{{slot::{slot}}}}}", value, StringComparison.Ordinal)));
                output.Append(EvaluateNested(keep ? expanded : expanded.Trim())); continue;
            }

            if (syntax.StartsWith("#if", StringComparison.Ordinal))
            {
                int originalStart = open;
                var resolved = ResolveInlineText(syntax);
                if (resolved.Unknown)
                {
                    if (!TryReadBlockForPreservation(source, ref position, out _)) warnings.Add("닫히지 않은 CBS 조건 블록은 원문으로 보존했습니다.");
                    output.Append(PreserveSource(source[originalStart..position], $"런타임 값이 필요한 조건 '{{{{{syntax}}}}}'은 평가하지 않고 원문으로 보존했습니다.").Text); continue;
                }
                bool condition = IfTruthy(resolved.Text), keepWhitespace = resolved.Text.StartsWith("#if_pure", StringComparison.Ordinal);
                if (!condition)
                {
                    if (!TryReadRawBlock(source, ref position, out _)) { warnings.Add("닫히지 않은 CBS 조건 블록은 원문으로 보존했습니다."); output.Append(PromptPreviewEngine.Marker(source[originalStart..position], 'w')); }
                    continue;
                }
                string body = ParseSequence(source, ref position, true, out string? marker);
                if (marker is null)
                {
                    warnings.Add("닫히지 않은 CBS 조건 블록은 원문으로 보존했습니다."); output.Append(PromptPreviewEngine.Marker(source[originalStart..position], 'w')); continue;
                }
                output.Append(keepWhitespace ? body : TrimLines(body)); continue;
            }

            if (syntax.StartsWith("#when", StringComparison.Ordinal))
            {
                int originalStart = open;
                var resolved = ResolveInlineText(syntax);
                bool? condition = EvaluateCondition(resolved.Text, resolved.Unknown, out string mode);
                if (condition is null)
                {
                    if (!TryReadBlockForPreservation(source, ref position, out _)) warnings.Add("닫히지 않은 CBS 조건 블록은 원문으로 보존했습니다.");
                    output.Append(PreserveSource(source[originalStart..position], $"런타임 값이 필요한 조건 '{{{{{syntax}}}}}'은 평가하지 않고 원문으로 보존했습니다.").Text); continue;
                }
                if (mode == "legacy" && !condition.Value)
                {
                    if (!TryReadRawBlock(source, ref position, out _)) { warnings.Add("닫히지 않은 CBS 조건 블록은 원문으로 보존했습니다."); output.Append(PromptPreviewEngine.Marker(source[originalStart..position], 'w')); }
                    continue;
                }
                string body = ParseSequence(source, ref position, true, out string? marker);
                if (marker is null)
                {
                    warnings.Add("닫히지 않은 CBS 조건 블록은 원문으로 보존했습니다."); output.Append(PromptPreviewEngine.Marker(source[originalStart..position], 'w')); continue;
                }
                output.Append(mode == "legacy" ? TrimLines(body) : SelectWhenBody(body, condition.Value, mode == "keep"));
                continue;
            }

            if (syntax == "#code")
            {
                int originalStart = open;
                string body = ParseSequence(source, ref position, true, out string? marker);
                if (marker is null) { warnings.Add("닫히지 않은 CBS #code 블록은 원문으로 보존했습니다."); output.Append(PromptPreviewEngine.Marker(source[originalStart..position], 'w')); continue; }
                output.Append(NormalizeCode(body)); continue;
            }

            if (syntax.StartsWith("#func", StringComparison.Ordinal))
            {
                string[] parts = syntax.Split(' ', StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    int originalStart = open;
                    if (!TryReadRawBlock(source, ref position, out string body)) { warnings.Add("닫히지 않은 CBS #func 블록은 원문으로 보존했습니다."); output.Append(PromptPreviewEngine.Marker(source[originalStart..position], 'w')); continue; }
                    functions[parts[1]] = (body.Trim(), parts[1..]); continue;
                }
            }

            var inline = ResolveInlineToken(syntax);
            output.Append(inline.Text);
        }
        return output.ToString();
    }

    static bool ReadToken(string source, int start, out string token, out int after)
    {
        int depth = 1;
        for (int i = start + 2; i < source.Length - 1;)
        {
            if (source.AsSpan(i).StartsWith("{{")) { depth++; i += 2; continue; }
            if (source.AsSpan(i).StartsWith("}}"))
            {
                depth--; if (depth == 0) { token = source[(start + 2)..i]; after = i + 2; return true; }
                i += 2; continue;
            }
            i++;
        }
        token = ""; after = source.Length; return false;
    }

    static bool TryReadRawBlock(string source, ref int position, out string body)
    {
        int contentStart = position, scan = position, depth = 1;
        while (scan < source.Length)
        {
            int open = source.IndexOf("{{", scan, StringComparison.Ordinal); if (open < 0 || !ReadToken(source, open, out string token, out int after)) break;
            if ((token.StartsWith('#') || token.StartsWith(':')) && token != ":else") depth++;
            else if (token.StartsWith('/') && !token.StartsWith("//") && --depth == 0)
            {
                body = source[contentStart..open]; position = after; return true;
            }
            scan = after;
        }
        body = source[contentStart..]; position = source.Length; return false;
    }

    static bool TryReadBlockForPreservation(string source, ref int position, out string body)
    {
        int contentStart = position, scan = position;
        var rawFrames = new Stack<bool>(); rawFrames.Push(false);
        while (scan < source.Length)
        {
            int open = source.IndexOf("{{", scan, StringComparison.Ordinal); if (open < 0 || !ReadToken(source, open, out string token, out int after)) break;
            if (token.StartsWith('/') && !token.StartsWith("//"))
            {
                rawFrames.Pop();
                if (rawFrames.Count == 0) { body = source[contentStart..open]; position = after; return true; }
            }
            else if (rawFrames.Contains(true))
            {
                if ((token.StartsWith('#') || token.StartsWith(':')) && token != ":else") rawFrames.Push(true);
            }
            else if (TryClassifyBlockStart(token, out bool raw)) rawFrames.Push(raw);
            scan = after;
        }
        body = source[contentStart..]; position = source.Length; return false;
    }

    static bool TryClassifyBlockStart(string token, out bool raw)
    {
        raw = false;
        if (token.StartsWith("#if", StringComparison.Ordinal) || token.StartsWith("#when", StringComparison.Ordinal) || token == "#code") return true;
        if (token is "#pure" or "#puredisplay" or "#pure_display" || token.StartsWith("#escape", StringComparison.Ordinal) || token.StartsWith("#each", StringComparison.Ordinal)) { raw = true; return true; }
        if (token.StartsWith("#func", StringComparison.Ordinal) && token.Split(' ', StringSplitOptions.None).Length > 1) { raw = true; return true; }
        return false;
    }

    InlineResult ResolveInlineText(string text)
    {
        var output = new StringBuilder(); bool unknown = false; int position = 0;
        while (position < text.Length)
        {
            int open = text.IndexOf("{{", position, StringComparison.Ordinal);
            if (open < 0) { output.Append(text.AsSpan(position)); break; }
            output.Append(text.AsSpan(position, open - position));
            if (!ReadToken(text, open, out string token, out int after)) { output.Append(text.AsSpan(open)); unknown = true; break; }
            var resolved = ResolveInlineToken(token.Trim()); output.Append(resolved.Text); unknown |= resolved.Unknown; position = after;
        }
        return new(output.ToString(), unknown);
    }

    InlineResult ResolveInlineToken(string token)
    {
        if (token.StartsWith("call::", StringComparison.Ordinal))
        {
            string[] callArgs = token.Split("::", StringSplitOptions.None)[1..];
            if (callArgs.Length > 0 && functions.TryGetValue(callArgs[0], out var function))
            {
                if (++callDepth > 20) { callDepth--; return new("ERROR: Call stack limit reached", false); }
                try
                {
                    string data = function.Data;
                    for (int i = 0; i < callArgs.Length; i++) data = data.Replace($"{{{{arg::{i}}}}}", callArgs[i], StringComparison.Ordinal);
                    return new(EvaluateNested(data), false);
                }
                finally { callDepth--; }
            }
        }
        if (token.StartsWith("? ", StringComparison.Ordinal))
        {
            var expression = ResolveInlineText(token[2..]);
            if (expression.Unknown) return Preserve(token, "수식 CBS의 인수에 현재 프리셋만으로 정할 수 없는 값이 있습니다.");
            return RisuStaticCbs.TryCalculate(expression.Text, out string calculation) ? new(calculation, false) : Preserve(token, "수식 CBS를 계산하지 못했습니다.");
        }
        var pieces = SplitToken(token);
        string name = RisuStaticCbs.Normalize(pieces[0]);
        if (name == "getglobalvar")
        {
            var keyResult = pieces.Count > 1 ? ResolveInlineText(pieces[1]) : new InlineResult("", false);
            if (keyResult.Unknown) return Preserve(token, "전역 변수 이름을 현재 프리셋만으로 결정할 수 없습니다.");
            string key = keyResult.Text;
            if (TryToggleValue(key, out string value)) return new(value, false);
            if (LooksLikeToggleKey(key)) return new("null", false);
            warnings.Add($"전역 변수 '{key}'는 편집기에 런타임 값이 없어 원문으로 보존했습니다.");
            return new(PromptPreviewEngine.Marker("{{" + token + "}}", 'w'), true);
        }
        if (name == "jbtoggled") return new(jailbreak ? "1" : "0", false);
        if (name == "user" && pieces.Count == 1) return new(PromptPreviewEngine.Marker("{{user}}", 'u'), true);
        if (name == "char" && pieces.Count == 1) return new(PromptPreviewEngine.Marker("{{char}}", 'c'), true);
        if (name == "maxcontext" && pieces.Count == 1) return new(PromptPreviewEngine.Marker("{{maxcontext}}", 'm'), true);
        if (name is "roll" or "rollp" or "rollpick" or "dice" or "random" or "randint" or "pick")
            return new(PromptPreviewEngine.Marker("{{" + token + "}}", 'r'), true);
        if (name == "slot") return new("{{" + token + "}}", false);

        var args = new List<string>();
        for (int i = 1; i < pieces.Count; i++)
        {
            var resolved = ResolveInlineText(pieces[i]);
            if (resolved.Unknown) return Preserve(token, $"CBS '{{{{{name}}}}}'의 인수에 현재 프리셋만으로 정할 수 없는 값이 있습니다.");
            args.Add(resolved.Text);
        }
        if (RisuStaticCbs.TryEvaluate(name, args, out string result)) return new(result, false);
        if (RuntimeFunctions.Contains(name)) return Preserve(token, $"CBS '{{{{{name}}}}}'은 캐릭터·채팅·시간·모듈 같은 RisuAI 실행 상태가 필요합니다.");
        return Preserve(token, $"CBS '{{{{{name}}}}}'은 이 미리보기에서 아직 계산하지 못합니다.");
    }

    InlineResult Preserve(string token, string warning)
    {
        warnings.Add(warning); return new(PromptPreviewEngine.Marker("{{" + token + "}}", 'w'), true);
    }

    InlineResult PreserveSource(string source, string warning)
    {
        warnings.Add(warning); return new(PromptPreviewEngine.Marker(source, 'w'), true);
    }

    bool TryToggleValue(string key, out string value)
    {
        string normalized = key.Replace("\\_", "_", StringComparison.Ordinal);
        if (!normalized.StartsWith("toggle_", StringComparison.Ordinal)) { value = ""; return false; }
        return toggles.TryGetValue(normalized[7..], out value!);
    }

    static bool LooksLikeToggleKey(string key) => key.Replace("\\_", "_", StringComparison.Ordinal).StartsWith("toggle_", StringComparison.Ordinal);

    static List<string> SplitToken(string token)
    {
        var result = new List<string>(); int start = 0, depth = 0;
        for (int i = 0; i < token.Length; i++)
        {
            if (i + 1 < token.Length && token[i] == '{' && token[i + 1] == '{') { depth++; i++; continue; }
            if (i + 1 < token.Length && token[i] == '}' && token[i + 1] == '}') { depth = Math.Max(0, depth - 1); i++; continue; }
            if (depth == 0 && i + 1 < token.Length && token[i] == ':' && token[i + 1] == ':')
            {
                result.Add(token[start..i]); start = i + 2; i++;
            }
        }
        result.Add(token[start..]);
        if (result.Count == 1 && token.Contains(':'))
        {
            int separator = token.IndexOf(':'); result.Clear(); result.Add(token[..separator]); result.Add(token[(separator + 1)..]);
        }
        return result;
    }

    bool? EvaluateCondition(string token, bool unknown, out string mode)
    {
        mode = "normal"; if (unknown) return null;
        if (token.StartsWith("#when ")) return Truthy(StateAfterFirstSpace(token));
        if (!token.StartsWith("#when::")) return false;
        var statement = token.Split("::").Skip(1).ToList();
        if (statement.Count == 1) return Truthy(statement[0]);
        while (statement.Count > 1)
        {
            string condition = Pop(statement), op = Pop(statement);
            switch (op)
            {
                case "not": statement.Add(Truthy(condition) ? "0" : "1"); break;
                case "keep": mode = "keep"; statement.Add(condition); break;
                case "legacy": mode = "legacy"; statement.Add(condition); break;
                case "and": statement.Add(Truthy(condition) && Truthy(Pop(statement)) ? "1" : "0"); break;
                case "or": statement.Add(Truthy(condition) || Truthy(Pop(statement)) ? "1" : "0"); break;
                case "is": statement.Add(condition == Pop(statement) ? "1" : "0"); break;
                case "isnot": statement.Add(condition != Pop(statement) ? "1" : "0"); break;
                case "toggle": statement.Add(Truthy(Toggle(condition)) ? "1" : "0"); break;
                case "tis": statement.Add(Toggle(Pop(statement)) == condition ? "1" : "0"); break;
                case "tisnot": statement.Add(Toggle(Pop(statement)) != condition ? "1" : "0"); break;
                case ">": statement.Add(JsParseFloat(Pop(statement)) > JsParseFloat(condition) ? "1" : "0"); break;
                case "<": statement.Add(JsParseFloat(Pop(statement)) < JsParseFloat(condition) ? "1" : "0"); break;
                case ">=": statement.Add(JsParseFloat(Pop(statement)) >= JsParseFloat(condition) ? "1" : "0"); break;
                case "<=": statement.Add(JsParseFloat(Pop(statement)) <= JsParseFloat(condition) ? "1" : "0"); break;
                case "var": case "vis": case "visnot": return null;
                default: statement.Add(Truthy(condition) ? "1" : "0"); break;
            }
        }
        return statement.Count > 0 && Truthy(statement[0]);
    }

    string Toggle(string key) => toggles.TryGetValue(key, out string? value) ? value : "null";
    static string Pop(List<string> values) { if (values.Count == 0) return ""; string value = values[^1]; values.RemoveAt(values.Count - 1); return value; }
    static bool Truthy(string value) => value is "1" or "true";
    static string StateAfterFirstSpace(string token)
    {
        int first = token.IndexOf(' '); if (first < 0) return "";
        int second = token.IndexOf(' ', first + 1); return second < 0 ? token[(first + 1)..] : token[(first + 1)..second];
    }
    static bool IfTruthy(string token) => Truthy(StateAfterFirstSpace(token));
    static double JsParseFloat(string value)
    {
        Match match = Regex.Match(value, @"^\s*[+-]?(?:Infinity|(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)", RegexOptions.CultureInvariant);
        if (!match.Success) return double.NaN;
        string parsed = match.Value.Trim();
        if (parsed is "Infinity" or "+Infinity") return double.PositiveInfinity;
        if (parsed == "-Infinity") return double.NegativeInfinity;
        return double.TryParse(parsed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : double.NaN;
    }

    static string SelectWhenBody(string body, bool condition, bool keep)
    {
        var lines = body.Split('\n').ToList();
        if (lines.Count == 1)
        {
            int elseIndex = body.IndexOf("{{:else}}", StringComparison.Ordinal);
            if (elseIndex >= 0) return condition ? body[..elseIndex] : body[(elseIndex + 9)..];
            return condition ? body : "";
        }
        int elseLine = lines.FindIndex(line => line.Trim() == "{{:else}}");
        if (elseLine >= 0)
        {
            if (condition) lines.RemoveRange(elseLine, lines.Count - elseLine);
            else lines.RemoveRange(0, elseLine + 1);
        }
        else if (!condition) return "";
        if (!keep)
        {
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        }
        return string.Join('\n', lines);
    }

    static string NormalizeCode(string body)
    {
        string value = body.Trim().Replace("\n", "", StringComparison.Ordinal).Replace("\t", "", StringComparison.Ordinal);
        value = Regex.Replace(value, @"\\u([0-9A-Fa-f]{4})", match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
        return Regex.Replace(value, @"\\(.)", match => match.Groups[1].Value switch
        {
            "n" => "\n", "r" => "\r", "t" => "\t", "b" => "\b", "f" => "\f", "v" => "\v", "a" => "a", "x" => "\0", _ => match.Groups[1].Value
        });
    }

    static string TrimLines(string value) => string.Join('\n', value.Trim().Split('\n').Select(line => line.TrimStart()));
}

public sealed partial class MainWindow
{
    sealed record PreviewRun(int Start, int Length, Run Run);
    sealed class PreviewTextSurface(RichTextBox box, string text, List<PreviewRun> runs)
    {
        public RichTextBox Box { get; } = box;
        public string Text { get; } = text;
        public List<PreviewRun> Runs { get; } = runs;
        public TextPointer? PointerAt(int offset)
        {
            var span = Runs.FirstOrDefault(r => offset >= r.Start && offset <= r.Start + r.Length);
            return span?.Run.ContentStart.GetPositionAtOffset(Math.Clamp(offset - span.Start, 0, span.Length), LogicalDirection.Forward);
        }
    }
    sealed class ReferenceTabView(StackPanel editorRoot, ScrollViewer editorScroll, StackPanel previewRoot, ScrollViewer previewScroll, Grid root)
    {
        public StackPanel EditorRoot { get; } = editorRoot;
        public ScrollViewer EditorScroll { get; } = editorScroll;
        public StackPanel PreviewRoot { get; } = previewRoot;
        public ScrollViewer PreviewScroll { get; } = previewScroll;
        public Grid Root { get; } = root;
        public List<PreviewTextSurface> TextSurfaces { get; } = new();
        public List<(string Text, FrameworkElement Element)> AuxiliaryText { get; } = new();
    }
    readonly List<PreviewTextSurface> previewTextSurfaces = new();
    readonly List<(string Text, FrameworkElement Element)> previewAuxiliaryText = new();

    void RenderPromptPreview(PromptPreviewResult result)
        => RenderPromptPreview(promptPreview, previewTextSurfaces, previewAuxiliaryText, result);

    void RenderPromptPreview(StackPanel target, List<PreviewTextSurface> textSurfaces, List<(string Text, FrameworkElement Element)> auxiliaryText, PromptPreviewResult result)
    {
        target.Children.Clear(); textSurfaces.Clear(); auxiliaryText.Clear();
        if (result.Entries.Count == 0)
        {
            target.Children.Add(Card(Text("표시할 프롬프트 메시지가 없습니다.", 13, Muted))); return;
        }
        foreach (var entry in result.Entries)
        {
            var body = new StackPanel();
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var role = Text(entry.Kind == PromptPreviewEntryKind.Runtime ? "RUNTIME · " + entry.Role : entry.Role.ToUpperInvariant(), 11,
                entry.Kind == PromptPreviewEntryKind.Runtime ? new SolidColorBrush(Color.FromRgb(151, 91, 18)) : RoleBrush(entry.Role));
            role.FontWeight = FontWeights.SemiBold; header.Children.Add(role); auxiliaryText.Add((role.Text, role));
            if (entry.CachePoint)
            {
                var cache = Text("CACHE POINT", 10, Accent); DockPanel.SetDock(cache, Dock.Right); header.Children.Add(cache); auxiliaryText.Add((cache.Text, cache));
            }
            body.Children.Add(header);
            var document = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily("Consolas, Malgun Gothic"), FontSize = 13, Foreground = Ink };
            var paragraph = new Paragraph { Margin = new Thickness(0) }; document.Blocks.Add(paragraph);
            var content = new RichTextBox { Document = document, IsReadOnly = true, IsDocumentEnabled = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MinHeight = 22, Cursor = Cursors.IBeam };
            textSurfaces.Add(AddPreviewInlines(content, paragraph, entry.Content)); body.Children.Add(content);
            if (!string.IsNullOrEmpty(entry.Note)) { var note = Text(entry.Note, 10, Muted); note.Margin = new Thickness(0, 7, 0, 0); body.Children.Add(note); auxiliaryText.Add((note.Text, note)); }
            target.Children.Add(new Border
            {
                Background = entry.Kind == PromptPreviewEntryKind.Runtime ? new SolidColorBrush(Color.FromRgb(255, 250, 235)) : Brushes.White,
                BorderBrush = entry.Kind == PromptPreviewEntryKind.Runtime ? new SolidColorBrush(Color.FromRgb(235, 201, 133)) : Line,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 8, 10), Child = body
            });
        }
    }

    static Brush RoleBrush(string role) => role switch
    {
        "user" => new SolidColorBrush(Color.FromRgb(37, 99, 235)),
        "assistant" => new SolidColorBrush(Color.FromRgb(147, 51, 234)),
        _ => new SolidColorBrush(Color.FromRgb(15, 118, 110))
    };

    static PreviewTextSurface AddPreviewInlines(RichTextBox box, Paragraph target, string text)
    {
        int position = 0, visiblePosition = 0; var runs = new List<PreviewRun>(); var visible = new StringBuilder();
        void AddRun(string value, Brush? foreground = null, Brush? background = null, string? tip = null, FontStyle? style = null)
        {
            var run = new Run(value) { Foreground = foreground ?? Ink, Background = background ?? Brushes.Transparent };
            if (style is { } fontStyle) run.FontStyle = fontStyle; if (tip is not null) run.ToolTip = tip;
            target.Inlines.Add(run); runs.Add(new(visiblePosition, value.Length, run)); visible.Append(value); visiblePosition += value.Length;
        }
        while (position < text.Length)
        {
            int start = text.IndexOf(PromptPreviewEngine.MarkerStart, position);
            if (start < 0) { AddRun(text[position..]); break; }
            if (start > position) AddRun(text[position..start]);
            int end = text.IndexOf(PromptPreviewEngine.MarkerEnd, start + 1);
            if (end < 0) { AddRun(text[start..]); break; }
            string encoded = text[(start + 1)..end]; char kind = encoded.Length > 0 ? encoded[0] : 's'; string label = encoded.Length > 0 ? encoded[1..] : "";
            switch (kind)
            {
                case 'u': AddRun(label, new SolidColorBrush(Color.FromRgb(220, 38, 38))); break;
                case 'c': AddRun(label, new SolidColorBrush(Color.FromRgb(37, 99, 235))); break;
                case 'm': AddRun(label, new SolidColorBrush(Color.FromRgb(202, 138, 4))); break;
                case 'r': AddRun(label, new SolidColorBrush(Color.FromRgb(22, 163, 74))); break;
                case 'w': AddRun(label, new SolidColorBrush(Color.FromRgb(194, 65, 12)), new SolidColorBrush(Color.FromRgb(255, 247, 237)), "현재 프리셋만으로 값을 결정할 수 없어 원문을 표시했습니다."); break;
                default: AddRun(label, new SolidColorBrush(Color.FromRgb(13, 110, 102)), new SolidColorBrush(Color.FromRgb(230, 247, 244)), style: FontStyles.Italic); break;
            }
            position = end + 1;
        }
        return new PreviewTextSurface(box, visible.ToString(), runs);
    }
}

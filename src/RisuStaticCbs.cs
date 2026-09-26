using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RisupEditor;

/// <summary>RisuAI CBS functions whose result is determined by the prompt text alone.</summary>
internal static class RisuStaticCbs
{
    static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = false };

    internal static bool TryEvaluate(string rawName, IReadOnlyList<string> args, out string result)
    {
        string name = Normalize(rawName);
        string A(int index) => index < args.Count ? args[index] : "";
        string Bool(bool value) => value ? "1" : "0";
        try
        {
            switch (name)
            {
                case "blank": case "none": case "hiddenkey": case "comment": case "//": result = ""; return true;
                case "br": case "newline": result = "\n"; return true;
                case "cbr": case "cnl": case "cnewline": { if (args.Count == 0) result = "\\n"; else { int count = Math.Max(1, (int)JsNumber(A(0))); result = string.Concat(Enumerable.Repeat(rawName + "::" + string.Join("::", args), count)); } return true; }
                case "equal": result = Bool(A(0) == A(1)); return true;
                case "notequal": case "not_equal": result = Bool(A(0) != A(1)); return true;
                case "greater": result = Bool(JsNumber(A(0)) > JsNumber(A(1))); return true;
                case "less": result = Bool(JsNumber(A(0)) < JsNumber(A(1))); return true;
                case "greaterequal": case "greater_equal": result = Bool(JsNumber(A(0)) >= JsNumber(A(1))); return true;
                case "lessequal": case "less_equal": result = Bool(JsNumber(A(0)) <= JsNumber(A(1))); return true;
                case "and": result = Bool(A(0) == "1" && A(1) == "1"); return true;
                case "or": result = Bool(A(0) == "1" || A(1) == "1"); return true;
                case "not": result = Bool(A(0) != "1"); return true;
                case "startswith": result = Bool(A(0).StartsWith(A(1), StringComparison.Ordinal)); return true;
                case "endswith": result = Bool(A(0).EndsWith(A(1), StringComparison.Ordinal)); return true;
                case "contains": result = Bool(A(0).Contains(A(1), StringComparison.Ordinal)); return true;
                case "replace": result = A(1).Length == 0 ? A(0) : A(0).Replace(A(1), A(2), StringComparison.Ordinal); return true;
                case "trim": result = A(0).Trim(); return true;
                case "length": result = A(0).Length.ToString(CultureInfo.InvariantCulture); return true;
                case "lower": result = A(0).ToLower(CultureInfo.CurrentCulture); return true;
                case "upper": result = A(0).ToUpper(CultureInfo.CurrentCulture); return true;
                case "capitalize": result = A(0).Length == 0 ? "" : A(0)[..1].ToUpper(CultureInfo.CurrentCulture) + A(0)[1..]; return true;
                case "split": result = MakeArray(A(1).Length == 0 ? A(0).Select(c => c.ToString()) : A(0).Split(A(1))); return true;
                case "join": result = string.Join(A(1), ParseArrayStrings(A(0))); return true;
                case "spread": result = string.Join("::", ParseArrayStrings(A(0))); return true;
                case "calc": result = Calculate(A(0)); return true;
                case "round": result = JsNumberText(Math.Floor(JsNumber(A(0)) + 0.5)); return true;
                case "floor": result = JsNumberText(Math.Floor(JsNumber(A(0)))); return true;
                case "ceil": result = JsNumberText(Math.Ceiling(JsNumber(A(0)))); return true;
                case "abs": result = JsNumberText(Math.Abs(JsNumber(A(0)))); return true;
                case "remaind": result = JsNumberText(JsNumber(A(0)) % JsNumber(A(1))); return true;
                case "pow": result = JsNumberText(Math.Pow(JsNumber(A(0)), JsNumber(A(1)))); return true;
                case "tonumber": result = string.Concat(A(0).Where(c => char.IsDigit(c) || c == '.')); return true;
                case "fromhex": result = long.TryParse(A(0), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long fromHex) ? fromHex.ToString(CultureInfo.InvariantCulture) : "NaN"; return true;
                case "tohex": result = long.TryParse(A(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out long toHex) ? toHex.ToString("x", CultureInfo.InvariantCulture) : "NaN"; return true;
                case "fixnum": result = JsNumber(A(0)).ToString("F" + Math.Clamp((int)JsNumber(A(1)), 0, 100), CultureInfo.InvariantCulture); return true;
                case "arraylength": result = ParseArray(A(0)).Count.ToString(CultureInfo.InvariantCulture); return true;
                case "arrayelement": result = ArrayElement(A(0), (int)JsNumber(A(1))); return true;
                case "dictelement": case "objectelement": result = DictionaryElement(A(0), A(1)); return true;
                case "element": case "ele": result = DeepElement(args); return true;
                case "arrayshift": { var a = ParseArray(A(0)); if (a.Count > 0) a.RemoveAt(0); result = ArrayJson(a); return true; }
                case "arraypop": { var a = ParseArray(A(0)); if (a.Count > 0) a.RemoveAt(a.Count - 1); result = ArrayJson(a); return true; }
                case "arraypush": { var a = ParseArray(A(0)); a.Add(A(1)); result = ArrayJson(a); return true; }
                case "arraysplice": result = ArraySplice(args); return true;
                case "arrayassert": result = ArrayAssert(args); return true;
                case "makearray": case "array": case "a": result = MakeArray(args); return true;
                case "makedict": case "dict": case "d": case "makeobject": case "object": case "o": result = MakeDictionary(args); return true;
                case "objectassert": case "dictassert": case "object_assert": result = ObjectAssert(args); return true;
                case "range": result = Range(A(0)); return true;
                case "filter": result = Filter(A(0), A(1)); return true;
                case "all": result = Bool(Values(args).All(v => v == "1")); return true;
                case "any": result = Bool(Values(args).Any(v => v == "1")); return true;
                case "min": result = JsNumberText(Values(args).Select(NumberOrZero).DefaultIfEmpty(double.PositiveInfinity).Min()); return true;
                case "max": result = JsNumberText(Values(args).Select(NumberOrZero).DefaultIfEmpty(double.NegativeInfinity).Max()); return true;
                case "sum": result = JsNumberText(Values(args).Sum(NumberOrZero)); return true;
                case "average": { var values = Values(args); result = JsNumberText(values.Count == 0 ? double.NaN : values.Sum(NumberOrZero) / values.Count); return true; }
                case "unicodeencode": case "unicode_encode": { int i = A(1).Length == 0 ? 0 : (int)JsNumber(A(1)); result = i >= 0 && i < A(0).Length ? ((int)A(0)[i]).ToString(CultureInfo.InvariantCulture) : "NaN"; return true; }
                case "unicodedecode": case "unicode_decode": result = ((char)(int)JsNumber(A(0))).ToString(); return true;
                case "u": case "ue": case "unicodedecodefromhex": case "unicodeencodefromhex": result = int.TryParse(A(0), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp) ? ((char)cp).ToString() : "\0"; return true;
                case "iserror": result = Bool(A(0).StartsWith("error:", StringComparison.CurrentCultureIgnoreCase)); return true;
                case "xor": case "xorencrypt": case "xorencode": case "xore": result = XorEncode(A(0)); return true;
                case "xordecrypt": case "xordecode": case "xord": result = XorDecode(A(0)); return true;
                case "reverse": result = string.Concat(A(0).EnumerateRunes().Reverse()); return true;
                case "crypt": case "crypto": case "caesar": case "encrypt": case "decrypt": result = Crypt(A(0), A(1)); return true;
                case "tex": case "latex": case "katex": result = "$$" + A(0) + "$$"; return true;
                case "ruby": case "furigana": result = $"<ruby>{A(0)}<rp> (</rp><rt>{A(1)}</rt><rp>) </rp></ruby>"; return true;
                case "codeblock": result = CodeBlock(args); return true;
                case "decbo": case "displayescapedcurlybracketopen": result = "{"; return true;
                case "decbc": case "displayescapedcurlybracketclose": result = "}"; return true;
                case "bo": case "ddecbo": case "doubledisplayescapedcurlybracketopen": result = "{{"; return true;
                case "bc": case "ddecbc": case "doubledisplayescapedcurlybracketclose": result = "}}"; return true;
                case "displayescapedbracketopen": case "debo": case "(": result = "("; return true;
                case "displayescapedbracketclose": case "debc": case ")": result = ")"; return true;
                case "displayescapedanglebracketopen": case "deabo": case "<": result = "<"; return true;
                case "displayescapedanglebracketclose": case "deabc": case ">": result = ">"; return true;
                case "displayescapedcolon": case "dec": case ":": result = ":"; return true;
                case "displayescapedsemicolon": case ";": result = ";"; return true;
                default: result = ""; return false;
            }
        }
        catch
        {
            // RisuAI catches malformed function input at parser level. Keep it visible instead of inventing a value.
            result = ""; return false;
        }
    }

    internal static string Normalize(string name) => string.Concat(name.Trim().ToLowerInvariant().Where(c => c is not (' ' or '_' or '-')));

    internal static bool TryCalculate(string expression, out string result)
    {
        try { result = Calculate(expression); return true; }
        catch { result = ""; return false; }
    }

    internal static bool TryParseArrayValues(string source, out List<string> values)
    {
        try
        {
            if (JsonNode.Parse(source) is JsonArray array) values = array.Select(NodeText).ToList();
            else values = source.Split('§').ToList();
        }
        catch { values = source.Split('§').ToList(); }
        return true;
    }

    static double JsNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        if (value.Equals("Infinity", StringComparison.OrdinalIgnoreCase)) return double.PositiveInfinity;
        if (value.Equals("-Infinity", StringComparison.OrdinalIgnoreCase)) return double.NegativeInfinity;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : double.NaN;
    }

    static double NumberOrZero(string value) { double number = JsNumber(value); return double.IsNaN(number) ? 0 : number; }
    static string JsNumberText(double value) => double.IsNaN(value) ? "NaN" : double.IsPositiveInfinity(value) ? "Infinity" : double.IsNegativeInfinity(value) ? "-Infinity" : value == 0 ? "0" : value.ToString("G15", CultureInfo.InvariantCulture);
    static string Json(JsonNode? node) => node?.ToJsonString(JsonOptions) ?? "null";
    static JsonArray ParseArray(string value) => JsonNode.Parse(value) as JsonArray ?? [];
    static JsonObject ParseObject(string value) => JsonNode.Parse(value) as JsonObject ?? [];
    static string NodeText(JsonNode? node) => node is null ? "null" : node is JsonValue value && value.TryGetValue<string>(out string? text) ? text ?? "null" : Json(node);
    static List<string> ParseArrayStrings(string value) => ParseArray(value).Select(NodeText).ToList();
    static string MakeArray(IEnumerable<string> values) { var array = new JsonArray(); foreach (string value in values) array.Add(value.Replace("::", "\\u003A\\u003A", StringComparison.Ordinal)); return Json(array); }
    static string ArrayJson(JsonArray array)
    {
        for (int i = 0; i < array.Count; i++) if (array[i] is JsonValue value && value.TryGetValue<string>(out string? text)) array[i] = (text ?? "").Replace("::", "\\u003A\\u003A", StringComparison.Ordinal);
        return Json(array);
    }

    static string ArrayElement(string source, int index)
    {
        var array = ParseArray(source); if (index < 0) index += array.Count;
        return index >= 0 && index < array.Count ? NodeText(array[index]) : "null";
    }

    static string DictionaryElement(string source, string key)
    {
        var obj = ParseObject(source); return obj.TryGetPropertyValue(key, out JsonNode? value) ? NodeText(value) : "null";
    }

    static string DeepElement(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return "null"; JsonNode? current = JsonNode.Parse(args[0]);
        for (int i = 1; i < args.Count; i++)
        {
            current = current switch { JsonObject o when o.TryGetPropertyValue(args[i], out JsonNode? v) => v, JsonArray a when int.TryParse(args[i], out int n) && n >= 0 && n < a.Count => a[n], _ => null };
            if (current is null) return "null";
        }
        return NodeText(current);
    }

    static string ArraySplice(IReadOnlyList<string> args)
    {
        var array = ParseArray(args.ElementAtOrDefault(0) ?? "[]"); int start = (int)JsNumber(args.ElementAtOrDefault(1) ?? "0"); if (start < 0) start = Math.Max(0, array.Count + start); start = Math.Min(start, array.Count);
        int count = Math.Max(0, (int)JsNumber(args.ElementAtOrDefault(2) ?? "0")); for (int i = 0; i < count && start < array.Count; i++) array.RemoveAt(start);
        if (args.Count > 3) array.Insert(start, args[3]); return ArrayJson(array);
    }

    static string ArrayAssert(IReadOnlyList<string> args)
    {
        var array = ParseArray(args.ElementAtOrDefault(0) ?? "[]"); int index = Math.Max(0, (int)JsNumber(args.ElementAtOrDefault(1) ?? "0"));
        if (index >= array.Count) { while (array.Count < index) array.Add(null); array.Add(args.ElementAtOrDefault(2) ?? ""); }
        return ArrayJson(array);
    }

    static string MakeDictionary(IReadOnlyList<string> args)
    {
        var obj = new JsonObject(); foreach (string arg in args) { int equal = arg.IndexOf('='); if (equal >= 0) obj[arg[..equal]] = arg[(equal + 1)..]; } return Json(obj);
    }

    static string ObjectAssert(IReadOnlyList<string> args)
    {
        var obj = ParseObject(args.ElementAtOrDefault(0) ?? "{}"); string key = args.ElementAtOrDefault(1) ?? "";
        if (!obj.TryGetPropertyValue(key, out JsonNode? value) || value is null || NodeText(value) is "" or "0" or "false") obj[key] = args.ElementAtOrDefault(2) ?? ""; return Json(obj);
    }

    static string Range(string source)
    {
        var values = ParseArrayStrings(source).Select(JsNumber).ToList(); double start = values.Count > 1 ? values[0] : 0, end = values.Count > 1 ? values[1] : values.ElementAtOrDefault(0), step = values.Count > 2 ? values[2] : 1;
        var result = new List<string>(); if (step > 0) for (double i = start; i < end && result.Count < 100000; i += step) result.Add(JsNumberText(i)); return MakeArray(result);
    }

    static string Filter(string source, string kind)
    {
        var values = ParseArrayStrings(source); var output = values.Where((value, index) => kind switch { "nonempty" => value != "", "unique" => values.IndexOf(value) == index, _ => value != "" && values.IndexOf(value) == index }); return MakeArray(output);
    }

    static List<string> Values(IReadOnlyList<string> args) => args.Count > 1 ? args.ToList() : args.Count == 1 ? ParseArrayStrings(args[0]) : [];
    static string Crypt(string source, string shiftText) { int shift = int.TryParse(shiftText, out int n) ? n : 32768; var output = new StringBuilder(source.Length); foreach (char c in source) output.Append((char)((c + shift) & 0xffff)); return output.ToString(); }
    static string CodeBlock(IReadOnlyList<string> args) { string code = (args.LastOrDefault() ?? "").Replace("\"", "&quot;").Replace("'", "&#39;").Replace("<", "&lt;").Replace(">", "&gt;"); return args.Count > 1 ? $"<pre-hljs-placeholder lang=\"{args[0]}\">{code}</pre-hljs-placeholder>" : $"<pre><code>{code}</code></pre>"; }
    static string XorEncode(string source) { byte[] bytes = Encoding.UTF8.GetBytes(source); for (int i = 0; i < bytes.Length; i++) bytes[i] ^= 0xff; return Convert.ToBase64String(bytes); }
    static string XorDecode(string source) { byte[] bytes = Convert.FromBase64String(source); for (int i = 0; i < bytes.Length; i++) bytes[i] ^= 0xff; return Encoding.UTF8.GetString(bytes); }

    static string Calculate(string expression)
    {
        expression = expression.Replace("&&", "&", StringComparison.Ordinal).Replace("||", "|", StringComparison.Ordinal)
            .Replace("<=", "≤", StringComparison.Ordinal).Replace(">=", "≥", StringComparison.Ordinal)
            .Replace("==", "=", StringComparison.Ordinal).Replace("!=", "≠", StringComparison.Ordinal)
            .Replace("null", "0", StringComparison.OrdinalIgnoreCase);
        int position = 0; double value = ParseComparison(); Skip(); if (position != expression.Length) throw new FormatException(); return JsNumberText(value);

        double ParseComparison()
        {
            double left = ParseAdd();
            while (true)
            {
                Skip(); if (position >= expression.Length || "<>|&≤≥=≠".IndexOf(expression[position]) < 0) return left;
                char op = expression[position++]; double right = ParseAdd();
                left = op switch { '<' => left < right ? 1 : 0, '>' => left > right ? 1 : 0, '|' => left != 0 ? left : right, '&' => left != 0 ? right : left, '≤' => left <= right ? 1 : 0, '≥' => left >= right ? 1 : 0, '=' => left == right ? 1 : 0, '≠' => left != right ? 1 : 0, _ => left };
            }
        }
        double ParseAdd() { double left = ParseMultiply(); while (true) { Skip(); if (Take('+')) left += ParseMultiply(); else if (Take('-')) left -= ParseMultiply(); else return left; } }
        double ParseMultiply() { double left = ParsePower(); while (true) { Skip(); if (Take('*')) left *= ParsePower(); else if (Take('/')) left /= ParsePower(); else if (Take('%')) left %= ParsePower(); else return left; } }
        double ParsePower() { double left = ParseUnary(); while (true) { Skip(); if (Take('^')) left = Math.Pow(left, ParseUnary()); else return left; } }
        double ParseUnary() { Skip(); if (Take('!')) return ParseUnary() == 0 ? 1 : 0; if (Take('-')) return -ParseUnary(); if (Take('+')) return ParseUnary(); return ParsePrimary(); }
        double ParsePrimary()
        {
            Skip(); if (Take('(')) { double value = ParseComparison(); Skip(); if (!Take(')')) throw new FormatException(); return value; }
            int start = position; while (position < expression.Length && (char.IsDigit(expression[position]) || expression[position] is '.' or 'e' or 'E' || ((expression[position] is '+' or '-') && position > start && expression[position - 1] is 'e' or 'E'))) position++;
            if (start == position || !double.TryParse(expression[start..position], NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) throw new FormatException(); return number;
        }
        void Skip() { while (position < expression.Length && char.IsWhiteSpace(expression[position])) position++; }
        bool Take(char token) { if (position < expression.Length && expression[position] == token) { position++; return true; } return false; }
    }
}

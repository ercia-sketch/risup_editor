using System.Text.RegularExpressions;

namespace RisupEditor;

public record SyntaxDiagnostic(int Start, int Length, string Message, bool Warning = false);
public static class SyntaxDiagnostics
{
    record Block(int Start, int Length, string Name, bool Literal);
    public static IReadOnlyList<SyntaxDiagnostic> Analyze(string text, bool additional)
    {
        var result = new List<SyntaxDiagnostic>();
        var braces = new Stack<(int Start, bool Legacy)>();
        var blocks = new Stack<Block>();
        for (int i = 0; i + 1 < text.Length; i++)
        {
            if (text[i] == '{' && text[i + 1] is '{' or '#') { braces.Push((i, text[i + 1] == '#')); i++; continue; }
            if ((text[i] == '}' || text[i] == '#') && text[i + 1] == '}')
            {
                if (braces.Count == 0)
                {
                    if (additional && text[i] == '}') result.Add(new(i, 2, "이 필드에서 대응하는 시작 기호를 찾지 못했습니다. 일반 텍스트일 수 있습니다.", true));
                    i++; continue;
                }
                var open = braces.Pop(); int end = i + 2;
                if (braces.Count == 0 && text[i] == '}') Process(text.Substring(open.Start + 2, i - open.Start - 2), open.Start, end - open.Start);
                i++;
            }
        }
        foreach (var brace in braces) result.Add(new(brace.Start, 2, "시작 기호에 대응하는 CBS 종료 기호가 없습니다."));
        foreach (var block in blocks) result.Add(new(block.Start, block.Length, "이 CBS 블록을 닫는 {{/…}} 구문을 찾지 못했습니다."));
        if (additional) CheckTags(text, result);
        return result.OrderBy(d => d.Start).ToArray();

        void Process(string token, int start, int length)
        {
            // Parser deliberately does not Trim(), and accepts any single-slash closing name.
            if (token.StartsWith('/') && !token.StartsWith("//"))
            {
                if (blocks.Count > 0) blocks.Pop();
                else if (additional) result.Add(new(start, length, "이 필드에서 닫을 CBS 블록을 찾지 못했습니다. 원문으로 남는 표현일 수 있습니다.", true));
                return;
            }
            if (token == ":else")
            {
                if (additional && !blocks.Any(b => b.Name.StartsWith("#when"))) result.Add(new(start, length, "대응하는 #when을 찾지 못했습니다. #if는 #when과 else 처리 방식이 다릅니다.", true));
                return;
            }
            bool raw = blocks.Any(b => b.Literal);
            bool known = token.StartsWith("#if") || token.StartsWith("#when") || token.StartsWith("#each") || token.StartsWith("#escape") || token.StartsWith("#func ") || token is "#pure" or "#pure_display" or "#puredisplay" or "#code";
            if (known || (raw && (token.StartsWith('#') || token.StartsWith(':'))))
            {
                bool literal = raw || token is "#pure" or "#pure_display" or "#puredisplay" || token.StartsWith("#escape") || token.StartsWith("#each") || token.StartsWith("#func ");
                blocks.Push(new(start, length, token, literal));
            }
        }
    }
    static void CheckTags(string text, List<SyntaxDiagnostic> result)
    {
        var stack = new List<(string Name, int Start, int Length)>();
        var voids = new HashSet<string>("area base br col embed hr img input link meta param source track wbr user char bot".Split(' '), StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(text, @"<!--[\s\S]*?-->|</?([A-Za-z_][\w:.-]*)(?:\s+(?:[^>""']|""[^""]*""|'[^']*')*)?\s*/?>", RegexOptions.NonBacktracking))
        {
            if (m.Value.StartsWith("<!--")) continue;
            string name = m.Groups[1].Value;
            if (voids.Contains(name) || m.Value.EndsWith("/>")) continue;
            if (!m.Value.StartsWith("</")) { stack.Add((name, m.Index, m.Length)); continue; }
            int index = stack.FindLastIndex(t => t.Name == name);
            if (index < 0) result.Add(new(m.Index, m.Length, "이 필드에서 대응하는 여는 태그를 찾지 못했습니다. 의도된 표현일 수 있습니다.", true));
            else
            {
                if (index != stack.Count - 1) result.Add(new(m.Index, m.Length, "태그 중첩 순서를 확인하세요. 다른 블록에서 이어지는 표현일 수 있습니다.", true));
                stack.RemoveAt(index);
            }
        }
        foreach (var tag in stack) result.Add(new(tag.Start, tag.Length, "이 필드에서 대응하는 닫는 태그를 찾지 못했습니다. 다른 블록에서 닫거나 의도적으로 열어 둘 수 있습니다.", true));
    }
}

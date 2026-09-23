namespace RisupEditor;
public static class DiagnosticTests
{
    public static void Run()
    {
        void Clean(string text) { if (SyntaxDiagnostics.Analyze(text, false).Count != 0) throw new Exception("Unexpected diagnostic: " + text); }
        Clean("{{#if {{getvar::x}}}}text{{/anything}}"); Clean("{{unknown::a}}"); Clean("{#legacy#}"); Clean("{{#pure}}literal{{/}}"); Clean("<open>"); Clean("}} {{/}}");
        if (SyntaxDiagnostics.Analyze("{{char", false).Count != 1) throw new Exception("missing delimiter");
        if (SyntaxDiagnostics.Analyze("{{#when::1}}", false).Count != 1) throw new Exception("missing block close");
        if (SyntaxDiagnostics.Analyze("<open>", true).Count != 1) throw new Exception("missing tag warning");
        if (SyntaxDiagnostics.Analyze("<br><img/><user><char><bot><x></x>", true).Count != 0) throw new Exception("tag exceptions");
        if (SyntaxDiagnostics.Analyze("}} {{/}}", true).Any(d => !d.Warning)) throw new Exception("literal treated as error");
        var timer = System.Diagnostics.Stopwatch.StartNew(); SyntaxDiagnostics.Analyze(new string('a', 100000) + "<tag " + new string(' ', 10000), true);
        if (timer.Elapsed.TotalSeconds > 3) throw new Exception("slow diagnostics");
    }
}

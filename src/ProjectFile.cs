using System.IO;
using System.IO.Compression;
using System.Text;

namespace RisupEditor;

public sealed class EditorProject
{
    public List<Preset> References { get; } = new();
    public List<Value> Blocks { get; } = new();
    public Preset? Basis { get; set; }
    public string ToggleText { get; set; } = "";
    public Dictionary<string, string> ToggleValues { get; } = new();
    public HashSet<int> CollapsedWork { get; } = new();
    public List<HashSet<int>> CollapsedReferences { get; } = new();
    public int SelectedTab { get; set; } = -1;
    public int SelectedBlock { get; set; } = -1;
    public bool Preview { get; set; }
    public bool AdditionalChecks { get; set; }
    public double[] Ratios { get; set; } = [4, 4, 2];
}

public static class ProjectFile
{
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("RISUPPROJ1");

    public static void Save(EditorProject state, string path)
    {
        var root = Value.Map();
        root.Set("version", Value.Int(1));
        root.Set("references", Value.Array(state.References.Select(PresetValue)));
        root.Set("blocks", Value.Array(state.Blocks.Select(v => v.Clone())));
        if (state.Basis is not null) root.Set("basis", PresetValue(state.Basis));
        root.Set("toggleText", Value.String(state.ToggleText));
        var toggleValues = Value.Map();
        foreach (var pair in state.ToggleValues) toggleValues.Set(pair.Key, Value.String(pair.Value));
        root.Set("toggleValues", toggleValues);
        root.Set("collapsedWork", Value.Array(state.CollapsedWork.Order().Select(i => Value.Int(i))));
        root.Set("collapsedReferences", Value.Array(state.CollapsedReferences.Select(set => Value.Array(set.Order().Select(i => Value.Int(i))))));
        root.Set("selectedTab", Value.Int(state.SelectedTab));
        root.Set("selectedBlock", Value.Int(state.SelectedBlock));
        root.Set("preview", Value.Bool(state.Preview));
        root.Set("additionalChecks", Value.Bool(state.AdditionalChecks));
        root.Set("ratios", Value.Array(state.Ratios.Select(r => Value.Int((long)Math.Round(r * 1000)))));

        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(Magic);
                using (var zip = new GZipStream(file, CompressionLevel.Optimal, true))
                    zip.Write(root.Encode());
                file.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static EditorProject Load(string path)
    {
        if (new FileInfo(path).Length > 128 * 1024 * 1024) throw new InvalidDataException("프로젝트 파일이 128MB를 초과합니다.");
        using var file = File.OpenRead(path);
        var magic = new byte[Magic.Length];
        if (file.Read(magic) != magic.Length || !magic.SequenceEqual(Magic)) throw new InvalidDataException("Risup Editor 프로젝트 파일이 아닙니다.");
        using var zip = new GZipStream(file, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = zip.Read(buffer)) > 0)
        {
            if (decoded.Length + read > 256L * 1024 * 1024) throw new InvalidDataException("프로젝트 압축 해제 크기가 256MB를 초과합니다.");
            decoded.Write(buffer, 0, read);
        }
        var root = Value.Parse(decoded.ToArray());
        if (root.Get("version")?.Number() != 1) throw new InvalidDataException("지원하지 않는 프로젝트 버전입니다.");

        var state = new EditorProject
        {
            Basis = root.Get("basis") is { } b ? ReadPreset(b) : null,
            ToggleText = root.Str("toggleText"),
            SelectedTab = (int)(root.Get("selectedTab")?.Number() ?? -1),
            SelectedBlock = (int)(root.Get("selectedBlock")?.Number() ?? -1),
            Preview = root.Get("preview")?.Boolean() ?? false,
            AdditionalChecks = root.Get("additionalChecks")?.Boolean() ?? false
        };
        foreach (var item in root.Get("references")?.Items ?? []) state.References.Add(ReadPreset(item));
        foreach (var item in root.Get("blocks")?.Items ?? []) state.Blocks.Add(item.Clone());
        foreach (var pair in root.Get("toggleValues")?.Fields ?? []) if (pair.Key.Text() is { } key) state.ToggleValues[key] = pair.Item.Text() ?? "";
        foreach (var item in root.Get("collapsedWork")?.Items ?? []) if (item.Number() is { } i) state.CollapsedWork.Add((int)i);
        foreach (var set in root.Get("collapsedReferences")?.Items ?? []) state.CollapsedReferences.Add((set.Items ?? []).Select(v => (int)(v.Number() ?? -1)).Where(i => i >= 0).ToHashSet());
        var ratios = root.Get("ratios")?.Items?.Select(v => (v.Number() ?? 0) / 1000d).ToArray();
        if (ratios is { Length: 3 } && ratios.All(v => v > 0)) state.Ratios = ratios;
        while (state.CollapsedReferences.Count < state.References.Count) state.CollapsedReferences.Add(new());
        return state;
    }

    static Value PresetValue(Preset preset)
    {
        var value = Value.Map();
        value.Set("path", Value.String(preset.Path));
        value.Set("envelope", preset.Envelope.Clone());
        value.Set("data", preset.Data.Clone());
        return value;
    }

    static Preset ReadPreset(Value value) => new()
    {
        Path = value.Str("path"),
        Envelope = value.Get("envelope")?.Clone() ?? throw new InvalidDataException("프로젝트의 프리셋 컨테이너가 없습니다."),
        Data = value.Get("data")?.Clone() ?? throw new InvalidDataException("프로젝트의 프리셋 데이터가 없습니다.")
    };
}

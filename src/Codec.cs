using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace RisupEditor;

// Keep scalar bytes, including extension values and numeric types, intact.
// Only containers are re-encoded; fields the editor does not know are retained.
public sealed class Value
{
    public byte[]? Raw;
    public List<Value>? Items;
    public List<(Value Key, Value Item)>? Fields;
    public bool IsMap => Fields != null;
    public bool IsArray => Items != null;
    public static Value Map() => new() { Fields = new() };
    public static Value Array(IEnumerable<Value> items) => new() { Items = items.ToList() };
    public static Value String(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text); using var s = new MemoryStream();
        s.WriteByte(0xdb); Write32(s, bytes.Length); s.Write(bytes); return new() { Raw = s.ToArray() };
    }
    public static Value Int(long value)
    {
        if(value >= int.MinValue && value <= int.MaxValue) { byte[] small = new byte[5]; small[0] = 0xd2; BinaryPrimitives.WriteInt32BigEndian(small.AsSpan(1), (int)value); return new() { Raw = small }; }
        byte[] b = new byte[9]; b[0] = 0xd3; BinaryPrimitives.WriteInt64BigEndian(b.AsSpan(1), value); return new() { Raw = b };
    }
    public static Value Bool(bool value) => new() { Raw = new[] { (byte)(value ? 0xc3 : 0xc2) } };
    public static Value Binary(byte[] bytes)
    {
        using var s = new MemoryStream(); s.WriteByte(0xc6); Write32(s, bytes.Length); s.Write(bytes); return new() { Raw = s.ToArray() };
    }
    public Value? Get(string key) => Fields?.FirstOrDefault(p => p.Key.Text() == key).Item;
    public string Str(string key, string fallback = "") => Get(key)?.Text() ?? fallback;
    public void Set(string key, Value val)
    {
        if (Fields == null) throw new InvalidDataException("객체가 아닙니다.");
        int i = Fields.FindIndex(p => p.Key.Text() == key);
        if(i < 0) Fields.Add((String(key), val)); else Fields[i] = (Fields[i].Key, val);
    }
    public void Remove(string key) => Fields?.RemoveAll(p => p.Key.Text() == key);
    public string? Text()
    {
        if(Raw == null || Raw.Length == 0) return null;
        int c=Raw[0], offset = c >= 0xa0 && c<=0xbf ? 1 : c==0xd9 ? 2 : c==0xda ? 3 : c==0xdb ? 5 : 0;
        return offset==0 ? null : Encoding.UTF8.GetString(Raw,offset,Raw.Length-offset);
    }
    public long? Number()
    {
        if(Raw == null) return null; int c=Raw[0]; var b=Raw.AsSpan(1);
        if(c<0x80) return c; if(c>=0xe0) return unchecked((sbyte)c);
        return c switch {0xcc=>b[0],0xcd=>BinaryPrimitives.ReadUInt16BigEndian(b),0xce=>BinaryPrimitives.ReadUInt32BigEndian(b),0xcf=>checked((long)BinaryPrimitives.ReadUInt64BigEndian(b)),0xd0=>unchecked((sbyte)b[0]),0xd1=>BinaryPrimitives.ReadInt16BigEndian(b),0xd2=>BinaryPrimitives.ReadInt32BigEndian(b),0xd3=>BinaryPrimitives.ReadInt64BigEndian(b),_=>null};
    }
    public bool Boolean() => Raw?.Length==1 && Raw[0]==0xc3;
    public byte[] Bytes()
    {
        if(Raw==null) throw new InvalidDataException("바이너리 데이터가 없습니다.");
        int off=Raw[0] switch {0xc4=>2,0xc5=>3,0xc6=>5,_=>0};
        if(off==0) throw new InvalidDataException("지원하지 않는 바이너리 형식입니다."); return Raw[off..];
    }
    public Value Clone() => new() { Raw = Raw, Items = Items?.Select(v => v.Clone()).ToList(), Fields = Fields?.Select(p => (p.Key.Clone(), p.Item.Clone())).ToList() };
    public byte[] Encode() { using var s=new MemoryStream(); Write(s); return s.ToArray(); }
    void Write(Stream s)
    {
        if(Fields!=null) {s.WriteByte(0xdf);Write32(s,Fields.Count);foreach(var p in Fields){p.Key.Write(s);p.Item.Write(s);}}
        else if(Items!=null){s.WriteByte(0xdd);Write32(s,Items.Count);foreach(var item in Items)item.Write(s);}
        else s.Write(Raw ?? new byte[]{0xc0});
    }
    static void Write32(Stream s,int n){Span<byte>b=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(b,(uint)n);s.Write(b);}
    public static Value Parse(byte[] bytes)
    {
        var r=new Reader(bytes); var val=r.Read(0); if(r.Pos!=bytes.Length)throw new InvalidDataException("데이터 뒤에 알 수 없는 내용이 있습니다.");return val;
    }
    sealed class Reader(byte[] data)
    {
        public int Pos;
        void Need(int n){if(n<0||n>data.Length-Pos)throw new InvalidDataException("파일이 잘렸거나 길이가 잘못되었습니다.");}
        int Take(){Need(1);return data[Pos++];}
        int Len(int size){Need(size);uint n=size==1?data[Pos]:size==2?BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(Pos)):BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(Pos));Pos+=size;if(n>64*1024*1024)throw new InvalidDataException("데이터 크기 제한을 초과했습니다.");return (int)n;}
        public Value Read(int depth)
        {
            if(depth>128)throw new InvalidDataException("중첩이 너무 깊습니다.");int start=Pos,c=Take();int array=-1,map=-1,skip=0;
            if(c<=0x7f||c>=0xe0||c==0xc0||c==0xc2||c==0xc3){}
            else if(c>=0xa0&&c<=0xbf)skip=c&31;
            else if(c>=0x90&&c<=0x9f)array=c&15;
            else if(c>=0x80&&c<=0x8f)map=c&15;
            else switch(c){
                case 0xc4:case 0xd9:skip=Len(1);break;case 0xc5:case 0xda:skip=Len(2);break;case 0xc6:case 0xdb:skip=Len(4);break;
                case 0xdc:array=Len(2);break;case 0xdd:array=Len(4);break;case 0xde:map=Len(2);break;case 0xdf:map=Len(4);break;
                case 0xcc:case 0xd0:skip=1;break;case 0xcd:case 0xd1:skip=2;break;case 0xca:case 0xce:case 0xd2:skip=4;break;case 0xcb:case 0xcf:case 0xd3:skip=8;break;
                case 0xd4:skip=2;break;case 0xd5:skip=3;break;case 0xd6:skip=5;break;case 0xd7:skip=9;break;case 0xd8:skip=17;break;
                case 0xc7:skip=Len(1)+1;break;case 0xc8:skip=Len(2)+1;break;case 0xc9:skip=Len(4)+1;break;
                default:throw new InvalidDataException($"지원하지 않는 MessagePack 코드: {c:X2}");
            }
            if(array>=0){if(array>1000000)throw new InvalidDataException("항목이 너무 많습니다.");var v=Array(System.Array.Empty<Value>());for(int i=0;i<array;i++)v.Items!.Add(Read(depth+1));return v;}
            if(map>=0){if(map>1000000)throw new InvalidDataException("항목이 너무 많습니다.");var v=Map();for(int i=0;i<map;i++){var key=Read(depth+1);v.Fields!.Add((key,Read(depth+1)));}return v;}
            Need(skip);Pos+=skip;return new(){Raw=data[start..Pos]};
        }
    }
}

public sealed class Preset
{
    public required string Path {get;init;}
    public required Value Envelope {get;init;}
    public required Value Data {get;init;}
    public string Name => Data.Str("name", System.IO.Path.GetFileNameWithoutExtension(Path));
    public List<Value>? Blocks => Data.Get("promptTemplate")?.Items;
    public Preset Clone() => new(){Path=Path,Envelope=Envelope.Clone(),Data=Data.Clone()};
}

public static class RisupCodec
{
    const int Limit=64*1024*1024;
    static byte[] MapData {get; }=LoadMap();
    static byte[] Key {get;}=SHA256.HashData(Encoding.UTF8.GetBytes("risupreset"));
    static byte[] LoadMap(){using var s=typeof(RisupCodec).Assembly.GetManifestResourceStream("RisupEditor.rpack_map.bin")!;using var m=new MemoryStream();s.CopyTo(m);return m.ToArray();}
    public static Preset Load(string path)
    {
        if(new FileInfo(path).Length>Limit)throw new InvalidDataException("파일이 64MB를 초과합니다.");
        var bytes=File.ReadAllBytes(path);
        if(System.IO.Path.GetExtension(path).Equals(".risup",StringComparison.OrdinalIgnoreCase))bytes=bytes.Select(b=>MapData[256+b]).ToArray();
        using var input=new MemoryStream(bytes);
        using Stream zip=bytes.Length>1&&bytes[0]==0x1f&&bytes[1]==0x8b?new GZipStream(input,CompressionMode.Decompress):bytes.Length>1&&(bytes[0]&15)==8&&((bytes[0]<<8)+bytes[1])%31==0?new ZLibStream(input,CompressionMode.Decompress):new DeflateStream(input,CompressionMode.Decompress);
        using var decoded=new MemoryStream();byte[] buffer=new byte[8192];int n;
        while((n=zip.Read(buffer))>0){if(decoded.Length+n>Limit)throw new InvalidDataException("압축 해제 크기가 64MB를 초과합니다.");decoded.Write(buffer,0,n);}
        var outer=Value.Parse(decoded.ToArray());
        if(outer.Str("type")!="preset" || outer.Get("presetVersion")?.Number() is not (0 or 2))throw new InvalidDataException("지원하지 않는 프리셋 형식 또는 버전입니다.");
        var encrypted=(outer.Get("preset")??outer.Get("pres")??throw new InvalidDataException("프리셋 데이터가 없습니다.")).Bytes();
        if(encrypted.Length<16)throw new InvalidDataException("암호화된 데이터가 잘렸습니다.");
        byte[] plain=new byte[encrypted.Length-16];using var aes=new AesGcm(Key,16);aes.Decrypt(new byte[12],encrypted.AsSpan(0,plain.Length),encrypted.AsSpan(plain.Length),plain);
        var data=Value.Parse(plain);if(!data.IsMap)throw new InvalidDataException("프리셋이 객체가 아닙니다.");
        if(data.Get("promptTemplate") is { } template && template.Raw?.FirstOrDefault()!=0xc0 && !template.IsArray)throw new InvalidDataException("프롬프트 목록 형식이 올바르지 않습니다.");
        if(data.Get("promptTemplate")?.Items is {} blocks && blocks.Any(b=>!b.IsMap||b.Get("type")?.Text()==null))throw new InvalidDataException("프롬프트 블록 형식이 올바르지 않습니다.");
        return new(){Path=path,Envelope=outer,Data=data};
    }
    public static void Save(Preset preset,string path)
    {
        var plain=preset.Data.Encode();var cipher=new byte[plain.Length];var tag=new byte[16];using var aes=new AesGcm(Key,16);aes.Encrypt(new byte[12],plain,cipher,tag);
        var envelope=preset.Envelope.Clone();envelope.Set("presetVersion",Value.Int(2));envelope.Set("type",Value.String("preset"));
        envelope.Set("preset",Value.Binary(cipher.Concat(tag).ToArray()));envelope.Remove("pres");
        using var ms=new MemoryStream();using(var zip=new GZipStream(ms,CompressionLevel.Optimal,true))zip.Write(envelope.Encode());
        byte[] result=ms.ToArray().Select(b=>MapData[b]).ToArray();
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(result);file.Flush(true);}File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}


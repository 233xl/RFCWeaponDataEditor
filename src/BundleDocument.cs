using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Globalization;
using System.Security.Cryptography;

namespace RfcEditor;

public sealed class Parameter
{
    public required string Path { get; init; }
    public required AssetTypeValueField Field { get; init; }
    public required string Original { get; set; }
    public required string Initial { get; init; }
    public bool ReadOnly { get; init; }
    public string Text => Convert.ToString(Field.AsObject, CultureInfo.InvariantCulture) ?? "";
    public bool Changed => Text != Original;
    public void Set(string text)
    {
        if (ReadOnly) throw new InvalidOperationException(L10n.T("readonly"));
        object value = Field.Value.ValueType switch
        {
            AssetValueType.Bool => text == "1" ? true : text == "0" ? false : bool.Parse(text),
            AssetValueType.Int8 => sbyte.Parse(text, CultureInfo.InvariantCulture),
            AssetValueType.UInt8 => byte.Parse(text, CultureInfo.InvariantCulture),
            AssetValueType.Int16 => short.Parse(text, CultureInfo.InvariantCulture),
            AssetValueType.UInt16 => ushort.Parse(text, CultureInfo.InvariantCulture),
            AssetValueType.Int32 => int.Parse(text, CultureInfo.InvariantCulture),
            AssetValueType.UInt32 => uint.Parse(text, CultureInfo.InvariantCulture),
            AssetValueType.Int64 => long.Parse(text, CultureInfo.InvariantCulture),
            AssetValueType.UInt64 => ulong.Parse(text, CultureInfo.InvariantCulture),
            AssetValueType.Float => float.Parse(text, CultureInfo.InvariantCulture),
            AssetValueType.Double => double.Parse(text, CultureInfo.InvariantCulture),
            AssetValueType.String => text,
            _ => throw new InvalidOperationException(L10n.T("unsupportedType"))
        };
        if (value is float f && !float.IsFinite(f) || value is double d && !double.IsFinite(d))
            throw new FormatException(L10n.T("finite"));
        Field.AsObject = value;
    }
}

public sealed class Component
{
    public required int Entry { get; init; }
    public required AssetsFileInstance File { get; init; }
    public required AssetFileInfo Info { get; init; }
    public required AssetTypeValueField Root { get; init; }
    public required string Kind { get; init; }
    public required string Name { get; init; }
    public required List<Parameter> Parameters { get; init; }
    public long GameObjectId => Root["m_GameObject.m_PathID"].AsLong;
    public bool Changed => Parameters.Any(p => p.Changed);
    public bool NeedsRewrite => Parameters.Any(p => p.Text != p.Initial);
    public override string ToString() => $"{(Changed ? "● " : "")}{Kind}  ·  {Name}";
}

public sealed class BundleDocument : IDisposable
{
    public string Source { get; }
    public string UnityVersion { get; private set; } = "";
    public List<Component> Components { get; } = new();
    public List<string> Diagnostics { get; } = new();
    readonly AssetsManager manager = new();
    readonly BundleFileInstance bundle;
    readonly Dictionary<int, AssetsFileInstance> files = new();
    byte[] sourceHash;
    public BundleDocument(string path)
    {
        Source = Path.GetFullPath(path);
        try
        {
            manager.LoadClassPackage(Path.Combine(AppContext.BaseDirectory, "classdata.tpk"));
            // Keep a read-only snapshot so the original has no lingering file handle.
            // This also makes repeated overwrites independent of stale offsets.
            byte[] sourceBytes = File.ReadAllBytes(Source);
            sourceHash = SHA256.HashData(sourceBytes);
            bundle = manager.LoadBundleFile(new MemoryStream(sourceBytes, false), Source, true);
            for (int entry = 0; entry < bundle.file.BlockAndDirInfo.DirectoryInfos.Length; entry++)
            {
                if (!bundle.file.IsAssetsFile(entry)) continue;
                var file = manager.LoadAssetsFileFromBundle(bundle, entry, false);
                files.Add(entry, file);
                UnityVersion = file.file.Metadata.UnityVersion;
                manager.LoadClassDatabaseFromPackage(UnityVersion);
                foreach (var info in file.file.GetAssetsOfType(114))
                {
                    try
                    {
                        var root = manager.GetBaseField(file, info);
                        string klass = ScriptClass(file, root);
                        var config = root["configuration"];
                        string kind = klass.Contains("Projectile", StringComparison.OrdinalIgnoreCase) ? "Projectile" :
                            klass.Contains("Weapon", StringComparison.OrdinalIgnoreCase) && klass != "WeaponContentMod" ? "Weapon" : "";
                        if (kind == "" && !config.IsDummy)
                        {
                            if (!config["damage"].IsDummy && !config["speed"].IsDummy) kind = "Projectile";
                            else if (!config["projectilePrefab"].IsDummy && !config["ammo"].IsDummy) kind = "Weapon";
                        }
                        if (kind == "") continue;
                        // Round-trip before enabling edits: do not rewrite partially parsed scripts.
                        if (!root.WriteToByteArray(file.file.Reader.BigEndian).SequenceEqual(Raw(file.file, info)))
                            throw new InvalidDataException(L10n.T("roundtrip",klass));
                        string name = GameObjectName(file, root);
                        var parameters = new List<Parameter>();
                        foreach (var child in root.Children)
                            Walk(child, child.FieldName, child.FieldName.StartsWith("m_"), parameters);
                        if (!parameters.Any(p => !p.ReadOnly)) throw new InvalidDataException(L10n.T("noFields"));
                        Components.Add(new Component { Entry = entry, File = file, Info = info, Root = root, Kind = kind,
                            Name = name, Parameters = parameters });
                    }
                    catch (Exception ex) { Diagnostics.Add($"{file.name} / {info.PathId}: {ex.Message}"); }
                }
            }
            Components.Sort((a,b) => string.Compare(a.Kind + a.Name,b.Kind + b.Name,StringComparison.Ordinal));
        }
        catch { manager.UnloadAll(true); throw; }
    }
    string ScriptClass(AssetsFileInstance file, AssetTypeValueField root)
    {
        try { var ext = manager.GetExtAsset(file, root["m_Script"]); return ext.baseField?["m_ClassName"].AsString ?? ""; }
        catch { return ""; }
    }
    string GameObjectName(AssetsFileInstance file, AssetTypeValueField root)
    {
        try { var ext = manager.GetExtAsset(file, root["m_GameObject"]); return ext.baseField?["m_Name"].AsString ?? "Unnamed"; }
        catch { return "Unnamed"; }
    }
    static void Walk(AssetTypeValueField field, string path, bool readOnly, List<Parameter> list)
    {
        readOnly |= field.TypeName.StartsWith("PPtr<") || field.FieldName is "m_FileID" or "m_PathID";
        if (field.Value != null && field.Value.ValueType is >= AssetValueType.Bool and <= AssetValueType.String)
        {
            list.Add(new Parameter { Field = field, Path = path, ReadOnly = readOnly,
                Original = Convert.ToString(field.AsObject, CultureInfo.InvariantCulture) ?? "",
                Initial = Convert.ToString(field.AsObject, CultureInfo.InvariantCulture) ?? "" });
            return;
        }
        for (int i=0; i<field.Children.Count; i++)
        {
            var child = field.Children[i];
            var part = field.TemplateField.IsArray ? $"[{i}]" : "." + child.FieldName;
            Walk(child, path + part, readOnly, list);
        }
    }
    static byte[] Raw(AssetsFile file, AssetFileInfo info)
    {
        file.Reader.Position = info.GetAbsoluteByteStart(file);
        return file.Reader.ReadBytes(checked((int)info.ByteSize));
    }
    public int ChangeCount => Components.Sum(c => c.Parameters.Count(p=>p.Changed));
    public string Relation(Component component)
    {
        if(component.Kind == "Weapon")
        {
            var ptr = component.Root["configuration.projectilePrefab"];
            if(ptr.IsDummy) return L10n.T("noProjectile");
            var linked = ptr["m_FileID"].AsInt == 0 ? Components.Where(c=>c.Kind=="Projectile" && c.Entry==component.Entry && c.GameObjectId==ptr["m_PathID"].AsLong).ToList() : new();
            return linked.Count>0 ? L10n.T("usesProjectile",string.Join(", ",linked.Select(c=>c.Name))) : L10n.T("projectileRef",ptr["m_FileID"].AsInt,ptr["m_PathID"].AsLong);
        }
        var users = Components.Where(c=>c.Kind=="Weapon" && c.Entry==component.Entry).Where(c=> {
            var ptr=c.Root["configuration.projectilePrefab"];
            return !ptr.IsDummy && ptr["m_FileID"].AsInt==0 && ptr["m_PathID"].AsLong==component.GameObjectId;
        }).Select(c=>c.Name).ToList();
        return users.Count>0 ? L10n.T("shared",string.Join(", ",users)) : L10n.T("noUsers");
    }
    public void SaveAs(string destination)
    {
        destination = Path.GetFullPath(destination);
        if(string.Equals(destination,Source,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L10n.T("newName"));
        if(File.Exists(destination)) throw new IOException(L10n.T("exists"));
        string temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            WriteVerified(temp);
            File.Move(temp,destination); // Never replace a pre-existing file.
        }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
    public string? Save()
    {
        if(ChangeCount==0) return null;
        string temp=Source+"."+Guid.NewGuid().ToString("N")+".tmp";
        string backup=Source+"."+DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture)+"."+Guid.NewGuid().ToString("N")[..8]+".bak";
        try
        {
            WriteVerified(temp);
            byte[] nextHash;
            using(var staged=File.OpenRead(temp)) nextHash=SHA256.HashData(staged);
            // Deny concurrent writes while hashing and atomically replacing the file.
            // Delete sharing is needed by File.Replace, which also creates the backup.
            using(var guard=new FileStream(Source,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
            {
                if(!sourceHash.SequenceEqual(SHA256.HashData(guard))) throw new IOException(L10n.T("externalChange"));
                File.Replace(temp,Source,backup);
            }
            sourceHash=nextHash;
            foreach(var p in Components.SelectMany(c=>c.Parameters)) p.Original=p.Text;
            return backup;
        }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
    void WriteVerified(string temp)
    {
            var replacements=new List<BundleReplacer>();
            foreach(var pair in files)
            {
                var changed=Components.Where(c=>c.Entry==pair.Key && c.NeedsRewrite).ToList();
                if(changed.Count==0) continue;
                var edits=changed.Select(c=>(AssetsReplacer)new AssetsReplacerFromMemory(pair.Value.file,c.Info,c.Root.WriteToByteArray(pair.Value.file.Reader.BigEndian))).ToList();
                using var data=new MemoryStream();
                using(var writer=new AssetsFileWriter(data)) pair.Value.file.Write(writer,0,edits);
                string name=bundle.file.GetFileName(pair.Key);
                replacements.Add(new BundleReplacerFromMemory(name,name,true,data.ToArray(),-1));
            }
            using(var writer=new AssetsFileWriter(temp)) bundle.file.Write(writer,replacements);
            Verify(temp);
    }
    void Verify(string path)
    {
        var check=new AssetsManager();
        try
        {
            var output=check.LoadBundleFile(path,true);
            if(!bundle.file.GetAllFileNames().SequenceEqual(output.file.GetAllFileNames()))throw new InvalidDataException(L10n.T("directoryChanged"));
            for(int i=0;i<bundle.file.BlockAndDirInfo.DirectoryInfos.Length;i++)
            {
                if(files.TryGetValue(i,out var original))
                {
                    var saved=check.LoadAssetsFileFromBundle(output,i,false);
                    if(!original.file.AssetInfos.Select(a=>a.PathId).Order().SequenceEqual(saved.file.AssetInfos.Select(a=>a.PathId).Order()))throw new InvalidDataException(L10n.T("idsChanged"));
                    var modified=Components.Where(c=>c.Entry==i && c.NeedsRewrite).ToDictionary(c=>c.Info.PathId);
                    foreach(var info in original.file.AssetInfos)
                    {
                        var expected=modified.TryGetValue(info.PathId,out var component) ? component.Root.WriteToByteArray(original.file.Reader.BigEndian) : Raw(original.file,info);
                        if(!expected.SequenceEqual(Raw(saved.file,saved.file.GetAssetInfo(info.PathId))))throw new InvalidDataException(L10n.T("dataMismatch",info.PathId));
                    }
                }
                else if(!EntryHash(bundle.file,i).SequenceEqual(EntryHash(output.file,i)))throw new InvalidDataException(L10n.T("streamsChanged"));
            }
        }
        finally { check.UnloadAll(true); }
    }
    static byte[] EntryHash(AssetBundleFile file,int index)
    {
        file.GetFileRange(index,out long offset,out long length);
        file.DataReader.Position=offset;
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while(length>0){var bytes=file.DataReader.ReadBytes((int)Math.Min(length,1024*1024));if(bytes.Length==0)throw new EndOfStreamException();hash.AppendData(bytes);length-=bytes.Length;}
        return hash.GetHashAndReset();
    }
    public void Dispose() => manager.UnloadAll(true);
}

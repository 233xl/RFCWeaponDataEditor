using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace RfcEditor;

public sealed partial class BundleDocument
{
    readonly HashSet<(int Entry,long Id)> removedAssets=new();
    readonly HashSet<(int Entry,long Id)> unsavedNewIds=new();
    int unsavedDeletions;

    public int DeleteComponent(Component component)
    {
        if(!Components.Contains(component) || component.Kind is not ("Projectile" or "Weapon"))throw new InvalidOperationException(L10n.T("invalidComponent"));
        if(component.Kind=="Projectile" && WeaponsFor(component) is {Count:>0} users)
            throw new InvalidOperationException(L10n.T("deleteInUse",string.Join(", ",users.Select(w=>w.Name))));
        var hierarchy=CollectHierarchy(component);
        var ids=hierarchy.Keys.ToHashSet();
        var staged=new List<AddedAsset>();
        var blockers=new List<string>();
        foreach(var pair in files)
        {
            var file=pair.Value;
            manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
            var records=file.file.AssetInfos.Where(i=>!removedAssets.Contains((pair.Key,i.PathId)))
                .Concat(addedAssets.Values.Where(a=>a.Entry==pair.Key).Select(a=>a.Info));
            foreach(var info in records)
            {
                if(pair.Key==component.Entry && ids.Contains(info.PathId))continue;
                AssetTypeValueField root;
                try
                {
                    if(editedAssets.TryGetValue((pair.Key,info.PathId),out var edited))root=edited.Root;
                    else if(addedAssets.TryGetValue((pair.Key,info.PathId),out var added))root=added.Root;
                    else if(Components.FirstOrDefault(c=>c.Entry==pair.Key && c.Info.PathId==info.PathId) is {} known)root=known.Root;
                    else
                    {
                        var template=manager.GetTemplateBaseField(file,info);
                        if(!HasPointers(template))continue;
                        root=manager.GetBaseField(file,info);
                        if(!root.WriteToByteArray(file.file.Reader.BigEndian).SequenceEqual(Raw(file.file,info)))
                            throw new InvalidDataException("Incomplete type information");
                    }
                }
                catch(Exception)
                {throw new InvalidOperationException(L10n.T("deleteCannotInspect",file.name,info.PathId));}

                bool Refers(AssetTypeValueField ptr)
                {
                    if(ptr.IsDummy || ptr["m_PathID"].IsDummy || ptr["m_FileID"].IsDummy)return false;
                    long id=ptr["m_PathID"].AsLong;int fileId=ptr["m_FileID"].AsInt;
                    if(!ids.Contains(id))return false;
                    if(fileId==0)return pair.Key==component.Entry;
                    if(fileId<1 || fileId>file.file.Metadata.Externals.Count)return false;
                    string external=file.file.Metadata.Externals[fileId-1].PathName.Replace('\\','/');
                    string target=component.File.name.Replace('\\','/');
                    return string.Equals(external,target,StringComparison.OrdinalIgnoreCase) || external.EndsWith("/"+target,StringComparison.OrdinalIgnoreCase);
                }
                bool metadata=info.TypeId==142;
                bool registry=info.TypeId==114 && !root["weaponEntries.Array"].IsDummy && ScriptClass(file,root)=="WeaponContentMod";
                bool changed=false;
                if(metadata || registry)
                {
                    using var reader=new AssetsFileReader(new MemoryStream(root.WriteToByteArray(file.file.Reader.BigEndian),false)){BigEndian=file.file.Reader.BigEndian};
                    root=root.TemplateField.MakeValue(reader,manager.GetRefTypeManager(file));
                    if(registry)
                    {
                        var entries=root["weaponEntries.Array"].Children;
                        changed|=entries.RemoveAll(e=>!e["prefab"].IsDummy && Refers(e["prefab"]))>0;
                    }
                    if(metadata)
                    {
                        var table=root["m_PreloadTable.Array"];var containers=root["m_Container.Array"];
                        if(table.IsDummy || containers.IsDummy)throw new InvalidOperationException(L10n.T("deleteCannotInspect",file.name,info.PathId));
                        var old=table.Children.ToList();var prefix=new int[old.Count+1];
                        for(int n=0;n<old.Count;n++)prefix[n+1]=prefix[n]+(Refers(old[n])?0:1);
                        changed|=containers.Children.RemoveAll(e=>Refers(e["second.asset"]))>0;
                        var ranges=containers.Children.Select(e=>e["second"]).ToList();
                        var main=root["m_MainAsset"];
                        if(!main.IsDummy)
                        {
                            if(Refers(main["asset"]))
                            {main["asset.m_FileID"].AsInt=0;main["asset.m_PathID"].AsLong=0;main["preloadIndex"].AsInt=0;main["preloadSize"].AsInt=0;changed=true;}
                            else ranges.Add(main);
                        }
                        foreach(var range in ranges)
                        {
                            int start=range["preloadIndex"].AsInt,count=range["preloadSize"].AsInt;
                            if(start<0 || count<0 || (long)start+count>old.Count)throw new InvalidOperationException(L10n.T("deleteCannotInspect",file.name,info.PathId));
                            range["preloadIndex"].AsInt=prefix[start];range["preloadSize"].AsInt=prefix[start+count]-prefix[start];
                        }
                        if(prefix[^1]!=old.Count){table.Children=old.Where(p=>!Refers(p)).ToList();changed=true;}
                    }
                }
                if(Pointers(root).Any(Refers))
                {
                    var known=Components.FirstOrDefault(c=>c.Entry==pair.Key && c.Info.PathId==info.PathId);
                    blockers.Add(known?.ToString()??$"{file.name} / {info.PathId}");
                }
                if(changed)staged.Add(new AddedAsset(pair.Key,info,root));
            }
        }
        if(blockers.Count>0)throw new InvalidOperationException(L10n.T("deleteBlocked",string.Join("\n",blockers.Take(12))));
        // All dependency checks and registry/preload edits are staged before mutation.
        foreach(var asset in staged)
        {
            if(addedAssets.ContainsKey((asset.Entry,asset.Info.PathId)))addedAssets[(asset.Entry,asset.Info.PathId)]=asset;
            else editedAssets[(asset.Entry,asset.Info.PathId)]=asset;
        }
        foreach(long id in ids)
        {
            var key=(component.Entry,id);
            removedAssets.Add(key);addedAssets.Remove(key);editedAssets.Remove(key);unsavedNewIds.Remove(key);
        }
        unsavedAdditions=unsavedNewIds.Count;unsavedDeletions+=ids.Count;
        Components.RemoveAll(c=>c.Entry==component.Entry && ids.Contains(c.Info.PathId));
        return ids.Count;
    }
    static bool HasPointers(AssetTypeTemplateField template)=>template.Type.StartsWith("PPtr<") || template.Children.Any(HasPointers);
}

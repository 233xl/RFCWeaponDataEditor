using AssetsTools.NET;
using System.Security.Cryptography;

namespace RfcEditor;

public sealed partial class BundleDocument
{
    sealed record AddedAsset(int Entry,AssetFileInfo Info,AssetTypeValueField Root);
    readonly Dictionary<(int Entry,long Id),AddedAsset> addedAssets=new();
    readonly Dictionary<(int Entry,long Id),AddedAsset> editedAssets=new();
    int unsavedAdditions;

    public Component? ProjectileFor(Component weapon)
    {
        var ptr=weapon.Root["configuration.projectilePrefab"];
        if(weapon.Kind!="Weapon" || ptr.IsDummy || ptr["m_FileID"].AsInt!=0)return null;
        return Components.FirstOrDefault(c=>c.Kind=="Projectile" && c.Entry==weapon.Entry && c.GameObjectId==ptr["m_PathID"].AsLong);
    }
    public List<Component> WeaponsFor(Component projectile)=>Components.Where(c=>c.Kind=="Weapon" && ProjectileFor(c)==projectile).ToList();
    void RequireComponent(Component component,string kind)
    {
        if(!Components.Contains(component) || component.Kind!=kind)throw new InvalidOperationException(L10n.T("invalidComponent"));
    }
    public void AssignProjectile(Component projectile,IEnumerable<Component> weapons)
    {
        RequireComponent(projectile,"Projectile");
        var selected=weapons.Distinct().ToList();
        foreach(var weapon in selected)
        {
            RequireComponent(weapon,"Weapon");
            if(weapon.Entry!=projectile.Entry || weapon.Root["configuration.projectilePrefab"].IsDummy)
                throw new InvalidOperationException(L10n.T("crossFile"));
        }
        foreach(var weapon in selected)
        {
            var ptr=weapon.Root["configuration.projectilePrefab"];
            ptr["m_FileID"].AsInt=0;ptr["m_PathID"].AsLong=projectile.GameObjectId;
        }
    }
    public void ResetComponent(Component component)
    {
        if(!Components.Contains(component))throw new InvalidOperationException(L10n.T("invalidComponent"));
        if(component.Kind=="Weapon")
        {
            var originalId=component.Parameters.FirstOrDefault(p=>p.Path=="configuration.projectilePrefab.m_PathID");
            var originalFile=component.Parameters.FirstOrDefault(p=>p.Path=="configuration.projectilePrefab.m_FileID");
            if(originalId!=null && originalFile?.Original=="0" && removedAssets.Contains((component.Entry,long.Parse(originalId.Original,System.Globalization.CultureInfo.InvariantCulture))))
                throw new InvalidOperationException(L10n.T("resetDeletedReference"));
        }
        foreach(var p in component.Parameters)
        {
            if(!p.ReadOnly)p.Set(p.Original);
            else if(component.Kind=="Weapon" && p.Path=="configuration.projectilePrefab.m_FileID")p.Field.AsInt=int.Parse(p.Original,System.Globalization.CultureInfo.InvariantCulture);
            else if(component.Kind=="Weapon" && p.Path=="configuration.projectilePrefab.m_PathID")p.Field.AsLong=long.Parse(p.Original,System.Globalization.CultureInfo.InvariantCulture);
        }
    }
    public Component CloneProjectile(Component source,string name,IEnumerable<Component>? weapons=null)
    {
        RequireComponent(source,"Projectile");
        name=name.Trim();
        if(name.Length==0 || name.Length>160)throw new ArgumentException(L10n.T("cloneNameInvalid"));
        var selected=(weapons??Array.Empty<Component>()).Distinct().ToList();
        foreach(var weapon in selected)
        {
            RequireComponent(weapon,"Weapon");
            if(weapon.Entry!=source.Entry || weapon.Root["configuration.projectilePrefab"].IsDummy)throw new InvalidOperationException(L10n.T("crossFile"));
        }
        var file=source.File;
        var owned=CollectHierarchy(source);
        // Allocate a separate ID for every owned object before remapping any pointer.
        var used=file.file.AssetInfos.Select(i=>i.PathId).Concat(addedAssets.Values.Where(a=>a.Entry==source.Entry).Select(a=>a.Info.PathId)).ToHashSet();
        var ids=new Dictionary<long,long>();
        foreach(long oldId in owned.Keys)
        {
            long next;do{next=BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8))&long.MaxValue;}while(next==0 || !used.Add(next));
            ids.Add(oldId,next);
        }
        var staged=new List<AddedAsset>();
        foreach(var pair in owned)
        {
            var original=pair.Value;byte[] bytes=original.Root.WriteToByteArray(file.file.Reader.BigEndian);
            using var reader=new AssetsFileReader(new MemoryStream(bytes,false)){BigEndian=file.file.Reader.BigEndian};
            var copy=original.Root.TemplateField.MakeValue(reader,manager.GetRefTypeManager(file));
            foreach(var ptr in Pointers(copy))
                if(ptr["m_FileID"].AsInt==0 && ids.TryGetValue(ptr["m_PathID"].AsLong,out long newId))ptr["m_PathID"].AsLong=newId;
            if(pair.Key==source.GameObjectId)copy["m_Name"].AsString=name;
            if(original.Info.TypeId is 4 or 224 && original.Root["m_GameObject.m_PathID"].AsLong==source.GameObjectId)
            {copy["m_Father.m_FileID"].AsInt=0;copy["m_Father.m_PathID"].AsLong=0;}
            var info=new AssetFileInfo{PathId=ids[pair.Key],TypeId=original.Info.TypeId,TypeIdOrIndex=original.Info.TypeIdOrIndex,
                ClassId=original.Info.ClassId,ScriptTypeIndex=original.Info.ScriptTypeIndex,Stripped=original.Info.Stripped,ByteSize=(uint)bytes.Length};
            staged.Add(new AddedAsset(source.Entry,info,copy));
        }
        var projectileAsset=staged.Single(a=>a.Info.PathId==ids[source.Info.PathId]);
        var parameters=new List<Parameter>();
        foreach(var field in projectileAsset.Root.Children)Walk(field,field.FieldName,field.FieldName.StartsWith("m_"),parameters);
        var clone=new Component{Entry=source.Entry,File=file,Info=projectileAsset.Info,Root=projectileAsset.Root,Kind="Projectile",Name=name,Parameters=parameters};
        var preloadEdits=PreparePreloads(source,staged);
        // Commit only after the complete hierarchy was successfully parsed and cloned.
        foreach(var asset in staged){addedAssets.Add((asset.Entry,asset.Info.PathId),asset);unsavedNewIds.Add((asset.Entry,asset.Info.PathId));}
        foreach(var asset in preloadEdits)editedAssets[(asset.Entry,asset.Info.PathId)]=asset;
        unsavedAdditions+=staged.Count;Components.Add(clone);
        AssignProjectile(clone,selected);
        return clone;
    }
    Dictionary<long,(AssetFileInfo Info,AssetTypeValueField Root)> CollectHierarchy(Component source)
    {
        var file=source.File;
        var owned=new Dictionary<long,(AssetFileInfo Info,AssetTypeValueField Root)>();
        var visited=new HashSet<long>();
        (AssetFileInfo Info,AssetTypeValueField Root) Read(long id)
        {
            if(owned.TryGetValue(id,out var cached))return cached;
            if(addedAssets.TryGetValue((source.Entry,id),out var added))return (added.Info,added.Root);
            var info=file.file.GetAssetInfo(id)??throw new InvalidDataException(L10n.T("cloneMissing",id));
            var edited=Components.FirstOrDefault(c=>c.Entry==source.Entry && c.Info.PathId==id);
            var root=edited?.Root??manager.GetBaseField(file,info);
            if(edited==null && !root.WriteToByteArray(file.file.Reader.BigEndian).SequenceEqual(Raw(file.file,info)))throw new InvalidDataException(L10n.T("cloneUnsupported",id));
            return (info,root);
        }
        long Local(AssetTypeValueField ptr)
        {
            if(ptr.IsDummy || ptr["m_FileID"].IsDummy || ptr["m_FileID"].AsInt!=0 || ptr["m_PathID"].AsLong==0)throw new InvalidDataException(L10n.T("cloneExternal"));
            return ptr["m_PathID"].AsLong;
        }
        void VisitGameObject(long id)
        {
            if(!visited.Add(id))throw new InvalidDataException(L10n.T("cloneCycle"));
            if(visited.Count>10000)throw new InvalidDataException(L10n.T("cloneTooLarge"));
            var go=Read(id);
            if(go.Info.TypeId!=1)throw new InvalidDataException(L10n.T("cloneUnsupported",id));
            owned.Add(id,go);
            var array=go.Root["m_Component.Array"];
            if(array.IsDummy)throw new InvalidDataException(L10n.T("cloneUnsupported",id));
            var transforms=new List<AssetTypeValueField>();
            foreach(var pair in array.Children)
            {
                long componentId=Local(pair["component"].IsDummy?pair:pair["component"]);
                var component=Read(componentId);
                if(Local(component.Root["m_GameObject"])!=id || owned.ContainsKey(componentId))throw new InvalidDataException(L10n.T("cloneUnsupported",componentId));
                owned.Add(componentId,component);
                if(component.Info.TypeId is 4 or 224)transforms.Add(component.Root);
            }
            if(transforms.Count!=1)throw new InvalidDataException(L10n.T("cloneUnsupported",id));
            var children=transforms[0]["m_Children.Array"];
            if(children.IsDummy)throw new InvalidDataException(L10n.T("cloneUnsupported",id));
            foreach(var childPtr in children.Children)
            {
                var childTransform=Read(Local(childPtr));
                if(childTransform.Info.TypeId is not (4 or 224))throw new InvalidDataException(L10n.T("cloneExternal"));
                VisitGameObject(Local(childTransform.Root["m_GameObject"]));
            }
        }
        if(source.Root["m_GameObject.m_FileID"].AsInt!=0)throw new InvalidDataException(L10n.T("cloneExternal"));
        VisitGameObject(source.GameObjectId);
        if(!owned.ContainsKey(source.Info.PathId))throw new InvalidDataException(L10n.T("cloneMissing",source.Info.PathId));
        return owned;
    }
    List<AddedAsset> PreparePreloads(Component source,List<AddedAsset> added)
    {
        var file=source.File;
        var result=new List<AddedAsset>();
        foreach(var info in file.file.GetAssetsOfType(142))
        {
            var original=editedAssets.TryGetValue((source.Entry,info.PathId),out var old)?old.Root:manager.GetBaseField(file,info);
            var root=CopyValue(original,source);
            var table=root["m_PreloadTable.Array"];
            var containers=root["m_Container.Array"];
            if(table.IsDummy || containers.IsDummy)throw new InvalidDataException(L10n.T("cloneUnsupported",info.PathId));
            var oldTable=table.Children.ToList();
            var ranges=new Dictionary<(int,int),int>();
            var assetInfos=containers.Children.Select(c=>c["second"]).ToList();
            var main=root["m_MainAsset"];
            if(!main.IsDummy && !main["asset"].IsDummy && main["asset.m_PathID"].AsLong!=0)assetInfos.Add(main);
            foreach(var assetInfo in assetInfos)
            {
                int start=assetInfo["preloadIndex"].AsInt,count=assetInfo["preloadSize"].AsInt;
                if(start<0 || count<0 || (long)start+count>oldTable.Count)throw new InvalidDataException(L10n.T("cloneUnsupported",info.PathId));
                if(!ranges.TryGetValue((start,count),out int offset))
                {
                    offset=table.Children.Count;ranges[(start,count)]=offset;
                    table.Children.AddRange(oldTable.Skip(start).Take(count));
                    foreach(var asset in added)
                    {
                        using var reader=new AssetsFileReader(new MemoryStream(new byte[12],false)){BigEndian=file.file.Reader.BigEndian};
                        var ptr=table.TemplateField.Children.Single(t=>t.Name=="data").MakeValue(reader,manager.GetRefTypeManager(file));
                        ptr["m_FileID"].AsInt=0;ptr["m_PathID"].AsLong=asset.Info.PathId;table.Children.Add(ptr);
                    }
                }
                assetInfo["preloadIndex"].AsInt=offset;assetInfo["preloadSize"].AsInt=count+added.Count;
            }
            result.Add(new AddedAsset(source.Entry,info,root));
        }
        return result;
    }
    AssetTypeValueField CopyValue(AssetTypeValueField value,Component context)
    {
        using var reader=new AssetsFileReader(new MemoryStream(value.WriteToByteArray(context.File.file.Reader.BigEndian),false)){BigEndian=context.File.file.Reader.BigEndian};
        return value.TemplateField.MakeValue(reader,manager.GetRefTypeManager(context.File));
    }
    static IEnumerable<AssetTypeValueField> Pointers(AssetTypeValueField field)
    {
        if(field.TypeName.StartsWith("PPtr<")){yield return field;yield break;}
        foreach(var child in field.Children)foreach(var ptr in Pointers(child))yield return ptr;
    }
}

using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Security.Cryptography;

namespace RfcEditor;

static class ProjectileGraphTest
{
    public static void Run(string fixture,string directory)
    {
        Directory.CreateDirectory(directory);
        byte[] fixtureHash=SHA256.HashData(File.ReadAllBytes(fixture));
        string source=Path.Combine(directory,"source.rfc");File.Copy(fixture,source);
        using(var doc=new BundleDocument(source))
        {
            var original=doc.Components.First(c=>c.Kind=="Projectile");
            var weapons=doc.WeaponsFor(original);if(weapons.Count<2)throw new Exception("Test requires two weapons sharing one projectile");
            int beforeCount=doc.Components.Count;
            using(var other=new BundleDocument(source))
            {
                try{doc.CloneProjectile(original,"Invalid foreign assignment",new[]{other.Components.First(c=>c.Kind=="Weapon")});throw new Exception("Foreign weapon accepted");}catch(InvalidOperationException){}
            }
            try{doc.CloneProjectile(original," ");throw new Exception("Blank name accepted");}catch(ArgumentException){}
            if(doc.Components.Count!=beforeCount || doc.PendingAdditions!=0)throw new Exception("Failed clone mutated document");
            var copy=doc.CloneProjectile(original,"Independent bullet",new[]{weapons[0]});
            if(copy.GameObjectId==original.GameObjectId || copy.Info.PathId==original.Info.PathId)throw new Exception("Clone reused object IDs");
            if(doc.ProjectileFor(weapons[0])!=copy || doc.ProjectileFor(weapons[1])!=original)throw new Exception("Subset assignment failed");
            copy.Parameters.Single(p=>p.Path=="configuration.damage").Set("51");
            if(original.Parameters.Single(p=>p.Path=="configuration.damage").Text=="51")throw new Exception("Original damage changed");
            doc.ResetComponent(weapons[0]);if(doc.ProjectileFor(weapons[0])!=original)throw new Exception("Binding reset failed");
            doc.AssignProjectile(copy,new[]{weapons[0]});
            string saved=Path.Combine(directory,"clone.rfc");doc.SaveAs(saved);
            CheckBindings(saved,copy.Info.PathId,weapons[0].Info.PathId,original.Info.PathId,weapons[1].Info.PathId);
            CheckPrefab(saved,copy.GameObjectId,original.GameObjectId);
            doc.Compression=SaveCompression.None;doc.SaveAs(Path.Combine(directory,"clone-uncompressed.rfc"));
            doc.Compression=SaveCompression.LZMA;doc.SaveAs(Path.Combine(directory,"clone-lzma.rfc"));
            CheckPrefab(Path.Combine(directory,"clone-lzma.rfc"),copy.GameObjectId,original.GameObjectId);
            doc.Compression=SaveCompression.Auto;
            byte[] beforeSave=SHA256.HashData(File.ReadAllBytes(source));
            string backup=doc.Save()??throw new Exception("Clone was not saved");
            if(!beforeSave.SequenceEqual(SHA256.HashData(File.ReadAllBytes(backup))))throw new Exception("Backup not exact");
            if(doc.PendingAdditions!=0 || doc.ChangeCount!=0 || doc.HasPendingChanges)throw new Exception("Saved structural state not cleared");
            var copy2=doc.CloneProjectile(copy,"Copy of copy",new[]{weapons[1]});
            copy2.Parameters.Single(p=>p.Path=="configuration.damage").Set("77");
            _=doc.Save()??throw new Exception("Second clone was not saved");
            CheckBindings(source,copy.Info.PathId,weapons[0].Info.PathId,copy2.Info.PathId,weapons[1].Info.PathId);
            using var read=new BundleDocument(source);
            if(read.Components.Count!=beforeCount+2)throw new Exception("Copy-of-copy missing after reopen");
            if(read.Components.Single(c=>c.Info.PathId==copy2.Info.PathId).Parameters.Single(p=>p.Path=="configuration.damage").Text!="77")throw new Exception("Copy-of-copy damage lost");
            if(doc.Save()!=null)throw new Exception("No-op save created backup");
            Console.WriteLine($"PASS: independent prefab clone; selective binding; reset; preload table; shared mesh; save/reopen; copy of copy; repeated overwrite/backup; {read.Components.Count} components.");
        }
        string hierarchy=Path.Combine(directory,"hierarchy.rfc");BuildHierarchyFixture(fixture,hierarchy);
        using(var doc=new BundleDocument(hierarchy))
        {
            var original=doc.Components.First(c=>c.Kind=="Projectile");
            var copy=doc.CloneProjectile(original,"Hierarchy copy");
            string path=Path.Combine(directory,"hierarchy-cloned.rfc");doc.SaveAs(path);
            CheckPrefab(path,copy.GameObjectId,original.GameObjectId,expectChild:true);
            Console.WriteLine("PASS: child GameObject/Transform cloned; parent-child references remapped; original child preserved.");
        }
        if(!fixtureHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(fixture))))throw new Exception("Fixture changed");
        Console.WriteLine("PASS: original fixture unchanged.");
    }
    static void CheckBindings(string path,long firstBullet,long firstWeapon,long secondBullet,long secondWeapon)
    {
        using var doc=new BundleDocument(path);
        var w1=doc.Components.Single(c=>c.Info.PathId==firstWeapon);var w2=doc.Components.Single(c=>c.Info.PathId==secondWeapon);
        if(doc.ProjectileFor(w1)?.Info.PathId!=firstBullet || doc.ProjectileFor(w2)?.Info.PathId!=secondBullet)throw new Exception("Saved bindings incorrect");
        if(doc.Components.Single(c=>c.Info.PathId==firstBullet).Parameters.Single(p=>p.Path=="configuration.damage").Text!="51")throw new Exception("Independent damage lost");
    }
    static AssetsManager Manager()=>new AssetsManager();
    static void CheckPrefab(string path,long newGo,long oldGo,bool expectChild=false)
    {
        var manager=Manager();
        try
        {
            manager.LoadClassPackage(Path.Combine(AppContext.BaseDirectory,"classdata.tpk"));
            var bundle=manager.LoadBundleFile(path,true);var file=manager.LoadAssetsFileFromBundle(bundle,0,false);manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
            AssetTypeValueField Root(long id)=>manager.GetBaseField(file,id);
            List<long> ComponentsOf(long id)=>Root(id)["m_Component.Array"].Children.Select(c=>c["component.m_PathID"].AsLong).ToList();
            var oldComponents=ComponentsOf(oldGo);var newComponents=ComponentsOf(newGo);
            if(oldComponents.Intersect(newComponents).Any())throw new Exception("Owned components were shared");
            foreach(long id in newComponents)if(Root(id)["m_GameObject.m_PathID"].AsLong!=newGo)throw new Exception("Component owner not remapped");
            long TransformOf(long id)=>ComponentsOf(id).Single(c=>file.file.GetAssetInfo(c).TypeId==4);
            var newTransform=Root(TransformOf(newGo));
            if(newTransform["m_Father.m_PathID"].AsLong!=0)throw new Exception("Copied root not detached");
            var oldMesh=oldComponents.FirstOrDefault(id=>file.file.GetAssetInfo(id).TypeId==33);
            var newMesh=newComponents.FirstOrDefault(id=>file.file.GetAssetInfo(id).TypeId==33);
            if(oldMesh!=0 && Root(oldMesh)["m_Mesh.m_PathID"].AsLong!=Root(newMesh)["m_Mesh.m_PathID"].AsLong)throw new Exception("Mesh reference changed");
            if(expectChild)
            {
                long child=newTransform["m_Children.Array"].Children.Single()["m_PathID"].AsLong;
                long oldChild=Root(TransformOf(oldGo))["m_Children.Array"].Children.Single()["m_PathID"].AsLong;
                if(child==oldChild || Root(child)["m_Father.m_PathID"].AsLong!=TransformOf(newGo))throw new Exception("Child links not remapped");
            }
            foreach(var info in file.file.GetAssetsOfType(142))
            {
                var root=manager.GetBaseField(file,info);var table=root["m_PreloadTable.Array"].Children;
                foreach(var container in root["m_Container.Array"].Children)
                {
                    var a=container["second"];int start=a["preloadIndex"].AsInt,count=a["preloadSize"].AsInt;
                    var ids=table.Skip(start).Take(count).Where(p=>p["m_FileID"].AsInt==0).Select(p=>p["m_PathID"].AsLong).ToHashSet();
                    if(!ids.Contains(newGo) || newComponents.Any(id=>!ids.Contains(id)))throw new Exception("Cloned assets missing from preload range");
                }
            }
        }
        finally{manager.UnloadAll(true);}
    }
    static void BuildHierarchyFixture(string source,string destination)
    {
        var manager=Manager();
        try
        {
            manager.LoadClassPackage(Path.Combine(AppContext.BaseDirectory,"classdata.tpk"));
            var bundle=manager.LoadBundleFile(source,true);var file=manager.LoadAssetsFileFromBundle(bundle,0,false);manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
            var projectile=file.file.GetAssetsOfType(114).Select(i=>manager.GetBaseField(file,i)).First(r=>!r["configuration.damage"].IsDummy);
            long goId=projectile["m_GameObject.m_PathID"].AsLong;var go=manager.GetBaseField(file,goId);
            long parentId=go["m_Component.Array"].Children.Select(p=>p["component.m_PathID"].AsLong).Single(id=>file.file.GetAssetInfo(id).TypeId==4);
            var parent=manager.GetBaseField(file,parentId);
            long childGo=1,childTransform=2;while(file.file.GetAssetInfo(childGo)!=null || file.file.GetAssetInfo(childTransform)!=null){childGo+=2;childTransform+=2;}
            AssetTypeValueField Copy(AssetTypeValueField field){using var r=new AssetsFileReader(new MemoryStream(field.WriteToByteArray(file.file.Reader.BigEndian))){BigEndian=file.file.Reader.BigEndian};return field.TemplateField.MakeValue(r,manager.GetRefTypeManager(file));}
            var child=Copy(go);child["m_Name"].AsString="Synthetic child";
            var comp=child["m_Component.Array"].Children.Single(p=>p["component.m_PathID"].AsLong==parentId);child["m_Component.Array"].Children.Clear();child["m_Component.Array"].Children.Add(comp);comp["component.m_PathID"].AsLong=childTransform;
            var transform=Copy(parent);transform["m_GameObject.m_PathID"].AsLong=childGo;transform["m_Children.Array"].Children.Clear();transform["m_Father.m_PathID"].AsLong=parentId;
            var childPtr=Copy(parent["m_Father"]);childPtr["m_PathID"].AsLong=childTransform;parent["m_Children.Array"].Children.Add(childPtr);
            var edits=new List<AssetsReplacer>{new AssetsReplacerFromMemory(file.file,file.file.GetAssetInfo(parentId),parent),new AssetsReplacerFromMemory(childGo,1,ushort.MaxValue,child.WriteToByteArray(file.file.Reader.BigEndian)),new AssetsReplacerFromMemory(childTransform,4,ushort.MaxValue,transform.WriteToByteArray(file.file.Reader.BigEndian))};
            using var data=new MemoryStream();using(var w=new AssetsFileWriter(data))file.file.Write(w,0,edits);
            string name=bundle.file.GetFileName(0);using var writer=new AssetsFileWriter(destination);bundle.file.Write(writer,new List<BundleReplacer>{new BundleReplacerFromMemory(name,name,true,data.ToArray(),-1)});
        }
        finally{manager.UnloadAll(true);}
    }
}

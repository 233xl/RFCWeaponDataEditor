using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Security.Cryptography;

namespace RfcEditor;

static class DeletionProgressTest
{
    sealed class Collector : IProgress<SaveProgress>
    {
        public List<SaveProgress> Events {get;}=new();
        public void Report(SaveProgress value)=>Events.Add(value);
    }
    public static void Run(string fixture,string directory)
    {
        Directory.CreateDirectory(directory);
        byte[] original=SHA256.HashData(File.ReadAllBytes(fixture));
        foreach(var compression in new[]{SaveCompression.None,SaveCompression.LZ4,SaveCompression.LZMA})
        {
            using var doc=new BundleDocument(fixture){Compression=compression};var collector=new Collector();
            doc.SaveAs(Path.Combine(directory,"progress-"+compression+".rfc"),collector);
            if(collector.Events.First().Stage!=SaveStage.Writing || collector.Events.Last()!=new SaveProgress(SaveStage.Complete,100))throw new Exception("Progress start/end incorrect");
            SaveStage previous=SaveStage.Writing;int percent=-1;
            foreach(var e in collector.Events)
            {
                if(e.Percent is <0 or >100 || e.Stage<previous || (e.Stage==previous && e.Percent<percent))throw new Exception("Progress regressed");
                previous=e.Stage;percent=e.Percent;
            }
            var compress=collector.Events.Where(e=>e.Stage==SaveStage.Compressing).ToList();
            if(compression==SaveCompression.None && compress.Count!=0)throw new Exception("Fake compression progress for uncompressed file");
            if(compression!=SaveCompression.None && (!compress.Any(e=>e.Percent is >0 and <100) || compress.Last().Percent!=100))throw new Exception("Missing intermediate compression progress");
            Console.WriteLine($"PASS: {compression} progress, {compress.Count} real compression updates, ordered stages, completion after verification.");
        }
        string source=Path.Combine(directory,"source.rfc");File.Copy(fixture,source);
        using(var doc=new BundleDocument(source))
        {
            var bullet=doc.Components.First(c=>c.Kind=="Projectile");var weapons=doc.WeaponsFor(bullet);int before=doc.Components.Count;
            try{doc.DeleteComponent(bullet);throw new Exception("In-use bullet deleted");}catch(InvalidOperationException){}
            if(doc.Components.Count!=before || doc.PendingDeletions!=0)throw new Exception("Blocked deletion mutated state");
            var temporary=doc.CloneProjectile(bullet,"Temporary copy");long tempGo=temporary.GameObjectId;
            int removed=doc.DeleteComponent(temporary);
            if(removed<2 || doc.PendingAdditions!=0 || doc.Components.Count!=before)throw new Exception("Unsaved clone deletion failed");
            string unsaved=Path.Combine(directory,"deleted-unsaved.rfc");doc.SaveAs(unsaved);
            CheckAbsent(unsaved,tempGo);
            var copy=doc.CloneProjectile(bullet,"Saved copy",new[]{weapons[0]});long copyGo=copy.GameObjectId;
            byte[] beforeSave=SHA256.HashData(File.ReadAllBytes(source));string backup=doc.Save()??throw new Exception("Save skipped");
            if(!beforeSave.SequenceEqual(SHA256.HashData(File.ReadAllBytes(backup))))throw new Exception("Backup mismatch");
            doc.AssignProjectile(bullet,new[]{weapons[0]});doc.DeleteComponent(copy);
            try{doc.ResetComponent(weapons[0]);throw new Exception("Reset revived removed reference");}catch(InvalidOperationException){}
            _=doc.Save()??throw new Exception("Saved clone deletion skipped");CheckAbsent(source,copyGo);
            if(doc.HasPendingChanges || doc.PendingDeletions!=0)throw new Exception("Deletion dirty state not cleared");
            if(doc.Save()!=null)throw new Exception("No-op after deletion saved again");
            Console.WriteLine("PASS: in-use protection; unsaved/saved clone deletion; reset protection; preload cleanup; repeated save/backup.");
        }
        string weaponsFile=Path.Combine(directory,"weapons.rfc");File.Copy(fixture,weaponsFile);
        using(var doc=new BundleDocument(weaponsFile))
        {
            var weapons=doc.Components.Where(c=>c.Kind=="Weapon").ToList();var bullet=doc.ProjectileFor(weapons[0])!;
            var ptr=weapons[1].Root["parentWeapon"];
            ptr["m_FileID"].AsInt=0;ptr["m_PathID"].AsLong=weapons[0].Info.PathId;
            try{doc.DeleteComponent(weapons[0]);throw new Exception("Live non-projectile reference ignored");}catch(InvalidOperationException){}
            if(doc.PendingDeletions!=0 || !doc.Components.Contains(weapons[0]))throw new Exception("Failed reference scan mutated state");
            ptr["m_PathID"].AsLong=0;
            long firstGo=weapons[0].GameObjectId;doc.DeleteComponent(weapons[0]);
            string deletedWeapon=Path.Combine(directory,"deleted-weapon.rfc");doc.SaveAs(deletedWeapon);CheckAbsent(deletedWeapon,firstGo);
            using(var read=new BundleDocument(deletedWeapon))
                if(read.Components.Count(c=>c.Kind=="Weapon")!=weapons.Count-1 || read.ProjectileFor(read.Components.First(c=>c.Kind=="Weapon"))==null)throw new Exception("Remaining weapon was damaged");
            CheckRegistryCount(deletedWeapon,weapons.Count-1);
            foreach(var weapon in doc.Components.Where(c=>c.Kind=="Weapon").ToList())doc.DeleteComponent(weapon);
            doc.DeleteComponent(bullet);doc.SaveAs(Path.Combine(directory,"all-components-deleted.rfc"));
            using var empty=new BundleDocument(Path.Combine(directory,"all-components-deleted.rfc"));
            if(empty.Components.Count!=0)throw new Exception("Deleted components reappeared");
            CheckRegistryCount(Path.Combine(directory,"all-components-deleted.rfc"),0);
            Console.WriteLine("PASS: arbitrary inbound-reference protection; weapon hierarchy deletion; registry cleanup; remaining weapon intact; delete all components.");
        }
        string originals=Path.Combine(directory,"originals.rfc");File.Copy(fixture,originals);
        using(var doc=new BundleDocument(originals))
        {
            var bullet=doc.Components.First(c=>c.Kind=="Projectile");long oldGo=bullet.GameObjectId;
            var copy=doc.CloneProjectile(bullet,"Replacement",doc.WeaponsFor(bullet));
            doc.DeleteComponent(bullet);_ =doc.Save()??throw new Exception("Original deletion not saved");CheckAbsent(originals,oldGo);
            copy.Parameters.Single(p=>p.Path=="configuration.damage").Set("61");_=doc.Save()??throw new Exception("Later edit not saved");CheckAbsent(originals,oldGo);
            using var read=new BundleDocument(originals);if(read.Components.Any(c=>c.Info.PathId==bullet.Info.PathId) || read.Components.Where(c=>c.Kind=="Weapon").Any(w=>read.ProjectileFor(w)?.Name!="Replacement"))throw new Exception("Original removal or replacement binding failed");
            Console.WriteLine("PASS: original asset removers; replacement bindings; original deletion remains deleted across saves.");
        }
        string failure=Path.Combine(directory,"failure.rfc");File.Copy(fixture,failure);
        using(var doc=new BundleDocument(failure))
        {
            doc.Components.First(c=>c.Kind=="Projectile").Parameters.Single(p=>p.Path=="configuration.damage").Set("62");
            var collector=new Collector();byte[] hash=SHA256.HashData(File.ReadAllBytes(failure));
            using(var held=new FileStream(failure,FileMode.Open,FileAccess.Read,FileShare.Read))
                try{doc.Save(collector);throw new Exception("Locked overwrite succeeded");}catch(IOException){}
            if(collector.Events.Any(e=>e.Stage==SaveStage.Complete) || !hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(failure))) || !doc.HasPendingChanges)throw new Exception("Failed save reported success or lost edits");
            Console.WriteLine("PASS: failed save never reports completion and preserves original/dirty state.");
        }
        if(Directory.GetFiles(directory,"*.tmp").Length>0 || Directory.GetFiles(directory,"*.raw").Length>0)throw new Exception("Temporary files leaked");
        if(!original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(fixture))))throw new Exception("Fixture changed");
        Console.WriteLine("PASS: temporary files cleaned; original fixture unchanged.");
    }
    static void Inspect(string path,Action<AssetsManager,AssetsFileInstance> action)
    {
        var manager=new AssetsManager();
        try{manager.LoadClassPackage(Path.Combine(AppContext.BaseDirectory,"classdata.tpk"));var b=manager.LoadBundleFile(path,true);var f=manager.LoadAssetsFileFromBundle(b,0,false);manager.LoadClassDatabaseFromPackage(f.file.Metadata.UnityVersion);action(manager,f);}
        finally{manager.UnloadAll(true);}
    }
    static void CheckAbsent(string path,long id)=>Inspect(path,(manager,file)=>
    {
        if(file.file.GetAssetInfo(id)!=null)throw new Exception("Deleted GameObject still exists");
        foreach(var info in file.file.GetAssetsOfType(142))
            if(manager.GetBaseField(file,info)["m_PreloadTable.Array"].Children.Any(p=>p["m_FileID"].AsInt==0 && p["m_PathID"].AsLong==id))throw new Exception("Stale preload reference");
    });
    static void CheckRegistryCount(string path,int count)=>Inspect(path,(manager,file)=>
    {
        var registries=file.file.GetAssetsOfType(114).Select(i=>manager.GetBaseField(file,i)).Where(r=>!r["weaponEntries.Array"].IsDummy).ToList();
        if(registries.Count==0 || registries.Sum(r=>r["weaponEntries.Array"].Children.Count)!=count)throw new Exception("Weapon registry count incorrect");
    });
}

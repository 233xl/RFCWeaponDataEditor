using System.Security.Cryptography;

namespace RfcEditor;
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="--delete-progress-test")
        {
            try { DeletionProgressTest.Run(args[1],args[2]); return 0; }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
        if(args.Length>0 && args[0]=="--graph-test")
        {
            try { ProjectileGraphTest.Run(args[1],args[2]); return 0; }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
        if(args.Length>0 && args[0]=="--compression-test")
        {
            try { CompressionTest.Run(args[1],args[2]); return 0; }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
        if(args.Length>0 && args[0]=="--self-test")
        {
            try { SelfTest.Run(args[1],args[2]); return 0; }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new EditorForm(args.FirstOrDefault()));
        return 0;
    }
}

static class CompressionTest
{
    public static void Run(string source,string directory)
    {
        Directory.CreateDirectory(directory);
        byte[] originalHash=SHA256.HashData(File.ReadAllBytes(source));
        foreach(var mode in new[]{SaveCompression.None,SaveCompression.LZ4,SaveCompression.LZMA})
        {
            using var doc=new BundleDocument(source);
            doc.Compression=mode;
            var damage=doc.Components.First(c=>c.Kind=="Projectile").Parameters.Single(p=>p.Path=="configuration.damage");
            damage.Set("147.25");
            string path=Path.Combine(directory,mode+".rfc");
            var watch=System.Diagnostics.Stopwatch.StartNew();doc.SaveAs(path);watch.Stop();
            var loadWatch=System.Diagnostics.Stopwatch.StartNew();
            using var read=new BundleDocument(path);loadWatch.Stop();
            if(read.SourceCompression!=doc.OutputCompression)throw new Exception("Wrong compression: "+mode);
            if(read.Components.First(c=>c.Kind=="Projectile").Parameters.Single(p=>p.Path=="configuration.damage").Text!="147.25")throw new Exception("Lost damage: "+mode);
            Console.WriteLine($"{mode}: {new FileInfo(path).Length} bytes; save+verify {watch.Elapsed.TotalSeconds:F2}s; editor reopen {loadWatch.Elapsed.TotalSeconds:F2}s");
            // Auto must preserve compressed inputs and compress old uncompressed inputs.
            string auto=Path.Combine(directory,mode+"-auto.rfc");read.SaveAs(auto);
            using var autoDoc=new BundleDocument(auto);
            var expected=mode==SaveCompression.LZMA?AssetsTools.NET.AssetBundleCompressionType.LZMA:AssetsTools.NET.AssetBundleCompressionType.LZ4;
            if(autoDoc.SourceCompression!=expected)throw new Exception("Auto mode failed: "+mode);
        }
        string current=Path.Combine(directory,"inplace.rfc");File.Copy(Path.Combine(directory,"None.rfc"),current);
        byte[] before=SHA256.HashData(File.ReadAllBytes(current));
        using(var doc=new BundleDocument(current))
        {
            if(doc.ChangeCount!=0 || !doc.HasPendingChanges)throw new Exception("Compression-only save not detected");
            string backup=doc.Save()??throw new Exception("Missing compression-only backup");
            if(!before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(backup))))throw new Exception("Compression backup mismatch");
            if(doc.HasPendingChanges || doc.Save()!=null)throw new Exception("Saved compression state not cleared");
            doc.Compression=SaveCompression.LZMA;
            _=doc.Save()??throw new Exception("Compression-only LZMA save skipped");
            using var check=new BundleDocument(current);
            if(check.SourceCompression!=AssetsTools.NET.AssetBundleCompressionType.LZMA)throw new Exception("LZMA overwrite failed");
        }
        if(Directory.GetFiles(directory,"*.raw").Length!=0 || Directory.GetFiles(directory,"*.tmp").Length!=0)throw new Exception("Temporary files leaked");
        if(!originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))))throw new Exception("Original file changed");
        Console.WriteLine("PASS: None/LZ4/LZMA outputs; changed-field round trips; all asset/resource data verified; Auto preserves compressed inputs; compression-only overwrite/backup/no-op; original unchanged.");
    }
}
static class SelfTest
{
    public static void Run(string source,string output)
    {
        var before=SHA256.HashData(File.ReadAllBytes(source));
        using var doc=new BundleDocument(source);
        var projectile=doc.Components.First(c=>c.Kind=="Projectile");
        var weapon=doc.Components.First(c=>c.Kind=="Weapon");
        var damage=projectile.Parameters.First(p=>p.Path=="configuration.damage");
        var ammo=weapon.Parameters.First(p=>p.Path=="configuration.ammo");
        string oldDamage=damage.Text, oldAmmo=ammo.Text;
        damage.Set("233.5"); ammo.Set("17");
        try {ammo.Set("17.5");throw new Exception("Invalid integer accepted");}catch(FormatException){}
        try {damage.Set("NaN");throw new Exception("NaN accepted");}catch(FormatException){}
        try {projectile.Parameters.First(p=>p.Path=="m_GameObject.m_PathID").Set("1");throw new Exception("Reference editable");}catch(InvalidOperationException){}
        doc.SaveAs(output);
        using var reread=new BundleDocument(output);
        if(reread.Components.Single(c=>c.Info.PathId==projectile.Info.PathId).Parameters.Single(p=>p.Path==damage.Path).Text!="233.5")throw new Exception("Damage round trip failed");
        if(reread.Components.Single(c=>c.Info.PathId==weapon.Info.PathId).Parameters.Single(p=>p.Path==ammo.Path).Text!="17")throw new Exception("Ammo round trip failed");
        if(!before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))))throw new Exception("Original changed");
        try{doc.SaveAs(source);throw new Exception("Original overwrite accepted");}catch(InvalidOperationException){}
        try{doc.SaveAs(output);throw new Exception("Existing output overwrite accepted");}catch(IOException){}
        damage.Set(oldDamage);ammo.Set(oldAmmo);
        if(doc.ChangeCount!=0)throw new Exception("Reset failed");
        Console.WriteLine($"PASS: {doc.Components.Count} components; damage/ammo round trip; invalid values rejected; references protected; all assets and resource streams verified; original SHA256 unchanged.");
        Console.WriteLine(doc.Relation(projectile));
        foreach(var c in doc.Components)Console.WriteLine($"{c.Kind} / {c.Name} / {c.Info.PathId} / {c.Parameters.Count} fields");
        foreach(var warning in doc.Diagnostics)Console.WriteLine("WARN: "+warning);
        TestOverwrite(output);
        foreach(bool english in new[]{false,true})
        {
            L10n.English=english;
            foreach(var pair in L10n.Strings)
            {
                if(string.IsNullOrWhiteSpace(pair.Value.Zh)||string.IsNullOrWhiteSpace(pair.Value.En))throw new Exception("Missing translation: "+pair.Key);
                _=L10n.T(pair.Key,"a","b","c");
            }
            foreach(var field in L10n.Labels.Keys)if(string.IsNullOrEmpty(L10n.Label(field)))throw new Exception("Missing field translation");
            if(!doc.Relation(projectile).Contains(english?"Weapons sharing":"共用"))throw new Exception("Relation localization failed");
            Console.WriteLine(L10n.T("status",doc.UnityVersion,doc.Components.Count,doc.ChangeCount));
        }
        Console.WriteLine("PASS: both languages have complete strings and parameter labels.");
    }
    static void TestOverwrite(string output)
    {
        string target=output+".overwrite.rfc";
        File.Copy(output,target);
        using var doc=new BundleDocument(target);
        var damage=doc.Components.First(c=>c.Kind=="Projectile").Parameters.Single(p=>p.Path=="configuration.damage");
        byte[] firstHash=SHA256.HashData(File.ReadAllBytes(target));
        damage.Set("241");
        string backup1=doc.Save()??throw new Exception("No backup generated");
        if(!firstHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(backup1))))throw new Exception("First backup is not exact");
        if(doc.ChangeCount!=0 || damage.Original!="241")throw new Exception("Dirty state was not cleared");
        CheckValue(target,"241");
        byte[] secondHash=SHA256.HashData(File.ReadAllBytes(target));
        damage.Set("242");
        string backup2=doc.Save()??throw new Exception("No second backup");
        if(backup1==backup2 || !secondHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(backup2))))throw new Exception("Second backup is not exact");
        CheckValue(target,"242");
        damage.Set("999");damage.Set(damage.Original);
        if(doc.ChangeCount!=0 || damage.Text!="242")throw new Exception("Reset to last save failed");
        damage.Set(damage.Initial);
        _=doc.Save();CheckValue(target,"233.5");
        int backups=Directory.GetFiles(Path.GetDirectoryName(target)!,Path.GetFileName(target)+".*.bak").Length;
        if(doc.Save()!=null || backups!=Directory.GetFiles(Path.GetDirectoryName(target)!,Path.GetFileName(target)+".*.bak").Length)throw new Exception("No-op save created backup");
        // Another application's read-only sharing lock must leave the original intact.
        damage.Set("250");byte[] beforeFailure=File.ReadAllBytes(target);
        using(var guard=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            try{doc.Save();throw new Exception("Locked source was overwritten");}catch(IOException){}
        }
        if(!beforeFailure.SequenceEqual(File.ReadAllBytes(target)) || doc.ChangeCount==0)throw new Exception("Failed save lost data/dirty state");
        File.AppendAllText(target,"external-change");
        byte[] external=File.ReadAllBytes(target);
        try{doc.Save();throw new Exception("External edit was overwritten");}catch(IOException){}
        if(!external.SequenceEqual(File.ReadAllBytes(target)))throw new Exception("External edit not preserved");
        if(Directory.GetFiles(Path.GetDirectoryName(target)!,Path.GetFileName(target)+".*.tmp").Length!=0)throw new Exception("Temporary file leaked");
        if(backups!=Directory.GetFiles(Path.GetDirectoryName(target)!,Path.GetFileName(target)+".*.bak").Length)throw new Exception("Failed save created backup");
        Console.WriteLine("PASS: repeated atomic saves; exact unique backups; reset/dirty state; return to initial values; no-op; locked/external changes rejected; failure cleanup.");
    }
    static void CheckValue(string path,string expected)
    {
        using var check=new BundleDocument(path);
        if(check.Components.First(c=>c.Kind=="Projectile").Parameters.Single(p=>p.Path=="configuration.damage").Text!=expected)throw new Exception("Overwrite value mismatch");
        if(check.Components.First(c=>c.Kind=="Weapon").Parameters.Single(p=>p.Path=="configuration.ammo").Text!="17")throw new Exception("Prior weapon edit lost");
    }
}

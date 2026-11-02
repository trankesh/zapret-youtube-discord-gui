using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ZapretDesktop
{
    internal static class InstallerRuntime
    {
        // Profile transfer is shared with standalone startup. The old installer
        // commands remain only so previously installed copies can be removed.
        public static int Run(string[] args)
        {
            try {
                Engine.RequireAdmin();
                if (args[0]=="--installer-running" && args.Length==1) return SingleInstance.IsCurrentUserRunning() ? 2 : 0;
                if (args[0]=="--installer-info" && args.Length==2) {
                    string data="";
                    try { var record=AutoStart.ReadRecord(); if (record!=null && File.Exists(Path.Combine(record.Data,"settings.json"))) data=record.Data; } catch { }
                    if (data.Length==0) { string legacy=AutoStart.FindLegacySource(); if (legacy!=null) { string candidate=Path.Combine(Path.GetDirectoryName(legacy),"data"); if (File.Exists(Path.Combine(candidate,"settings.json"))) data=candidate; } }
                    File.WriteAllText(args[1],"[Portable]\r\nDataPath="+data+"\r\n",Encoding.Unicode); return 0;
                }
                if (!AutoStart.IsInstalledAction(ApplicationPaths.SourceExecutable,ApplicationPaths.SourceExecutable) || !File.Exists(Path.Combine(AutoStart.InstalledDirectory,"installed.mode"))) throw new IOException("Команда доступна только установленному приложению.");
                ApplicationPaths.Configure(false);
                if (args[0]=="--installer-maintain" && args.Length<=2) {
                    string importedSource=AutoStart.FindLegacySource();
                    if (args.Length==2 && !String.IsNullOrWhiteSpace(args[1])) {
                        if (ImportData(args[1],ApplicationPaths.DataDirectory)) importedSource=AutoStart.LegacySourceForData(args[1]) ?? importedSource;
                    }
                    AutoStart.RefreshFromPortable(ApplicationPaths.SourceExecutable,importedSource); return 0;
                }
                if (args[0]=="--installer-uninstall" && args.Length==1) { AutoStart.RemoveInstalledRegistrations(); return 0; }
                throw new ArgumentException("Неизвестная команда установщика.");
            } catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
        }
        internal static string Rebase(string path,string source,string target)
        {
            if (String.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return path;
            string root=Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string full=Path.GetFullPath(path);
            if (AutoStart.SamePath(full,source)) return target;
            if (full.StartsWith(root,StringComparison.OrdinalIgnoreCase)) return Path.Combine(target,full.Substring(root.Length));
            return path;
        }
        static void CheckEntry(string path)
        {
            if ((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) throw new IOException("Перенос ссылок/junction не поддерживается. Выбери обычную локальную копию папки data: " + path);
        }
        static void CopyTree(string source,string target,ref long size,ref int count)
        {
            CheckEntry(source); Directory.CreateDirectory(target);
            foreach (string entry in Directory.GetFileSystemEntries(source)) {
                CheckEntry(entry);
                if (Directory.Exists(entry)) CopyTree(entry,Path.Combine(target,Path.GetFileName(entry)),ref size,ref count);
                else {
                    size+=new FileInfo(entry).Length; count++;
                    if (size>1024L*1024*1024 || count>50000) throw new IOException("Папка данных слишком большая для автоматического переноса.");
                    File.Copy(entry,Path.Combine(target,Path.GetFileName(entry)),false);
                }
            }
        }
        internal static bool ImportData(string source,string target)
        {
            source=Path.GetFullPath(source); target=Path.GetFullPath(target);
            if (AutoStart.SamePath(source,target) || File.Exists(Path.Combine(target,"settings.json"))) return false;
            if (Directory.Exists(target) && Directory.GetFileSystemEntries(target).Length!=0) throw new IOException("Каталог установленного профиля уже содержит данные. Автоматическая замена отменена.");
            CheckEntry(source);
            if (target.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Каталог назначения не может быть вложен в исходный.");
            CheckEntry(Path.Combine(source,"settings.json"));
            var serializer=new JavaScriptSerializer(); var settings=serializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(source,"settings.json")));
            if (settings==null) throw new IOException("Не удалось прочитать settings.json.");
            settings.Root=Rebase(settings.Root,source,target);
            if (settings.HostsReceipt!=null) settings.HostsReceipt.Backup=Rebase(settings.HostsReceipt.Backup,source,target);
            settings.DnsBackup=Rebase(settings.DnsBackup,source,target);
            if (settings.DnsBackupHistory!=null) settings.DnsBackupHistory=settings.DnsBackupHistory.Select(p=>Rebase(p,source,target)).ToArray();
            string staging=target+".import-"+Guid.NewGuid().ToString("N"); Directory.CreateDirectory(staging);
            // Failed staging folders are retained for recovery; existing user data
            // is never recursively removed or overwritten by the importer.
            long size=0; int count=0;
            foreach (string name in new[]{"bundles","backups"}) if (Directory.Exists(Path.Combine(source,name))) CopyTree(Path.Combine(source,name),Path.Combine(staging,name),ref size,ref count);
            foreach (string name in new[]{"last-results.json","last-results.txt","zapret.log"}) if (File.Exists(Path.Combine(source,name))) { string file=Path.Combine(source,name); CheckEntry(file); size+=new FileInfo(file).Length; if (size>1024L*1024*1024) throw new IOException("Папка данных слишком большая для автоматического переноса."); File.Copy(file,Path.Combine(staging,name)); }
            File.WriteAllText(Path.Combine(staging,"settings.json"),serializer.Serialize(settings),new UTF8Encoding(false));
            if (Directory.Exists(target)) {
                if (Directory.GetFileSystemEntries(target).Length!=0) throw new IOException("Каталог установленного профиля уже содержит данные. Автоматическая замена отменена.");
                Directory.Delete(target,false);
            }
            Directory.Move(staging,target); return true;
        }
    }
}

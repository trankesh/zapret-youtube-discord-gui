using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using Microsoft.Win32;

namespace ZapretDesktop
{
    internal static class ApplicationPaths
    {
        public static string SourceExecutable = Assembly.GetExecutingAssembly().Location;
        public static string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZapretDesktop");
        public static string MigrationError;
        public static void Configure(bool migrateLegacy=true)
        {
            string legacy=Path.Combine(Path.GetDirectoryName(SourceExecutable),"data");
            if (AutoStart.IsProtectedExecutable(SourceExecutable)) {
                var record=AutoStart.ReadRecord();
                if (record==null || !AutoStart.SamePath(record.Snapshot,SourceExecutable)) throw new IOException("Не найдена задача для защищённой копии. Запусти исходный Zapret.exe и заново настрой автозапуск.");
                SourceExecutable=record.Source; legacy=record.Data;
            }
            string folder=Path.GetDirectoryName(SourceExecutable);
            bool installed=File.Exists(Path.Combine(folder,"installed.mode"));
            bool portableMode=File.Exists(Path.Combine(folder,"portable.mode"));
            DataDirectory=DefaultData(SourceExecutable,installed,Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),portableMode);
            MigrationError=null;
            if (!migrateLegacy) return;
            if (portableMode && !installed || File.Exists(Path.Combine(DataDirectory,"settings.json"))) return;
            if (!File.Exists(Path.Combine(legacy,"settings.json"))) {
                try { var previous=AutoStart.ReadRecord(); if (previous!=null) legacy=previous.Data; } catch { }
            }
            if (File.Exists(Path.Combine(legacy,"settings.json")) && !AutoStart.SamePath(legacy,DataDirectory)) {
                try { InstallerRuntime.ImportData(legacy,DataDirectory); }
                catch (Exception error) { DataDirectory=legacy; MigrationError="Не удалось перенести профиль в AppData: " + error.Message + "\n\nЧтобы сохранить настройки, пока используется прежняя папка:\n" + legacy; }
            }
        }
        internal static string DefaultData(string exe,bool installed,string localData,bool portableMode=false) { return portableMode && !installed ? Path.Combine(Path.GetDirectoryName(exe),"data") : Path.Combine(localData,"ZapretDesktop"); }
    }
    internal interface IStartupTasks
    {
        string Read();
        void Register(string xml);
        void Delete();
    }
    internal sealed class WindowsStartupTasks : IStartupTasks
    {
        static void Release(object value) { if (value!=null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        T Use<T>(Func<dynamic,T> action)
        {
            object service=null, folder=null;
            try { dynamic scheduler=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true)); service=scheduler; scheduler.Connect(); folder=scheduler.GetFolder("\\"); return action(folder); }
            finally { Release(folder); Release(service); }
        }
        public string Read()
        {
            return Use<string>(folder => {
                object task=null;
                try { dynamic registered=folder.GetTask(AutoStart.TaskName); task=registered; return (string)registered.Xml; }
                catch (Exception e) { if (AutoStart.IsMissingTask(e)) return null; throw; }
                finally { Release(task); }
            });
        }
        public void Register(string xml) { Use<int>(folder => { object task=folder.RegisterTask(AutoStart.TaskName,xml,6,AutoStart.UserSid,null,3,"O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;" + AutoStart.UserSid + ")"); Release(task); return 0; }); }
        public void Delete() { Use<int>(folder => { folder.DeleteTask(AutoStart.TaskName,0); return 0; }); }
    }
    internal sealed class StartupRecord
    {
        public string Marker { get; set; }
        public string Source { get; set; }
        public string Data { get; set; }
        public string Snapshot { get; set; }
    }
    public static class AutoStart
    {
        const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        const string LegacyName="ZapretDesktop";
        const string Marker="ZapretDesktop.ElevatedStartup.v1";
        const string Namespace="http://schemas.microsoft.com/windows/2004/02/mit/task";
        public static string ReadError;
        internal static bool IsMissingTask(Exception error)
        {
            // COM interop maps ERROR_FILE_NOT_FOUND to FileNotFoundException on
            // Windows, not necessarily COMException. A missing task is OFF.
            return (error is FileNotFoundException || error is COMException) && error.HResult==unchecked((int)0x80070002);
        }
        internal static string UserSid { get { return WindowsIdentity.GetCurrent().User.Value; } }
        internal static string TaskName { get { return "ZapretDesktop-" + UserSid; } }
        internal static string ProtectedDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ZapretDesktop","Startup",UserSid); } }
        internal static string InstalledDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ZapretDesktop"); } }
        internal static bool IsInstalledAction(string source,string action) { return SamePath(source,Path.Combine(InstalledDirectory,"Zapret.exe")) && SamePath(source,action); }
        public static string Command(string exe) { return Engine.Quote(exe) + " --startup"; }
        internal static bool SamePath(string left,string right) { return String.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.OrdinalIgnoreCase); }
        internal static bool IsProtectedExecutable(string exe) { return SamePath(Path.GetDirectoryName(exe),ProtectedDirectory); }
        internal static string TaskXml(string source,string data,string snapshot,string sid)
        {
            var record=new StartupRecord { Marker=Marker,Source=Path.GetFullPath(source),Data=Path.GetFullPath(data),Snapshot=Path.GetFullPath(snapshot) };
            var document=new XmlDocument(); var task=document.CreateElement("Task",Namespace); task.SetAttribute("version","1.2"); document.AppendChild(task);
            Action<XmlElement,string,string> add = (parent,name,value) => { var item=document.CreateElement(name,Namespace); item.InnerText=value; parent.AppendChild(item); };
            Func<XmlElement,string,XmlElement> group = (parent,name) => { var item=document.CreateElement(name,Namespace); parent.AppendChild(item); return item; };
            var info=group(task,"RegistrationInfo"); add(info,"Description",Marker); add(info,"Author",sid);
            var trigger=group(group(task,"Triggers"),"LogonTrigger"); add(trigger,"Enabled","true"); add(trigger,"UserId",sid); add(trigger,"Delay","PT10S");
            var principal=group(group(task,"Principals"),"Principal"); principal.SetAttribute("id","Owner"); add(principal,"UserId",sid); add(principal,"LogonType","InteractiveToken"); add(principal,"RunLevel","HighestAvailable");
            var settings=group(task,"Settings"); add(settings,"MultipleInstancesPolicy","IgnoreNew"); add(settings,"DisallowStartIfOnBatteries","false"); add(settings,"StopIfGoingOnBatteries","false"); add(settings,"AllowHardTerminate","false"); add(settings,"StartWhenAvailable","true"); add(settings,"Enabled","true"); add(settings,"ExecutionTimeLimit","PT0S");
            add(task,"Data",new JavaScriptSerializer().Serialize(record));
            var actions=group(task,"Actions"); actions.SetAttribute("Context","Owner"); var exec=group(actions,"Exec"); add(exec,"Command",record.Snapshot); add(exec,"Arguments","--startup --elevated"); add(exec,"WorkingDirectory",Path.GetDirectoryName(record.Snapshot));
            return document.OuterXml;
        }
        internal static StartupRecord ParseTask(string xml,string sid,string directory)
        {
            if (String.IsNullOrEmpty(xml)) return null;
            var document=new XmlDocument { XmlResolver=null };
            using (var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536 })) document.Load(reader);
            var ns=new XmlNamespaceManager(document.NameTable); ns.AddNamespace("t",Namespace);
            Func<string,string> value=path => { var node=document.SelectSingleNode("/t:Task/"+path,ns); return node==null ? "" : node.InnerText; };
            if (document.SelectNodes("/t:Task/t:Principals/t:Principal",ns).Count!=1 || value("t:Principals/t:Principal/@id")!="Owner" || value("t:Actions/@Context")!="Owner") throw new IOException("Неизвестный контекст задачи автозапуска.");
            var record=new JavaScriptSerializer().Deserialize<StartupRecord>(value("t:Data"));
            if (record==null || record.Marker!=Marker || value("t:RegistrationInfo/t:Description")!=Marker || value("t:Principals/t:Principal/t:UserId")!=sid || value("t:Principals/t:Principal/t:RunLevel")!="HighestAvailable" || value("t:Principals/t:Principal/t:LogonType")!="InteractiveToken" || document.SelectNodes("/t:Task/t:Actions/*",ns).Count!=1 || document.SelectNodes("/t:Task/t:Triggers/*",ns).Count!=1 || value("t:Triggers/t:LogonTrigger/t:UserId")!=sid || value("t:Actions/t:Exec/t:Arguments")!="--startup --elevated" || (!SamePath(Path.GetDirectoryName(record.Snapshot),directory) && !IsInstalledAction(record.Source,record.Snapshot)) || !SamePath(record.Snapshot,value("t:Actions/t:Exec/t:Command")) || !SamePath(Path.GetDirectoryName(record.Snapshot),value("t:Actions/t:Exec/t:WorkingDirectory")) || !Path.IsPathRooted(record.Source) || !Path.IsPathRooted(record.Data)) throw new IOException("Задача автозапуска имеет неизвестные параметры. Она не будет перезаписана.");
            return record;
        }
        internal static StartupRecord ReadRecord() { return ParseTask(new WindowsStartupTasks().Read(),UserSid,ProtectedDirectory); }
        static bool TaskEnabled(string xml)
        {
            if (xml==null) return false;
            var document=new XmlDocument { XmlResolver=null }; document.LoadXml(xml);
            var ns=new XmlNamespaceManager(document.NameTable); ns.AddNamespace("t",Namespace);
            return document.SelectNodes("/t:Task/t:Settings/t:Enabled[text()='false'] | /t:Task/t:Triggers/t:LogonTrigger/t:Enabled[text()='false']",ns).Count==0;
        }
        static string Legacy()
        {
            using (var key=Registry.CurrentUser.OpenSubKey(RunKey)) return key==null ? "" : Convert.ToString(key.GetValue(LegacyName));
        }
        internal static bool IsLegacy(string value,string exe) { return String.Equals(value,Command(exe),StringComparison.OrdinalIgnoreCase); }
        internal static string LegacySourceForData(string value,string data)
        {
            var match=System.Text.RegularExpressions.Regex.Match(value ?? "", "^\"([^\"]+)\" --startup$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            string source=match.Groups[1].Value;
            return SamePath(Path.Combine(Path.GetDirectoryName(source),"data"),data) ? source : null;
        }
        internal static string LegacySourceForData(string data) { return LegacySourceForData(Legacy(),data); }
        internal static string FindLegacySource()
        {
            var match=System.Text.RegularExpressions.Regex.Match(Legacy() ?? "", "^\"([^\"]+)\" --startup$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success || !Path.IsPathRooted(match.Groups[1].Value) || !File.Exists(match.Groups[1].Value)) return null;
            string source=match.Groups[1].Value;
            try { return System.Diagnostics.FileVersionInfo.GetVersionInfo(source).ProductName=="Zapret Desktop" ? source : null; }
            catch (IOException) { return null; }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }
        static void RemoveLegacy(string source,string previousSource)
        {
            using (var key=Registry.CurrentUser.OpenSubKey(RunKey,true)) {
                if (key==null) return; string value=Convert.ToString(key.GetValue(LegacyName));
                if (IsLegacy(value,source) || previousSource!=null && IsLegacy(value,previousSource)) key.DeleteValue(LegacyName,false);
            }
        }
        public static bool Enabled(string exe)
        {
            return ReadState(new WindowsStartupTasks(),exe,Legacy);
        }
        internal static bool ReadState(IStartupTasks store,string exe,Func<string> legacy)
        {
            ReadError=null;
            try { string xml=store.Read(); ParseTask(xml,UserSid,ProtectedDirectory); return TaskEnabled(xml) || IsLegacy(legacy(),exe); }
            catch (Exception error) { ReadError=error.Message; return false; }
        }
        internal static void ReplaceTask(IStartupTasks store,string xml,Func<string,bool> verify,Action removeLegacy)
        {
            string old=store.Read(); bool attempted=false;
            try { attempted=true; store.Register(xml); if (!verify(store.Read())) throw new IOException("Планировщик не подтвердил параметры автозапуска."); removeLegacy(); }
            catch (Exception error) {
                if (attempted) try { if (old==null) { if (store.Read()!=null) store.Delete(); } else store.Register(old); }
                catch (Exception rollback) { throw new IOException(error.Message + " Откат задачи не выполнен: " + rollback.Message,error); }
                throw;
            }
        }
        public static void Set(string exe,bool enabled,string importedLegacySource=null)
        {
            Engine.RequireAdmin(); exe=Path.GetFullPath(exe);
            var store=new WindowsStartupTasks(); string old=store.Read(); var previous=ParseTask(old,UserSid,ProtectedDirectory);
            if (!enabled) { if (old!=null) store.Delete(); RemoveLegacy(exe,previous==null ? null : previous.Source); return; }
            string data=ApplicationPaths.DataDirectory;
            string snapshot=SecureSnapshot(exe);
            string xml=TaskXml(exe,data,snapshot,UserSid);
            ReplaceTask(store,xml,registered => { var record=ParseTask(registered,UserSid,ProtectedDirectory); return record!=null && SamePath(record.Source,exe) && SamePath(record.Data,data) && SamePath(record.Snapshot,snapshot) && TaskEnabled(registered); },()=>{ RemoveLegacy(exe,previous==null ? null : previous.Source); if (importedLegacySource!=null) RemoveLegacy(exe,importedLegacySource); });
        }
        // Only the explicitly elevated portable GUI may refresh its protected copy.
        // A scheduled startup never imports executable bytes from writable storage.
        public static bool RefreshFromPortable(string exe,string importedLegacySource=null)
        {
            if (IsProtectedExecutable(exe)) return false;
            string xml=new WindowsStartupTasks().Read(); var record=ParseTask(xml,UserSid,ProtectedDirectory);
            bool legacy=IsLegacy(Legacy(),exe) || importedLegacySource!=null && IsLegacy(Legacy(),importedLegacySource);
            if (!legacy && !TaskEnabled(xml)) return false;
            if (!legacy && record!=null && SamePath(record.Source,exe) && SamePath(record.Data,ApplicationPaths.DataDirectory) && SnapshotMatches(exe,record.Snapshot)) return false;
            Set(exe,true,importedLegacySource); return true;
        }
        static string SnapshotName(byte[] bytes,byte[] config) { return "Zapret-" + HostsUpdate.Hash(bytes.Concat(config).ToArray()) + ".exe"; }
        static bool SnapshotMatches(string source,string snapshot)
        {
            if (IsInstalledAction(source,snapshot)) return File.Exists(source) && File.Exists(source+".config");
            if (!File.Exists(source) || !File.Exists(snapshot) || !File.Exists(snapshot+".config")) return false;
            byte[] bytes=File.ReadAllBytes(source),config=RuntimeDefaults.Config(source);
            return String.Equals(Path.GetFileName(snapshot),SnapshotName(bytes,config),StringComparison.OrdinalIgnoreCase) && HostsUpdate.Hash(bytes)==HostsUpdate.Hash(File.ReadAllBytes(snapshot)) && HostsUpdate.Hash(config)==HostsUpdate.Hash(File.ReadAllBytes(snapshot+".config"));
        }
        static void NoReparse(string path)
        {
            for (string current=Path.GetFullPath(path); !String.IsNullOrEmpty(current); current=Path.GetDirectoryName(current))
                if ((Directory.Exists(current)||File.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw new IOException("Путь защищённого автозапуска не может содержать ссылки или junction: " + current);
        }
        internal static DirectorySecurity DirectoryAcl(string sid)
        {
            var acl=new DirectorySecurity(); acl.SetAccessRuleProtection(true,false);
            var admins=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null); var system=new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null);
            acl.SetOwner(admins);
            foreach (var identity in new[]{admins,system}) acl.AddAccessRule(new FileSystemAccessRule(identity,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow)); return acl;
        }
        static string SecureSnapshot(string source)
        {
            if (IsInstalledAction(source,source) && File.Exists(Path.Combine(InstalledDirectory,"installed.mode"))) {
                NoReparse(source); NoReparse(source+".config");
                if (!File.Exists(source+".config")) throw new IOException("Не найден config установленного приложения.");
                Directory.SetAccessControl(InstalledDirectory,DirectoryAcl("S-1-5-32-545"));
                return source;
            }
            byte[] bytes=File.ReadAllBytes(source),config=RuntimeDefaults.Config(source);
            string directory=ProtectedDirectory; NoReparse(directory);
            foreach (string parent in new[]{Path.GetDirectoryName(Path.GetDirectoryName(directory)),Path.GetDirectoryName(directory)}) { Directory.CreateDirectory(parent); Directory.SetAccessControl(parent,DirectoryAcl("S-1-5-32-545")); }
            Directory.CreateDirectory(directory); Directory.SetAccessControl(directory,DirectoryAcl(UserSid));
            string snapshot=Path.Combine(directory,SnapshotName(bytes,config));
            foreach (var item in new[]{Tuple.Create(snapshot,bytes),Tuple.Create(snapshot+".config",config)}) {
                NoReparse(item.Item1);
                if (File.Exists(item.Item1)) { if (HostsUpdate.Hash(File.ReadAllBytes(item.Item1))!=HostsUpdate.Hash(item.Item2)) throw new IOException("Защищённая копия повреждена: " + item.Item1); }
                else using (var file=new FileStream(item.Item1,FileMode.CreateNew,FileAccess.Write,FileShare.None)) file.Write(item.Item2,0,item.Item2.Length);
                var acl=new FileSecurity(); acl.SetAccessRuleProtection(true,false); var admins=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null); acl.SetOwner(admins);
                acl.AddAccessRule(new FileSystemAccessRule(admins,FileSystemRights.FullControl,AccessControlType.Allow)); acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.FullControl,AccessControlType.Allow)); acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(UserSid),FileSystemRights.ReadAndExecute,AccessControlType.Allow)); File.SetAccessControl(item.Item1,acl);
            }
            return snapshot;
        }
        public static bool BypassEnabled(string root)
        {
            if (!Bundle.Valid(root) || !Engine.ServiceMatches(root)) return false;
            using (var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret")) return key!=null && Convert.ToInt32(key.GetValue("Start",3))==2;
        }
        internal static bool IsInstalledRegistration(string xml,string sid)
        {
            string directory=Path.Combine(InstalledDirectory,"Startup",sid);
            var record=ParseTask(xml,sid,directory);
            return record!=null && SamePath(record.Source,Path.Combine(InstalledDirectory,"Zapret.exe"));
        }
        internal static void RemoveInstalledRegistrations()
        {
            Engine.RequireAdmin(); object service=null,folder=null,tasks=null;
            try {
                dynamic scheduler=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true)); service=scheduler; scheduler.Connect(); dynamic root=scheduler.GetFolder("\\"); folder=root;
                dynamic collection=root.GetTasks(1); tasks=collection; var names=new System.Collections.Generic.List<string>();
                for (int index=1;index<=collection.Count;index++) {
                    dynamic task=collection[index];
                    try {
                        string name=task.Name;
                        if (!name.StartsWith("ZapretDesktop-S-1-",StringComparison.Ordinal)) continue;
                        string sid=name.Substring("ZapretDesktop-".Length); new SecurityIdentifier(sid);
                        if (IsInstalledRegistration((string)task.Xml,sid)) names.Add(name);
                    } finally { if (Marshal.IsComObject(task)) Marshal.FinalReleaseComObject(task); }
                }
                foreach (string name in names) root.DeleteTask(name,0);
                RemoveLegacy(Path.Combine(InstalledDirectory,"Zapret.exe"),null);
            } finally { foreach (object value in new[]{tasks,folder,service}) if (value!=null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        }
    }
}

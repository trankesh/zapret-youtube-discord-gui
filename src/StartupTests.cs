using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Xml;

namespace ZapretDesktop
{
    internal static class StartupTests
    {
        sealed class Store : IStartupTasks
        {
            public string Xml;
            public int Failures;
            public bool CorruptOnce;
            public int Writes;
            public string Read() { return Xml; }
            public void Register(string xml) { Writes++; if (Failures-->0) throw new IOException("simulated registration failure"); Xml=CorruptOnce ? "unexpected task" : xml; CorruptOnce=false; }
            public void Delete() { Xml=null; }
        }
        static void Check(bool condition,string message) { if (!condition) throw new Exception("FAILED startup: " + message); }
        static void Refused(Action action,string message) { bool failed=false; try { action(); } catch { failed=true; } Check(failed,message); }
        public static void Run()
        {
            string sid=AutoStart.UserSid, directory=AutoStart.ProtectedDirectory;
            string source="C:\\User & test\\Запрет\\Zapret.exe",data="C:\\User & test\\Запрет\\data",snapshot=Path.Combine(directory,"Zapret-test.exe");
            string xml=AutoStart.TaskXml(source,data,snapshot,sid);
            Check(AutoStart.IsMissingTask(new FileNotFoundException("task absent")),"native FileNotFoundException means task is not configured");
            Check(AutoStart.IsMissingTask(new System.Runtime.InteropServices.COMException("task absent",unchecked((int)0x80070002))),"COM variant of missing task is supported");
            Check(!AutoStart.IsMissingTask(new UnauthorizedAccessException("access denied")) && !AutoStart.IsMissingTask(new System.Runtime.InteropServices.COMException("access denied",unchecked((int)0x80070005))),"access denied remains a real error");
            Check(!AutoStart.ReadState(new Store(),source,()=>"") && AutoStart.ReadError==null,"no task is an available OFF switch, not a read error");
            Check(AutoStart.ReadState(new Store(),source,()=>AutoStart.Command(source)) && AutoStart.ReadError==null,"legacy startup readable when no task exists");
            var record=AutoStart.ParseTask(xml,sid,directory);
            Check(record.Source==source && record.Data==data && record.Snapshot==snapshot,"XML preserves Unicode, spaces and ampersands");
            string account=System.Security.SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            Check(AutoStart.ParseTask(xml.Replace("<UserId>"+sid+"</UserId>","<UserId>"+account+"</UserId>"),sid,directory)!=null,"scheduler account names resolve to the same SID");
            Check(!AutoStart.SameUser("S-1-5-21-111-222-333-1001",sid),"foreign user is not accepted");
            Check(!AutoStart.ParseTask(xml.Replace("--startup --elevated","  --startup\t--elevated  "),sid,directory).NeedsUpgrade,"scheduler whitespace is accepted");
            string legacyTask=xml.Replace("--startup --elevated","--startup");
            Check(AutoStart.ParseTask(legacyTask,sid,directory).NeedsUpgrade,"known old task is identified for upgrade");
            Check(AutoStart.ParseTask(xml.Replace("ElevatedStartup.v2","ElevatedStartup.v1"),sid,directory).NeedsUpgrade,"previous GUI task format upgrades to v2");
            int launches=0; var bridge=new Store {Xml=xml};
            Check(AutoStart.TryStartScheduled(bridge,()=>launches++) && launches==1 && bridge.Writes==0,"legacy logon launches trusted scheduled action without changing it");
            Check(!AutoStart.TryStartScheduled(new Store {Xml=legacyTask},()=>launches++) && launches==1,"old arguments cannot recurse through scheduled bridge");
            Check(!AutoStart.TryStartScheduled(new Store(),()=>launches++) && launches==1,"missing task does not request elevation or launch anything");
            Check(!AutoStart.TryStartScheduled(new Store {Xml=xml.Replace("HighestAvailable","LeastPrivilege")},()=>launches++) && launches==1,"unprivileged task is not launched");
            string moved=Path.Combine(Path.GetTempPath(),"Zapret-moved-"+Guid.NewGuid().ToString("N"),"Zapret.exe");
            Check(AutoStart.OwnedLegacySource(AutoStart.Command(moved))==moved,"moved old Run entry can be migrated using current EXE");
            Check(AutoStart.OwnedLegacySource(AutoStart.Command(Path.ChangeExtension(moved,"cmd")))==null && AutoStart.OwnedLegacySource(AutoStart.Command(moved)+" --unknown")==null,"unrelated legacy commands remain untouched");
            Check(xml.Contains("HighestAvailable") && xml.Contains("InteractiveToken") && !xml.Contains("Password") && !xml.Contains("S-1-5-18"),"interactive user, elevated level, no SYSTEM/password");
            Check(xml.Contains("PT0S") && xml.Contains("IgnoreNew") && xml.Contains("PT10S"),"unlimited runtime, no duplicate instances, login delay");
            Refused(()=>AutoStart.ParseTask(xml.Replace("HighestAvailable","LeastPrivilege"),sid,directory),"reject wrong run level");
            Refused(()=>AutoStart.ParseTask(xml.Replace("--startup","--unexpected"),sid,directory),"reject unexpected command arguments");
            Refused(()=>AutoStart.ParseTask(xml,sid,Path.GetTempPath()),"reject writable executable directory");
            Refused(()=>AutoStart.ParseTask(xml,"S-1-5-21-111-222-333-1001",directory),"reject different user");
            Refused(()=>AutoStart.ParseTask(xml.Replace("Context=\"Owner\"","Context=\"Other\""),sid,directory),"reject unexpected execution context");
            var acl=AutoStart.DirectoryAcl(sid);
            Check(acl.AreAccessRulesProtected && ((SecurityIdentifier)acl.GetOwner(typeof(SecurityIdentifier))).IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid),"protected ACL and administrator ownership");
            var rules=acl.GetAccessRules(true,false,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
            var user=rules.Single(r=>r.IdentityReference.Value==sid);
            Check((user.FileSystemRights&FileSystemRights.ReadAndExecute)==FileSystemRights.ReadAndExecute && (user.FileSystemRights&(FileSystemRights.Write|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership|FileSystemRights.Delete))==0,"user can execute but cannot replace protected startup binary");
            Check(AutoStart.IsLegacy(AutoStart.Command(source),source) && !AutoStart.IsLegacy(AutoStart.Command("C:\\other\\Zapret.exe"),source),"legacy migration only matches owned command");
            var store=new Store { Xml="old" }; bool removed=false;
            AutoStart.ReplaceTask(store,xml,value=>value==xml,()=>removed=true);
            Check(store.Xml==xml && removed,"migration removes legacy only after task verification");
            store=new Store { Xml="old",Failures=1 }; removed=false;
            Refused(()=>AutoStart.ReplaceTask(store,xml,value=>true,()=>removed=true),"registration failure reported");
            Check(store.Xml=="old" && !removed,"registration failure retains old task and legacy startup");
            store=new Store { Xml="old",CorruptOnce=true }; removed=false;
            Refused(()=>AutoStart.ReplaceTask(store,xml,value=>value==xml,()=>removed=true),"verification failure reported");
            Check(store.Xml=="old" && !removed,"verification failure restores previous task");
            store=new Store { Xml=null,CorruptOnce=true };
            Refused(()=>AutoStart.ReplaceTask(store,xml,value=>value==xml,()=>removed=true),"new task verification failure reported");
            Check(store.Xml==null,"failed first registration removes new task");
            store=new Store { Xml="old" };
            Refused(()=>AutoStart.ReplaceTask(store,xml,value=>true,()=>{throw new IOException("registry failure");}),"registry cleanup failure reported");
            Check(store.Xml=="old","failed legacy cleanup rolls task back");
        }
    }
}

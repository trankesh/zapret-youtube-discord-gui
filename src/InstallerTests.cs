using System;
using System.IO;
using System.Web.Script.Serialization;

namespace ZapretDesktop
{
    internal static class InstallerTests
    {
        static void Check(bool condition,string message) { if (!condition) throw new Exception("FAILED installer: " + message); }
        public static void Run(string directory)
        {
            string source=Path.Combine(directory,"portable","data"),target=Path.Combine(directory,"installed-profile");
            Directory.CreateDirectory(Path.Combine(source,"bundles","Flowseal")); Directory.CreateDirectory(Path.Combine(source,"backups"));
            File.WriteAllText(Path.Combine(source,"bundles","Flowseal","example.txt"),"bundle fixture");
            string backup=Path.Combine(source,"backups","hosts.backup"); File.WriteAllText(backup,"exact backup bytes\r\n");
            string dns=Path.Combine(source,"backups","dns.xml"); File.WriteAllText(dns,"dns fixture");
            var serializer=new JavaScriptSerializer();
            var settings=new Settings { Root=Path.Combine(source,"bundles","Flowseal"),Strategy="general",AccentColor="#75D9AB",StartMinimized=true,HostsReceipt=new HostsReceipt { Backup=backup,OriginalHash="original",AppliedHash="applied" },DnsBackup=dns,DnsBackupHistory=new[]{dns},SetupVersion=1 };
            string original=serializer.Serialize(settings); File.WriteAllText(Path.Combine(source,"settings.json"),original);
            Check(InstallerRuntime.ImportData(source,target),"portable profile import succeeds");
            var imported=serializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(target,"settings.json")));
            Check(imported.Root==Path.Combine(target,"bundles","Flowseal") && File.ReadAllText(Path.Combine(imported.Root,"example.txt"))=="bundle fixture","bundle copied and path rebased");
            Check(imported.HostsReceipt.Backup==Path.Combine(target,"backups","hosts.backup") && File.ReadAllText(imported.HostsReceipt.Backup)=="exact backup bytes\r\n" && imported.HostsReceipt.AppliedHash=="applied","hosts restore receipt and exact backup retained");
            Check(imported.DnsBackup==Path.Combine(target,"backups","dns.xml") && imported.DnsBackupHistory[0]==imported.DnsBackup,"DNS rollback paths rebased");
            Check(imported.AccentColor==settings.AccentColor && imported.StartMinimized && imported.SetupVersion==1,"preferences retained");
            Check(File.ReadAllText(Path.Combine(source,"settings.json"))==original,"portable source unchanged");
            string existing=File.ReadAllText(Path.Combine(target,"settings.json"));
            Check(!InstallerRuntime.ImportData(source,target) && existing==File.ReadAllText(Path.Combine(target,"settings.json")),"existing installed settings never overwritten");
            Check(InstallerRuntime.Rebase("C:\\external\\ZAPRET",source,target)=="C:\\external\\ZAPRET","external bundle remains external");
            Check(ApplicationPaths.DefaultData("C:\\app\\Zapret.exe",true,"C:\\profile")=="C:\\profile\\ZapretDesktop" && ApplicationPaths.DefaultData("C:\\app\\Zapret.exe",false,"C:\\profile")=="C:\\profile\\ZapretDesktop","installed and standalone EXE share a stable local profile");
            Check(ApplicationPaths.DefaultData("C:\\app\\Zapret.exe",false,"C:\\profile",true)=="C:\\app\\data" && ApplicationPaths.DefaultData("C:\\app\\Zapret.exe",true,"C:\\profile",true)=="C:\\profile\\ZapretDesktop","explicit portable mode remains optional and is ignored by installed version");
            byte[] fallback=RuntimeDefaults.Config(Path.Combine(directory,"no-sidecar.exe"));
            var config=new System.Xml.XmlDocument(); config.LoadXml(System.Text.Encoding.UTF8.GetString(fallback));
            Check(config.SelectSingleNode("/configuration/startup/supportedRuntime").Attributes["sku"].Value==".NETFramework,Version=v4.8","single EXE has an embedded startup config");
            string custom=Path.Combine(directory,"custom.exe"); File.WriteAllText(custom+".config","custom config");
            Check(System.Text.Encoding.UTF8.GetString(RuntimeDefaults.Config(custom))=="custom config","external config is preserved when supplied");
            string sid=AutoStart.UserSid,installed=Path.Combine(AutoStart.InstalledDirectory,"Zapret.exe");
            string xml=AutoStart.TaskXml(installed,target,installed,sid);
            Check(AutoStart.ParseTask(xml,sid,AutoStart.ProtectedDirectory).Source==installed && AutoStart.IsInstalledRegistration(xml,sid),"installed task uses protected main EXE and uninstall identifies it");
            string portableXml=AutoStart.TaskXml(Path.Combine(directory,"portable","Zapret.exe"),source,Path.Combine(AutoStart.ProtectedDirectory,"fixture.exe"),sid);
            Check(!AutoStart.IsInstalledRegistration(portableXml,sid),"uninstaller preserves portable task");
            string renamed=Path.Combine(directory,"portable","My custom Zapret.exe");
            Check(AutoStart.LegacySourceForData(AutoStart.Command(renamed),source)==renamed,"renamed portable legacy entry migrated by data location");
        }
    }
}

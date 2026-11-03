using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace ZapretDesktop
{
    internal static class FeatureTests
    {
        static void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); }
        static void Throws(Action action, string name) { bool caught = false; try { action(); } catch (InvalidDataException) { caught = true; } catch (IOException) { caught = true; } Check(caught, name); }
        public static string Run(string directory)
        {
            StartupTests.Run();
            GuiUpdates.Test();
            InterfaceDns.Test();
            InstallerTests.Run(directory);
            var interlock=new SweepInterlock();
            Check(!interlock.Active && !interlock.Cancel(),"idle UI is unlocked"); interlock.Begin();
            Check(interlock.Active && interlock.Cancel() && interlock.Active && interlock.Cancelling && !interlock.Cancel(),"cancel is idempotent and UI stays locked until cleanup ends");
            interlock.End(); Check(!interlock.Active && !interlock.Cancelling,"UI unlocked after operation cleanup");
            Check(Shell.FriendlyTarget("YouTubeShort") == "YouTube Short" && Shell.FriendlyTarget("YouTube_Short") == "YouTube Short" && Shell.FriendlyTarget("YouTubeVideoRedirect") == "YouTube Video Redirect" && Shell.FriendlyTarget("DiscordCDN") == "Discord CDN", "target labels preserve brands and abbreviations without extra spaces");
            const string nativeText = "Глобальные параметры TCP\r\nСостояние масштабирования: normal\r\n";
            Check(NativeOutput.Decode(Encoding.UTF8.GetBytes(nativeText), 866) == nativeText, "UTF-8 native tool output preserves Cyrillic");
            Check(NativeOutput.Decode(Encoding.GetEncoding(866).GetBytes(nativeText), 866) == nativeText, "OEM native tool output preserves Cyrillic");
            Check(NativeOutput.Decode(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(nativeText)).ToArray(), 866) == nativeText, "Unicode native tool output preserves Cyrillic");
            string dnsPath=Path.Combine(directory,"dns-hosts");
            File.WriteAllText(dnsPath,"127.0.0.1 localhost\n10.0.0.1 dns.google private.lan # keep alias\n");
            string dnsRemote="203.0.113.1 example.org\n203.0.113.2 dns.google another.example\n";
            string dnsSource=SecureDns.HostsSource(dnsRemote,true);
            Check(dnsSource.Contains("203.0.113.2 another.example") && !dnsSource.Contains("203.0.113.2 dns.google"),"DNS endpoint overrides conflicting remote mapping, preserves unrelated alias");
            var dnsPlan=HostsUpdate.Plan(dnsPath,dnsSource);
            Check(dnsPlan.Text.Contains("10.0.0.1\tprivate.lan # keep alias") && dnsPlan.Text.Contains("2620:119:fc::2 doh.opendns.com"),"DNS hosts preserves unrelated local aliases and includes OpenDNS IPv6");
            HostsUpdate.Apply(dnsPlan,Path.Combine(directory,"dns-backups"));
            Check(HostsUpdate.Plan(dnsPath,dnsSource).Current,"DNS hosts merge is idempotent");
            var dnsOff=HostsUpdate.Plan(dnsPath,SecureDns.HostsSource(dnsRemote,false));
            Check(!dnsOff.Current && dnsOff.Removed>0,"DNS hosts opt-out detects removal from managed block");
            HostsUpdate.Apply(dnsOff,Path.Combine(directory,"dns-backups"));
            Check(!File.ReadAllText(dnsPath).Contains("dns.quad9.net") && File.ReadAllText(dnsPath).Contains("private.lan"),"DNS hosts opt-out preserves unrelated mappings");
            foreach (var provider in SecureDns.Providers) {
                Check(provider.V4.Concat(provider.V6).All(ip=>System.Net.IPAddress.Parse(ip)!=null) && new Uri(provider.Template).Scheme=="https","DNS catalog contains valid IPs and HTTPS templates");
                string script=SecureDns.Script("Apply","11111111-1111-1111-1111-111111111111",provider,"C:\\backup user's\\dns.xml");
                Check(script.Contains("backup user''s") && script.Contains("Add-Type -TypeDefinition") && !script.Contains("Assembly]::LoadFrom"),"DNS quotes paths and embeds native helper without loading writable executables");
            }
            string path = Path.Combine(directory, "hosts"); string backups = Path.Combine(directory, "backups");
            string original = "# Личные записи\r\n127.0.0.1 localhost\r\n10.0.0.1 github.com private.lan # keep comment\r\n192.168.1.20 printer.lan\r\n";
            byte[] bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(original)).ToArray(); File.WriteAllBytes(path, bytes);
            string remote = "140.82.121.3 github.com\n146.75.22.132 objects.githubusercontent.com\n";
            var plan = HostsUpdate.Plan(path, remote);
            Check(!plan.Current && plan.Added == 1 && plan.Changed == 1, "hosts added/changed counts");
            Check(plan.Text.Contains("10.0.0.1\tprivate.lan # keep comment") && plan.Text.Contains("192.168.1.20 printer.lan") && plan.Text.Contains("# Личные записи"), "hosts preserves aliases, comments, unrelated entries and Cyrillic");
            var receipt = HostsUpdate.Apply(plan, backups); Check(File.ReadAllBytes(receipt.Backup).SequenceEqual(bytes), "hosts exact backup");
            Check(File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "hosts BOM preservation");
            Check(HostsUpdate.Plan(path, remote).Current, "hosts semantic current and idempotence");
            Check(HostsUpdate.Apply(HostsUpdate.Plan(path, remote), backups) == null, "hosts no rewrite when current");
            HostsUpdate.Restore(path, receipt, backups); Check(File.ReadAllBytes(path).SequenceEqual(bytes), "hosts exact restore");
            plan = HostsUpdate.Plan(path, remote); File.AppendAllText(path, "# later edit\r\n");
            Throws(() => HostsUpdate.Apply(plan, backups), "hosts stale preview refusal");
            Check(File.ReadAllText(path).Contains("later edit"), "hosts stale edit not lost");
            plan = HostsUpdate.Plan(path, remote); receipt = HostsUpdate.Apply(plan, backups); File.AppendAllText(path, "# another application\r\n");
            Throws(() => HostsUpdate.Restore(path, receipt, backups), "hosts refuses restore over subsequent edits");
            Throws(() => HostsUpdate.Plan(path, "<html>Error</html>"), "hosts rejects HTML");
            File.WriteAllText(path, "# BEGIN Zapret Desktop (Flowseal)\n");
            Throws(() => HostsUpdate.Plan(path, remote), "hosts rejects malformed managed block");
            File.WriteAllText(path, "140.82.121.3 github.com\n146.75.22.132 objects.githubusercontent.com\n127.0.0.1 localhost\n");
            Check(HostsUpdate.Plan(path, remote).Current, "recognizes original/manual Flowseal hosts without rewriting");
            File.WriteAllText(path, "# BEGIN Zapret Desktop (Flowseal)\n1.1.1.1 github.com\n# END Zapret Desktop (Flowseal)\n127.0.0.1 localhost\n");
            plan = HostsUpdate.Plan(path, remote); HostsUpdate.Apply(plan, backups);
            Check(File.ReadAllText(path).Split(new[] { "# BEGIN Zapret Desktop" }, StringSplitOptions.None).Length == 2, "managed block replaced once");
            var settings = new JavaScriptSerializer().Deserialize<Settings>("{}"); Check(settings.CloseToTray && settings.SetupVersion == 0, "tray defaults and setup migration");
            string bundle = EmbeddedBundle.Extract(Path.Combine(directory, "bundled")); Check(Bundle.Valid(bundle) && Bundle.Version(bundle) == EmbeddedBundle.Version, "embedded official bundle extraction"); Bundle.ValidateForSetup(bundle);
            var allStrategies = Bundle.Strategies(bundle);
            Check(Bundle.RuntimeProblem(bundle)==null,"complete Flowseal bundle available");
            string ipMode=Bundle.IpMode(bundle); Bundle.SetIpMode(bundle,"none"); Bundle.ValidateForSetup(bundle); Bundle.SetIpMode(bundle,ipMode);
            string missingFixture=Path.Combine(bundle,"bin","winws.exe"); var engineFixture=File.ReadAllBytes(missingFixture); File.Delete(missingFixture);
            Check(Bundle.RuntimeProblem(bundle)!=null,"removed engine requires bundle selection"); File.WriteAllBytes(missingFixture,engineFixture);
            Check(Bundle.RuntimeProblem(Path.Combine(directory,"deleted-flowseal"))!=null && Bundle.RuntimeProblem("")!=null,"missing folder and empty selection are blocked");
            Check(Bundle.TestSubset(allStrategies, new[] { allStrategies[1].Name.ToUpperInvariant(), "missing" }, true).Single().Name == allStrategies[1].Name, "selected test scope is exact and case-insensitive");
            Check(Bundle.TestSubset(allStrategies, new string[0], true).Count == 0 && Bundle.TestSubset(allStrategies, new string[0], false).Count == allStrategies.Count, "empty selection never becomes all strategies");
            string serviceFile = Path.Combine(bundle, "service.bat"), originalService = File.ReadAllText(serviceFile);
            File.WriteAllText(serviceFile, "@echo off\r\nset \"LOCAL_VERSION=9.8.7\"\r\n"); Check(Bundle.Version(bundle) == "9.8.7", "bundle version reads installed service data");
            File.WriteAllText(serviceFile, originalService);
            Check(GestureMath.Commit(44,1000) && GestureMath.Commit(18,100) && !GestureMath.Commit(18,300) && !GestureMath.Commit(-50,100), "drag completion thresholds and reversed gesture");
            Check(GestureMath.Pull(30,80) == 0 && GestureMath.Pull(130,80) == 50, "sheet pull starts only after reaching list top");
            settings.TestSelectedOnly = true; settings.TestStrategies = new[] { allStrategies[1].Name };
            var roundtrip = new JavaScriptSerializer().Deserialize<Settings>(new JavaScriptSerializer().Serialize(settings)); Check(roundtrip.TestSelectedOnly && roundtrip.TestStrategies.Single() == allStrategies[1].Name, "test scope settings persist");
            foreach (var strategy in Bundle.Strategies(bundle)) Bundle.Arguments(strategy, bundle, new GameFilter());
            string userList = Path.Combine(bundle, "lists", "list-general-user.txt"); File.WriteAllText(userList, "custom.example\n");
            Check(EmbeddedBundle.Extract(Path.Combine(directory, "bundled")) == bundle && File.ReadAllText(userList) == "custom.example\n", "embedded bundle reuse preserves user edits");
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BundledZapret.zip")) using (var memory = new MemoryStream()) {
                stream.CopyTo(memory); Check(HostsUpdate.Hash(memory.ToArray()) == "291AF673BD0238506F9548E0E94CA0ABF244000F4C9A06960544FB01C5F97B34", "embedded bundle provenance hash");
            }
            using (var memory = new MemoryStream()) {
                using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true)) using (var writer = new StreamWriter(archive.CreateEntry("../escaped.txt").Open())) writer.Write("bad");
                memory.Position = 0; string extract = Path.Combine(directory, "bad-zip"); Directory.CreateDirectory(extract);
                using (var archive = new ZipArchive(memory, ZipArchiveMode.Read)) Throws(() => EmbeddedBundle.ExtractSafe(archive, extract), "ZIP traversal refusal");
                Check(!File.Exists(Path.Combine(directory, "escaped.txt")), "ZIP cannot escape destination");
            }
            using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location)) Check(icon != null && icon.Width >= 16, "native executable icon");
            return "PASS elevated startup XML, protected ACL, migration and rollback with fake scheduler; hosts merge/backup/restore/BOM/guards; embedded bundle/hash/strategies/ZIP guards; tray defaults and icon";
        }
    }
}

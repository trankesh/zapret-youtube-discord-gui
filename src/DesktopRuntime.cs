using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Threading;

namespace ZapretDesktop
{
    internal sealed class SingleInstance : IDisposable
    {
        readonly Mutex mutex;
        readonly Mutex presence;
        internal const string PresenceName = @"Global\ZapretDesktop.AppPresence.4C430F24-4E93-4E20-90AA-F530D0C88F69";
        readonly EventWaitHandle signal;
        RegisteredWaitHandle listener;
        public readonly bool First;
        public SingleInstance(bool enabled)
        {
            if (!enabled) { First = true; return; }
            string id = "Local\\ZapretDesktop-" + WindowsIdentity.GetCurrent().User.Value;
            mutex = new Mutex(true, id, out First);
            signal = new EventWaitHandle(false, EventResetMode.AutoReset, id + "-show");
            if (First) { var security=new MutexSecurity(); security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid,null),MutexRights.FullControl,AccessControlType.Allow)); bool created; presence=new Mutex(false,PresenceName,out created,security); }
        }
        public void ShowExisting() { if (signal != null) signal.Set(); }
        internal static bool IsCurrentUserRunning()
        {
            Mutex existing;
            if (!Mutex.TryOpenExisting("Local\\ZapretDesktop-"+WindowsIdentity.GetCurrent().User.Value,out existing)) return false;
            existing.Dispose(); return true;
        }
        public void Listen(Action show)
        {
            if (signal != null) listener = ThreadPool.RegisterWaitForSingleObject(signal, (s, t) => show(), null, Timeout.Infinite, false);
        }
        public void Dispose()
        {
            if (listener != null) listener.Unregister(null);
            if (signal != null) signal.Dispose();
            if (presence != null) presence.Dispose();
            if (mutex != null) { if (First) mutex.ReleaseMutex(); mutex.Dispose(); }
        }
    }
    internal static class EmbeddedBundle
    {
        public const string Version = "1.10.4";
        public static string Extract(string dataDirectory)
        {
            byte[] bytes;
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("BundledZapret.zip"))
            using (var memory = new MemoryStream()) { if (source == null) throw new FileNotFoundException("Встроенный набор не найден."); source.CopyTo(memory); bytes = memory.ToArray(); }
            string parent = Path.Combine(dataDirectory, "bundles"); Directory.CreateDirectory(parent);
            string destination = Path.Combine(parent, "Flowseal-" + Version + "-" + HostsUpdate.Hash(bytes).Substring(0, 12));
            string hash = HostsUpdate.Hash(bytes), root = null;
            foreach (string candidate in Directory.GetDirectories(parent, Path.GetFileName(destination) + "*")) {
                string marker = Path.Combine(candidate, ".complete");
                if (File.Exists(marker) && File.ReadAllText(marker) == hash && (root = FindRoot(candidate)) != null) { Bundle.EnsureUserLists(root); return root; }
            }
            // Extract into a new directory and publish a completion marker only
            // after validation. No directory rename is required while Windows
            // or a sync client may still have handles to freshly extracted files.
            if (Directory.Exists(destination)) destination += "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Directory.CreateDirectory(destination);
            using (var memory = new MemoryStream(bytes)) using (var zip = new ZipArchive(memory, ZipArchiveMode.Read)) ExtractSafe(zip, destination);
            root = FindRoot(destination); if (root == null) throw new InvalidDataException("Встроенный набор не прошёл проверку файлов.");
            Bundle.EnsureUserLists(root);
            var strategies = Bundle.Strategies(root); if (strategies.Count == 0) throw new InvalidDataException("Нет стратегий во встроенном наборе.");
            foreach (var strategy in strategies) Bundle.Arguments(strategy, root, GameFilter.Read(root));
            foreach (string file in new[] { "WinDivert.dll", "WinDivert64.sys", "cygwin1.dll" }) if (!File.Exists(Path.Combine(root, "bin", file))) throw new FileNotFoundException("В наборе отсутствует " + file);
            File.WriteAllText(Path.Combine(destination, ".complete"), hash);
            return root;
        }
        public static void ExtractSafe(ZipArchive zip, string destination)
        {
            string prefix = Path.GetFullPath(destination).TrimEnd('\\', '/') + Path.DirectorySeparatorChar; long total = 0;
            foreach (var entry in zip.Entries)
            {
                string target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Архив содержит небезопасный путь.");
                total += entry.Length; if (total > 200 * 1024 * 1024) throw new InvalidDataException("Архив слишком большой.");
                if (String.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var input = entry.Open()) using (var output = new FileStream(target, FileMode.CreateNew)) input.CopyTo(output);
            }
        }
        static string FindRoot(string directory)
        {
            if (!Directory.Exists(directory)) return null;
            if (Bundle.Valid(directory)) return directory;
            return Directory.GetDirectories(directory).FirstOrDefault(Bundle.Valid);
        }
    }
}

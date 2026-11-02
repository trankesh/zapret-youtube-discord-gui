using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretDesktop
{
    public sealed class HostsPlan
    {
        public string Path, OriginalHash, Text, RemoteHash;
        public byte[] Original, Updated;
        public bool Current, Existed;
        public int Domains, Added, Changed, Removed;
    }
    public sealed class HostsReceipt
    {
        public string Backup { get; set; }
        public string AppliedHash { get; set; }
        public string OriginalHash { get; set; }
        public string Date { get; set; }
    }
    public static class HostsUpdate
    {
        public const string Url = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts";
        const string Start = "# BEGIN Zapret Desktop (Flowseal)";
        const string End = "# END Zapret Desktop (Flowseal)";
        public static string SystemPath { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts"); } }
        public static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        public static async Task<string> Download(string url, CancellationToken token)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) })
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(25000); token = timeout.Token;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretDesktop/" + typeof(HostsUpdate).Assembly.GetName().Version.ToString(3));
                client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new IOException("Файл обновления неожиданно большой.");
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var buffer = new MemoryStream())
                    {
                        byte[] bytes = new byte[8192]; int n;
                        while ((n = await stream.ReadAsync(bytes, 0, bytes.Length, token)) > 0) { buffer.Write(bytes, 0, n); if (buffer.Length > 2 * 1024 * 1024) throw new IOException("Превышен размер файла обновления."); }
                        return new UTF8Encoding(false, true).GetString(buffer.ToArray()).TrimStart('\uFEFF');
                    }
                }
            }
        }
        sealed class Record { public string IP; public string[] Names; public string Comment; }
        static Record Parse(string line)
        {
            int comment = line.IndexOf('#'); string body = comment < 0 ? line : line.Substring(0, comment);
            var tokens = Regex.Split(body.Trim(), @"\s+"); IPAddress ip;
            if (tokens.Length < 2 || !IPAddress.TryParse(tokens[0], out ip)) return null;
            if (tokens.Skip(1).Any(n => !Regex.IsMatch(n, @"^[A-Za-z0-9_][A-Za-z0-9_.-]*$"))) return null;
            return new Record { IP = ip.ToString(), Names = tokens.Skip(1).ToArray(), Comment = comment < 0 ? "" : line.Substring(comment) };
        }
        static Dictionary<string, HashSet<string>> Mappings(string text, bool strict)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in Regex.Split(text, @"\r?\n"))
            {
                if (String.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                var r = Parse(line);
                if (r == null) { if (strict) throw new InvalidDataException("Источник hosts содержит некорректную строку. Обновление отменено."); continue; }
                foreach (string name in r.Names) { if (!result.ContainsKey(name)) result[name] = new HashSet<string>(); result[name].Add(r.IP); }
            }
            return result;
        }
        static Encoding Detect(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191) return new UTF8Encoding(true);
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255) return Encoding.BigEndianUnicode;
            try { new UTF8Encoding(false, true).GetString(bytes); return new UTF8Encoding(false); } catch (DecoderFallbackException) { return Encoding.Default; }
        }
        static byte[] Encode(string text, Encoding encoding) { return encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray(); }
        public static HostsPlan Plan(string path, string remote)
        {
            if (String.IsNullOrWhiteSpace(remote) || remote.Length > 2 * 1024 * 1024 || remote.Contains(Start) || remote.Contains(End)) throw new InvalidDataException("Некорректный источник hosts.");
            var desired = Mappings(remote, true); if (desired.Count == 0) throw new InvalidDataException("В источнике hosts нет записей.");
            bool existed = File.Exists(path); byte[] bytes = existed ? File.ReadAllBytes(path) : new byte[0]; var encoding = Detect(bytes);
            string old = encoding.GetString(bytes).TrimStart('\uFEFF'); var actual = Mappings(old, false);
            int starts = Regex.Matches(old, "(?m)^" + Regex.Escape(Start) + "\\r?$").Count, ends = Regex.Matches(old, "(?m)^" + Regex.Escape(End) + "\\r?$").Count;
            if (starts != ends || starts > 1) throw new InvalidDataException("В hosts повреждены границы блока Zapret. Исправьте их вручную перед обновлением.");
            string blockPattern = "(?ms)^" + Regex.Escape(Start) + "\\r?\\n.*?^" + Regex.Escape(End) + "(?:\\r?\\n|$)";
            string managed = Regex.Match(old, blockPattern).Value;
            int removed = Mappings(managed, false).Keys.Count(n => !desired.ContainsKey(n));
            string without = Regex.Replace(old, blockPattern, "");
            if (without.Contains(Start) || without.Contains(End)) throw new InvalidDataException("Некорректные границы блока Zapret.");
            string newline = old.Contains("\r\n") || !old.Contains("\n") ? "\r\n" : "\n";
            var kept = new List<string>();
            foreach (string line in Regex.Split(without, @"\r?\n"))
            {
                var record = Parse(line);
                if (record == null || !record.Names.Any(desired.ContainsKey)) { kept.Add(line); continue; }
                string[] others = record.Names.Where(n => !desired.ContainsKey(n)).ToArray();
                if (others.Length > 0) kept.Add(record.IP + "\t" + String.Join(" ", others) + (record.Comment == "" ? "" : " " + record.Comment));
                else if (record.Comment != "") kept.Add(record.Comment);
            }
            int added = desired.Count(p => !actual.ContainsKey(p.Key)), changed = desired.Count(p => actual.ContainsKey(p.Key) && !actual[p.Key].SetEquals(p.Value));
            string prefix = String.Join(newline, kept).TrimEnd('\r', '\n');
            string result = (prefix.Length > 0 ? prefix + newline + newline : "") + Start + newline + String.Join(newline, Regex.Split(remote.Trim(), @"\r?\n")) + newline + End + newline;
            return new HostsPlan { Path = path, Original = bytes, Existed = existed, OriginalHash = Hash(bytes), Updated = Encode(result, encoding), Text = result,
                RemoteHash = Hash(Encoding.UTF8.GetBytes(remote)), Domains = desired.Count, Added = added, Changed = changed, Removed=removed, Current = added == 0 && changed == 0 && removed == 0 };
        }
        public static HostsReceipt Apply(HostsPlan plan, string backupDirectory)
        {
            if (File.Exists(plan.Path) != plan.Existed || Hash(File.Exists(plan.Path) ? File.ReadAllBytes(plan.Path) : new byte[0]) != plan.OriginalHash) throw new IOException("Hosts изменился после проверки. Проверьте обновление ещё раз.");
            if (plan.Current) return null;
            Directory.CreateDirectory(backupDirectory);
            string backup = System.IO.Path.Combine(backupDirectory, "hosts-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bak");
            File.WriteAllBytes(backup, plan.Original);
            AtomicWrite(plan.Path, plan.Updated);
            return new HostsReceipt { Backup = backup, OriginalHash = plan.OriginalHash, AppliedHash = Hash(plan.Updated), Date = DateTimeOffset.Now.ToString("o") };
        }
        public static void Restore(string path, HostsReceipt receipt, string backupDirectory)
        {
            if (receipt == null || String.IsNullOrEmpty(receipt.Backup)) throw new IOException("Нет резервной копии hosts.");
            string root = System.IO.Path.GetFullPath(backupDirectory).TrimEnd('\\') + "\\";
            if (!System.IO.Path.GetFullPath(receipt.Backup).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Резервная копия находится вне папки приложения.");
            if (!File.Exists(path) || Hash(File.ReadAllBytes(path)) != receipt.AppliedHash) throw new IOException("После обновления hosts менялся. Автоматический откат не выполнен, чтобы сохранить новые правки. Копия: " + receipt.Backup);
            byte[] bytes = File.ReadAllBytes(receipt.Backup);
            if (Hash(bytes) != receipt.OriginalHash) throw new IOException("Резервная копия повреждена.");
            AtomicWrite(path, bytes);
        }
        static void AtomicWrite(string path, byte[] bytes)
        {
            string temporary = path + ".zapret-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, bytes); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}

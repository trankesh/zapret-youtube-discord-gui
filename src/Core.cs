using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ZapretDesktop
{
    public sealed class Settings
    {
        public Settings() { CloseToTray = true; IncludeDnsHosts = true; }
        public string Root { get; set; }
        public string Strategy { get; set; }
        public bool StartMinimized { get; set; }
        public bool AdvancedMode { get; set; }
        public string AccentColor { get; set; }
        public bool CloseToTray { get; set; }
        public int SetupVersion { get; set; }
        public string HostsCheckedAt { get; set; }
        public string IpsetCheckedAt { get; set; }
        public HostsReceipt HostsReceipt { get; set; }
        public bool TestSelectedOnly { get; set; }
        public string[] TestStrategies { get; set; }
        public bool IncludeDnsHosts { get; set; }
        public string DnsProviderId { get; set; }
        public string DnsBackup { get; set; }
        public string[] DnsBackupHistory { get; set; }
    }
    public sealed class Strategy
    {
        public string Name { get; set; }
        public string File { get; set; }
        public string Methods { get; set; }
        public int Profiles { get; set; }
        public override string ToString() { return Name; }
    }
    public sealed class Target
    {
        public string Name { get; set; }
        public string Address { get; set; }
    }
    public sealed class Probe
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public bool Success { get; set; }
        public long Milliseconds { get; set; }
        public long Bytes { get; set; }
        public bool IsPing { get; set; }
    }
    public sealed class TestResult
    {
        public string Strategy { get; set; }
        public int Passed { get; set; }
        public int Total { get; set; }
        public long Average { get; set; }
        public string Summary { get; set; }
        public List<Probe> Probes { get; set; }
        public List<StandardRow> Rows { get; set; }
        public int Errors { get; set; }
        public int Unsupported { get; set; }
        public int PingOK { get; set; }
        public int PingFail { get; set; }
    }
    public sealed class GameFilter
    {
        public string Mode = "disabled";
        public string Tcp = "1024-65535";
        public string Udp = "1024-65535";
        public string ActiveTcp { get { return Mode == "all" || Mode == "tcp" ? Tcp : "12"; } }
        public string ActiveUdp { get { return Mode == "all" || Mode == "udp" ? Udp : "12"; } }
        public static bool ValidPorts(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            foreach (string item in value.Split(','))
            {
                if (!Regex.IsMatch(item, @"^[1-9]\d{0,4}(-[1-9]\d{0,4})?$")) return false;
                var parts = item.Split('-');
                int a = int.Parse(parts[0]), b = parts.Length == 2 ? int.Parse(parts[1]) : a;
                if (a > b || b > 65535) return false;
            }
            return true;
        }
        public static GameFilter Read(string root)
        {
            var f = new GameFilter();
            string file = Path.Combine(root, "utils", "game_filter.enabled");
            if (!System.IO.File.Exists(file)) return f;
            foreach (string line in System.IO.File.ReadAllLines(file))
            {
                var p = line.Trim().Split(new[] { '=' }, 2);
                if (p[0] == "all" || p[0] == "tcp" && p.Length == 1 || p[0] == "udp" && p.Length == 1) f.Mode = p[0];
                if (p.Length != 2) continue;
                if (p[0] == "mode" && new[] { "all", "tcp", "udp", "disabled" }.Contains(p[1])) f.Mode = p[1];
                if (p[0] == "tcp" && ValidPorts(p[1])) f.Tcp = p[1];
                if (p[0] == "udp" && ValidPorts(p[1])) f.Udp = p[1];
            }
            return f;
        }
        public void Save(string root)
        {
            if (!new[] { "all", "tcp", "udp", "disabled" }.Contains(Mode) || !ValidPorts(Tcp) || !ValidPorts(Udp)) throw new InvalidDataException("Некорректные параметры Game Filter.");
            Bundle.WriteBackup(Path.Combine(root, "utils", "game_filter.enabled"), "mode=" + Mode + "\r\ntcp=" + Tcp + "\r\nudp=" + Udp + "\r\n");
        }
    }
    public static class Bundle
    {
        public static void ValidateForSetup(string root)
        {
            if (!Valid(root)) throw new InvalidDataException("Нужна распакованная папка Flowseal с service.bat и bin/winws.exe.");
            foreach (string relative in new[] { "bin/WinDivert.dll", "bin/WinDivert64.sys", "bin/cygwin1.dll", "lists/list-general.txt", "lists/list-google.txt", "lists/list-exclude.txt", "lists/ipset-exclude.txt", "utils/targets.txt" })
                if (!File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)))) throw new FileNotFoundException("Набор неполный: нет " + relative + ". Выберите свежий распакованный архив Flowseal или встроенный набор.");
            if (!File.Exists(Path.Combine(root,"lists","ipset-all.txt")) && !File.Exists(Path.Combine(root,"lists","ipset-all.txt.backup"))) throw new FileNotFoundException("В наборе нет списка IPSet. Выбери полный распакованный набор Flowseal.");
            if (Strategies(root).Count == 0) throw new InvalidDataException("В наборе нет стратегий.");
        }
        public static void EnsureUserLists(string root)
        {
            if (!Valid(root)) throw new DirectoryNotFoundException("Набор Zapret не найден.");
            string lists = Path.Combine(root, "lists"); Directory.CreateDirectory(lists);
            foreach (var item in new[] { new[] { "list-general-user.txt", "# User list\r\ndomain.example.abc\r\n" }, new[] { "list-exclude-user.txt", "domain.example.abc\r\n" }, new[] { "ipset-exclude-user.txt", "203.0.113.113/32\r\n" } })
                if (!File.Exists(Path.Combine(lists, item[0]))) File.WriteAllText(Path.Combine(lists, item[0]), item[1], new UTF8Encoding(false));
        }
        public static bool Valid(string root) { return !String.IsNullOrEmpty(root) && Directory.Exists(root) && File.Exists(Path.Combine(root, "service.bat")) && File.Exists(Path.Combine(root, "bin", "winws.exe")); }
        public static string RuntimeProblem(string root)
        {
            if (String.IsNullOrWhiteSpace(root)) return "Сначала выбери папку набора Flowseal или используй встроенный набор.";
            if (!Directory.Exists(root)) return "Папка Flowseal не найдена. Возможно, её переместили, удалили или отключили диск. Выбери её новое расположение либо используй встроенный набор.";
            foreach (string relative in new[]{"service.bat","bin/winws.exe","bin/WinDivert.dll","bin/WinDivert64.sys","bin/cygwin1.dll","lists/list-general.txt","lists/list-google.txt","lists/list-exclude.txt","utils/targets.txt"})
                if (!File.Exists(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)))) return "В папке Flowseal не хватает файла " + relative + ". Выбери полную распакованную папку или используй встроенный набор.";
            try { if (!Directory.GetFiles(root,"*.bat").Any(file=>!Path.GetFileName(file).StartsWith("service",StringComparison.OrdinalIgnoreCase))) return "В наборе Flowseal не осталось стратегий. Выбери полный набор или используй встроенный."; }
            catch (IOException) { return "Не удалось прочитать папку Flowseal. Проверь доступ к диску и выбери папку ещё раз."; }
            catch (UnauthorizedAccessException) { return "Windows не разрешила прочитать папку Flowseal. Выбери другую распакованную копию набора."; }
            return null;
        }
        public static string Detect()
        {
            string[] roots = { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZAPRET"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ZAPRET") };
            return roots.FirstOrDefault(Valid) ?? "";
        }
        public static string Version(string root)
        {
            if (!Valid(root)) return "не выбран";
            string text = File.ReadAllText(Path.Combine(root, "service.bat"));
            var m = Regex.Match(text, @"(?im)\bLOCAL_VERSION\s*=\s*(v?\d+(?:\.\d+){1,3}(?:[-\w.]*)?)");
            if (m.Success) return m.Groups[1].Value;
            string versionFile = Path.Combine(root, ".service", "version.txt");
            if (File.Exists(versionFile)) { string version = File.ReadAllText(versionFile).Trim(); if (Regex.IsMatch(version, @"^v?\d+(?:\.\d+){1,3}[-\w.]*$")) return version; }
            return "не определена";
        }
        public static List<Strategy> Strategies(string root)
        {
            return Directory.GetFiles(root, "*.bat").Where(p => !Path.GetFileName(p).StartsWith("service", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => Path.GetFileName(p).Equals("general.bat", StringComparison.OrdinalIgnoreCase) ? "" : Regex.Replace(Path.GetFileName(p), @"\d+", m => m.Value.PadLeft(8, '0')))
                .Select(p => {
                    string text = File.ReadAllText(p);
                    return new Strategy { File = p, Name = Path.GetFileNameWithoutExtension(p), Profiles = Regex.Matches(text, @"--filter-(tcp|udp)=").Count,
                        Methods = String.Join(" / ", Regex.Matches(text, @"--dpi-desync=([^\s]+)").Cast<Match>().Select(m => m.Groups[1].Value).Distinct()) };
                }).ToList();
        }
        public static List<Strategy> TestSubset(System.Collections.Generic.IEnumerable<Strategy> strategies, System.Collections.Generic.IEnumerable<string> selected, bool selectedOnly)
        {
            if (!selectedOnly) return strategies.ToList();
            var names = new System.Collections.Generic.HashSet<string>(selected ?? new string[0], StringComparer.OrdinalIgnoreCase);
            return strategies.Where(s => names.Contains(s.Name)).ToList();
        }
        public static string Arguments(Strategy strategy, string root, GameFilter filter)
        {
            // Parse the data portion only. No batch commands are executed.
            string text = File.ReadAllText(strategy.File, Encoding.UTF8);
            var start = Regex.Match(text, "(?im)^start[^\\r\\n]*?winws\\.exe\"[ \\t]+(?<args>[^\\r\\n]*)");
            if (!start.Success) throw new InvalidDataException("Не найден запуск winws.exe в " + strategy.Name);
            string tail = text.Substring(start.Groups["args"].Index);
            var lines = Regex.Split(tail, @"\r?\n");
            var b = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                bool more = line.EndsWith("^");
                b.Append(more ? line.Substring(0, line.Length - 1) : line).Append(' ');
                if (!more) break;
            }
            string args = b.ToString().Replace("%~dp0", Path.GetFullPath(root).TrimEnd('\\') + "\\")
                .Replace("%BIN%", Path.Combine(root, "bin") + "\\").Replace("%LISTS%", Path.Combine(root, "lists") + "\\")
                .Replace("%GameFilterTCP%", filter.ActiveTcp).Replace("%GameFilterUDP%", filter.ActiveUdp)
                .Replace("%GameFilter%", filter.Mode == "udp" ? filter.ActiveUdp : filter.ActiveTcp).Replace("^!", "!").Trim();
            if (Regex.IsMatch(args, @"%[^%\s]+%")) throw new InvalidDataException("Неизвестная переменная в стратегии: " + strategy.Name);
            if (args.Contains("\r") || args.Contains("\n") || args.Contains("^")) throw new InvalidDataException("Неподдерживаемый синтаксис стратегии.");
            foreach (Match m in Regex.Matches(args, "=\"([^\"]+)\""))
            {
                string path = m.Groups[1].Value;
                if (Path.IsPathRooted(path) && !File.Exists(path)) throw new FileNotFoundException("Файл стратегии отсутствует", path);
            }
            return args;
        }
        public static List<Target> Targets(string root)
        {
            string path = Path.Combine(root, "utils", "targets.txt");
            if (!File.Exists(path)) throw new FileNotFoundException("Не найден utils/targets.txt");
            var targets = new List<Target>();
            foreach (string line in File.ReadAllLines(path))
            {
                var m = Regex.Match(line, "^\\s*(\\w+)\\s*=\\s*\"([^\"]+)\"");
                if (!m.Success) continue;
                string address = m.Groups[2].Value;
                Uri uri;
                if (!address.StartsWith("PING:") && (!Uri.TryCreate(address, UriKind.Absolute, out uri) || uri.Scheme != "https" && uri.Scheme != "http"))
                    throw new InvalidDataException("Некорректная цель: " + address);
                targets.Add(new Target { Name = m.Groups[1].Value, Address = address });
            }
            if (targets.Count == 0) throw new InvalidDataException("Список целей пуст.");
            return targets;
        }
        public static void WriteBackup(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path)) File.Copy(path, path + ".ui-backup", true);
            string temp = path + ".ui-tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static string IpMode(string root)
        {
            string path = Path.Combine(root, "lists", "ipset-all.txt");
            if (!File.Exists(path)) return "none";
            string value = File.ReadAllText(path);
            return String.IsNullOrWhiteSpace(value) ? "any" : value.Contains("203.0.113.113/32") ? "none" : "loaded";
        }
        public static void SetIpMode(string root, string mode)
        {
            string path = Path.Combine(root, "lists", "ipset-all.txt"), backup = path + ".backup";
            if (mode == IpMode(root)) return;
            if (IpMode(root) == "loaded") File.Copy(path, backup, true);
            if (mode == "loaded")
            {
                if (!File.Exists(backup) || String.IsNullOrWhiteSpace(File.ReadAllText(backup)) || File.ReadAllText(backup).Contains("203.0.113.113/32")) throw new IOException("Нет сохранённого IPSet. Сначала обновите список.");
                WriteBackup(path, File.ReadAllText(backup));
            }
            else WriteBackup(path, mode == "any" ? "" : "203.0.113.113/32\r\n");
        }
    }
    public sealed class Engine : IDisposable
    {
        public Process Owned;
        public Strategy Active;
        public Action<string> Log = delegate { };
        public bool Running { get { try { return Owned != null && !Owned.HasExited; } catch { return false; } } }
        public static bool IsAdmin { get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } }
        public static string ServiceState()
        {
            try { using (var svc = new ServiceController("zapret")) return svc.Status.ToString(); }
            catch (InvalidOperationException) { return "NotInstalled"; }
        }
        public static bool ServiceMatches(string root)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret"))
            {
                string image = key == null ? "" : Convert.ToString(key.GetValue("ImagePath"));
                string exe = Path.Combine(root, "bin", "winws.exe");
                return image.StartsWith("\"" + exe + "\"", StringComparison.OrdinalIgnoreCase) || image.StartsWith(exe + " ", StringComparison.OrdinalIgnoreCase);
            }
        }
        public static bool AnyWinws() { var ps = Process.GetProcessesByName("winws"); bool any = ps.Length > 0; foreach (var p in ps) p.Dispose(); return any; }
        public static void RequireAdmin() { if (!IsAdmin) throw new InvalidOperationException("Запустите Zapret с правами администратора."); }
        public async Task Start(Strategy strategy, string root, CancellationToken token)
        {
            RequireAdmin();
            if (ServiceState() != "NotInstalled") throw new InvalidOperationException("Для отдельного запуска и перебора удалите службу zapret на вкладке «Настройки».");
            if (Running) throw new InvalidOperationException("Сначала остановите текущую стратегию.");
            if (AnyWinws()) throw new InvalidOperationException("Уже запущен сторонний winws.exe. Остановите его в исходном приложении.");
            Bundle.EnsureUserLists(root);
            string args = Bundle.Arguments(strategy, root, GameFilter.Read(root));
            // Flowseal enables timestamps before starting strategies that use fooling=ts.
            await RunTool("netsh.exe", new[] { "interface", "tcp", "set", "global", "timestamps=enabled" }, token);
            token.ThrowIfCancellationRequested();
            var info = new ProcessStartInfo(Path.Combine(root, "bin", "winws.exe"), args) {
                WorkingDirectory = Path.Combine(root, "bin"), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            var proc = new Process { StartInfo = info, EnableRaisingEvents = true };
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
            Owned = proc; Active = strategy;
            try
            {
                proc.Start(); proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
                await Task.Delay(900, token);
                if (proc.HasExited) throw new InvalidOperationException("winws завершился с кодом " + proc.ExitCode + ". Подробности в журнале.");
                Log("Запущена стратегия: " + strategy.Name);
            }
            catch { Stop(); throw; }
        }
        public void Stop()
        {
            if (Owned == null) return;
            try { if (!Owned.HasExited) { Owned.Kill(); Owned.WaitForExit(4000); } }
            finally { Owned.Dispose(); Owned = null; Active = null; }
            Log("Процесс интерфейса остановлен.");
        }
        public static string Quote(string value)
        {
            return "\"" + Regex.Replace(value, "(\\\\*)\"", "$1$1\\\"").TrimEnd('\\') + new string('\\', value.Reverse().TakeWhile(c => c == '\\').Count() * 2) + "\"";
        }
        public static Task<string> RunTool(string exe, IEnumerable<string> args, CancellationToken token) { return RunTool(exe,args,token,null); }
        public static async Task<string> RunTool(string exe, IEnumerable<string> args, CancellationToken token,string input)
        {
            using (var p = new Process { StartInfo = new ProcessStartInfo(exe, String.Join(" ", args.Select(Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,RedirectStandardInput=input!=null } })
            using (var stdout = new MemoryStream())
            using (var stderr = new MemoryStream())
            {
                p.Start();
                var output = p.StandardOutput.BaseStream.CopyToAsync(stdout); var error = p.StandardError.BaseStream.CopyToAsync(stderr);
                using (token.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } }))
                {
                    if(input!=null) { byte[] bytes=Encoding.UTF8.GetBytes(input); try { await p.StandardInput.BaseStream.WriteAsync(bytes,0,bytes.Length,token); } finally { p.StandardInput.Close(); } }
                    await Task.Run(() => p.WaitForExit());
                    await Task.WhenAll(output, error);
                    string result = NativeOutput.Decode(stdout.ToArray()) + NativeOutput.Decode(stderr.ToArray());
                    token.ThrowIfCancellationRequested();
                    if (p.ExitCode != 0) throw new IOException(exe + " (" + p.ExitCode + "): " + result.Trim());
                    return input==null ? result.Trim() : NativeOutput.Decode(stdout.ToArray()).Trim();
                }
            }
        }
        public async Task InstallService(string root, Strategy strategy, CancellationToken token)
        {
            RequireAdmin();
            if (ServiceState() != "NotInstalled") throw new InvalidOperationException("Служба уже установлена. Удалите её перед сменой стратегии.");
            if (AnyWinws()) throw new InvalidOperationException("Сначала остановите winws.");
            Bundle.EnsureUserLists(root);
            string args = Bundle.Arguments(strategy, root, GameFilter.Read(root));
            await RunTool("netsh.exe", new[] { "interface", "tcp", "set", "global", "timestamps=enabled" }, token);
            await RunTool("sc.exe", new[] { "create", "zapret", "binPath=", Quote(Path.Combine(root, "bin", "winws.exe")) + " " + args, "start=", "auto", "DisplayName=", "zapret" }, token);
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret", true)) key.SetValue("zapret-discord-youtube", strategy.Name);
            await ServiceAction(root, "start", token);
            Log("Служба установлена, автозапуск включён: " + strategy.Name);
        }
        public static async Task ServiceAction(string root, string action, CancellationToken token)
        {
            RequireAdmin();
            if (!ServiceMatches(root)) throw new InvalidOperationException("Служба zapret относится к другой папке. Выберите соответствующую сборку.");
            using (var svc = new ServiceController("zapret"))
            {
                var state = svc.Status;
                if (action == "start")
                {
                    if (state == ServiceControllerStatus.Running) return;
                    svc.Start(); await Task.Run(() => svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15)));
                }
                else
                {
                    if (state != ServiceControllerStatus.Stopped) { svc.Stop(); await Task.Run(() => svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15))); }
                    if (action == "delete") await RunTool("sc.exe", new[] { "delete", "zapret" }, token);
                }
            }
        }
        public void Dispose() { Stop(); }
    }
    public static class NetworkTest
    {
        static async Task<Probe> Check(Target target, CancellationToken token)
        {
            var result = new Probe { Name = target.Name, IsPing = target.Address.StartsWith("PING:") }; var watch = Stopwatch.StartNew();
            try
            {
                token.ThrowIfCancellationRequested();
                if (target.Address.StartsWith("PING:"))
                {
                    using (var ping = new Ping())
                    {
                        var reply = await ping.SendPingAsync(target.Address.Substring(5), 3500);
                        token.ThrowIfCancellationRequested();
                        result.Success = reply.Status == IPStatus.Success;
                        result.Status = "ICMP " + reply.Status;
                    }
                }
                else
                {
                    // A fresh connection for every strategy, system TLS/certificate validation.
                    using (var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = true })
                    using (var client = new HttpClient(handler))
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        timeout.CancelAfter(6000);
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretDesktop/0.1");
                        using (var response = await client.GetAsync(target.Address, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                        {
                            int code = (int)response.StatusCode;
                            result.Status = "HTTP " + code;
                            result.Success = code >= 200 && code < 400;
                            using (var stream = await response.Content.ReadAsStreamAsync())
                            using (timeout.Token.Register(() => stream.Dispose()))
                            {
                                byte[] buffer = new byte[8192]; int read;
                                while (result.Bytes < 65536 && (read = await stream.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, 65536 - result.Bytes), timeout.Token)) > 0) result.Bytes += read;
                            }
                            result.Status += " · " + result.Bytes + " B";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                result.Success = false;
                result.Status = ex is OperationCanceledException || ex is ObjectDisposedException ? "Тайм-аут 6 с" : ex.GetBaseException().Message;
            }
            result.Milliseconds = watch.ElapsedMilliseconds;
            return result;
        }
        public static async Task<TestResult> Run(string name, List<Target> targets, CancellationToken token)
        {
            using (var limit = new SemaphoreSlim(4))
            {
                var tasks = targets.Select(async t => {
                    await limit.WaitAsync(token);
                    try { return await Check(t, token); } finally { limit.Release(); }
                }).ToArray();
                var probes = (await Task.WhenAll(tasks)).ToList();
                var http = probes.Where(p => !p.IsPing).ToList();
                var good = http.Where(p => p.Success).ToList();
                return new TestResult { Strategy = name, Probes = probes, Total = http.Count, Passed = good.Count,
                    Average = good.Count > 0 ? (long)good.Average(p => p.Milliseconds) : 0,
                    Summary = good.Count + "/" + http.Count + " HTTPS · ICMP " + probes.Count(p => p.IsPing && p.Success) };
            }
        }
    }
}

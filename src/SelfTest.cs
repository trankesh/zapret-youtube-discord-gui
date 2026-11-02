using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretDesktop
{
    public static class SelfTest
    {
        [DllImport("shell32.dll", SetLastError = true)] static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string cmd, out int argc);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
        static void Assert(bool value, string message) { if (!value) throw new Exception("FAILED: " + message); }
        static string[] Split(string text)
        {
            int count; IntPtr p = CommandLineToArgvW("program.exe " + text, out count);
            if (p == IntPtr.Zero) throw new Exception("CommandLineToArgvW failed");
            try { return Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(p, i * IntPtr.Size))).ToArray(); }
            finally { LocalFree(p); }
        }
        public static int Run(string root)
        {
            var report = new StringBuilder();
            string temp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                report.AppendLine(FeatureTests.Run(temp));
                if (!Bundle.Valid(root)) root = EmbeddedBundle.Extract(Path.Combine(temp, "bundled"));
                Assert(Bundle.Valid(root), "bundle detection or embedded fallback");
                foreach (string value in new[] { "", "abc", "space here", "C:\\каталог с пробелом\\", "a\"b", "a\\\"b", "\\", "trailing\\\\", "\"quoted\"\\", "!%&" })
                    Assert(Split(Engine.Quote(value)).Single() == value, "argument quoting: " + value);
                report.AppendLine("PASS Windows argument quoting including nested quotes, Unicode and trailing slashes");
                Assert(GameFilter.ValidPorts("80,443,1024-65535"), "valid ports");
                foreach (var bad in new[] { "0", "65536", "500-20", "443,", "1&echo x", "01", "-1", "" }) Assert(!GameFilter.ValidPorts(bad), "invalid ports " + bad);
                report.AppendLine("PASS port and range validation");
                var strategies = Bundle.Strategies(root); Assert(strategies.Count > 0, "strategies found");
                foreach (var strategy in strategies)
                {
                    foreach (string mode in new[] { "disabled", "tcp", "udp", "all" })
                    {
                        var game = new GameFilter { Mode = mode, Tcp = "1000-2000", Udp = "3000-4000" };
                        string args = Bundle.Arguments(strategy, root, game); var argv = Split(args);
                        Assert(argv.All(a => a.StartsWith("--")), "no broken argument boundaries: " + strategy.Name);
                        Assert(argv.Count(a => a.StartsWith("--filter-tcp=") || a.StartsWith("--filter-udp=")) == strategy.Profiles, "all profiles retained");
                        Assert(argv.Any(a => a.StartsWith("--wf-tcp=") && a.EndsWith("," + game.ActiveTcp)), "TCP substitution");
                        Assert(argv.Any(a => a.StartsWith("--wf-udp=") && a.EndsWith("," + game.ActiveUdp)), "UDP substitution");
                        Assert(!args.Contains("%") && !args.Contains("^"), "batch expansions removed");
                        if (File.ReadAllText(strategy.File).Contains("^!")) Assert(argv.Contains("--dpi-desync-fake-tls=!"), "literal exclamation preserved");
                    }
                    report.AppendLine("PASS " + strategy.Name + " / four game modes / " + strategy.Profiles + " profiles");
                }
                var targets = Bundle.Targets(root); Assert(targets.Any(t => t.Address.StartsWith("PING:")) && targets.Any(t => t.Address.StartsWith("https:")), "target types");
                report.AppendLine("PASS target file: " + targets.Count + " entries");
                Directory.CreateDirectory(Path.Combine(temp, "utils")); Directory.CreateDirectory(Path.Combine(temp, "lists"));
                Bundle.WriteBackup(Path.Combine(temp, "utils", "game_filter.enabled"), "mode=udp\ntcp=400-500\nudp=600-700");
                var filter = GameFilter.Read(temp); Assert(filter.ActiveTcp == "12" && filter.ActiveUdp == "600-700", "game settings read");
                filter.Mode = "disabled"; filter.Save(temp);
                var disabled = GameFilter.Read(temp); Assert(disabled.Mode == "disabled" && disabled.Tcp == "400-500" && disabled.Udp == "600-700", "game switch preserves port ranges");
                disabled.Mode = "all"; disabled.Save(temp);
                var enabled = GameFilter.Read(temp); Assert(enabled.ActiveTcp == "400-500" && enabled.ActiveUdp == "600-700", "game switch enables TCP and UDP");
                File.WriteAllText(Path.Combine(temp, "lists", "ipset-all.txt"), "1.2.3.0/24\n");
                Bundle.SetIpMode(temp, "none"); Assert(Bundle.IpMode(temp) == "none", "ipset none");
                Bundle.SetIpMode(temp, "any"); Assert(Bundle.IpMode(temp) == "any", "ipset any");
                Bundle.SetIpMode(temp, "loaded"); Assert(File.ReadAllText(Path.Combine(temp, "lists", "ipset-all.txt")) == "1.2.3.0/24\n", "ipset restore");
                string saved = Path.Combine(temp, "list.txt"); Bundle.WriteBackup(saved, "first"); Bundle.WriteBackup(saved, "second");
                Assert(File.ReadAllText(saved) == "second" && File.ReadAllText(saved + ".ui-backup") == "first", "atomic save backup");
                report.AppendLine("PASS settings, IPSet mode cycle, restore and atomic backup on temporary fixtures");
                CheckStandard();
                report.AppendLine("PASS Flowseal status classification, per-target format, analytics and original score/Ping tie-break");
                Assert(AutoStart.Command("C:\\folder with spaces\\Zapret.exe") == "\"C:\\folder with spaces\\Zapret.exe\" --startup", "startup path quoting");
                report.AppendLine("PASS autostart command quoting (no registry entries changed)");
                CheckNetwork().GetAwaiter().GetResult();
                report.AppendLine("PASS local HTTP 200/403 classification, bounded body read and cancellation");
                CheckCurl().GetAwaiter().GetResult();
                report.AppendLine("PASS actual system curl: HTTP 200 and HTTP 403 both OK as in Flowseal; HEAD arguments");
                report.AppendLine("No winws process, service or system settings were changed by these tests.");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.txt"), report.ToString(), Encoding.UTF8); return 0;
            }
            catch (Exception ex)
            {
                report.AppendLine(ex.ToString()); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.txt"), report.ToString(), Encoding.UTF8); return 1;
            }
            finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        }
        static void CheckStandard()
        {
            Assert(StandardTest.Classify(new CurlReply { ExitCode = 0, Output = "403" }) == "OK", "original accepts HTTP403");
            Assert(StandardTest.Classify(new CurlReply { ExitCode = 28 }) == "ERROR", "curl timeout");
            Assert(StandardTest.Classify(new CurlReply { ExitCode = 35 }) == "UNSUP", "original TLS35 unsupported");
            Assert(StandardTest.Classify(new CurlReply { ExitCode = 60, Error = "SSL certificate problem" }) == "SSL", "certificate error");
            Assert(StandardTest.Classify(new CurlReply { ExitCode = 6, Error = "Could not resolve host" }) == "SSL", "original DNS label");
            Assert(StandardTest.Classify(new CurlReply { ExitCode = 2, Error = "unsupported protocol" }) == "UNSUP", "unsupported curl option");
            var rows = new List<StandardRow> { new StandardRow { Name = "DiscordMain", Ping = "42 ms", Checks = new List<ProtocolCheck> {
                new ProtocolCheck { Protocol = "HTTP", Status = "OK" }, new ProtocolCheck { Protocol = "TLS1.2", Status = "SSL" }, new ProtocolCheck { Protocol = "TLS1.3", Status = "UNSUP" }
            } }, new StandardRow { Name = "DNS", Ping = "Timeout", Checks = new List<ProtocolCheck>() } };
            var a = StandardTest.Summarize("A", rows);
            Assert(a.Passed == 1 && a.Errors == 1 && a.Unsupported == 1 && a.Total == 3 && a.PingOK == 1 && a.PingFail == 1, "original analytics counts");
            string text = StandardReport.Full(new List<TestResult> { a });
            foreach (string token in new[] { "Config: A (Type: standard)", "HTTP:OK", "TLS1.2:SSL", "TLS1.3:UNSUP", "| Ping: 42 ms", "=== ANALYTICS ===", "HTTP OK:", "UNSUP:", "Best config: A" }) Assert(text.Contains(token), "report " + token);
            var b = new TestResult { Strategy = "B", Passed = 1, Total = 3, PingOK = 2, Average = 9999 };
            Assert(StandardReport.Best(new[] { a, b }) == b, "tie uses ping not time");
            Assert(StandardReport.Best(new[] { new TestResult { Total = 3 } }) == null, "no working config");
        }
        static async Task CheckCurl()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () => {
                for (int i = 0; i < 8; i++)
                {
                    using (var client = await listener.AcceptTcpClientAsync())
                    using (var stream = client.GetStream())
                    {
                        byte[] request = new byte[4096]; int read = await stream.ReadAsync(request, 0, request.Length);
                        string text = Encoding.ASCII.GetString(request, 0, read); Assert(text.StartsWith("HEAD "), "curl uses HEAD like original");
                        string response = "HTTP/1.1 " + (text.Contains("/denied") ? "403 Forbidden" : "200 OK") + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                        byte[] bytes = Encoding.ASCII.GetBytes(response); await stream.WriteAsync(bytes, 0, bytes.Length);
                    }
                }
            });
            try
            {
                foreach (string path in new[] { "/ok", "/denied" })
                {
                    var reply = await StandardTest.Curl("http://127.0.0.1:" + port + path, "HTTP", CancellationToken.None);
                    Assert(StandardTest.Classify(reply) == "OK", "actual curl status: " + reply.Error);
                    Assert(reply.Output == (path == "/ok" ? "200" : "403"), "actual curl HTTP code");
                }
                var targets = new List<Target> { new Target { Name = "Loopback_OK", Address = "http://127.0.0.1:" + port + "/ok" }, new Target { Name = "Loopback_403", Address = "http://127.0.0.1:" + port + "/denied" } };
                int reported = 0;
                var result = await StandardTest.Run("LOCAL TEST FIXTURE (127.0.0.1)", targets, CancellationToken.None, row => Interlocked.Increment(ref reported));
                Assert(result.Passed == 6 && result.Total == 6 && result.Rows.Count == 2 && reported == 2, "full standard test callbacks and counts");
                Assert(result.Rows[0].Name == "Loopback_OK" && result.Rows[1].Checks.All(c => c.HttpCode == "403"), "target order and HTTP403 across protocols");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-standard.json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(result), Encoding.UTF8);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-standard.txt"), StandardReport.Full(new List<TestResult> { result }), Encoding.UTF8);
                await server;
            }
            finally { listener.Stop(); }
        }
        static async Task CheckNetwork()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () => {
                for (int i = 0; i < 2; i++)
                {
                    using (var client = await listener.AcceptTcpClientAsync())
                    using (var stream = client.GetStream())
                    {
                        var request = new byte[4096]; int read = await stream.ReadAsync(request, 0, request.Length);
                        bool denied = Encoding.ASCII.GetString(request, 0, read).Contains("/denied");
                        string response = "HTTP/1.1 " + (denied ? "403 Forbidden" : "200 OK") + "\r\nContent-Length: 70000\r\nConnection: close\r\n\r\n" + new string('x', 70000);
                        byte[] bytes = Encoding.ASCII.GetBytes(response); await stream.WriteAsync(bytes, 0, bytes.Length);
                    }
                }
            });
            try
            {
                var targets = new List<Target> { new Target { Name = "OK", Address = "http://127.0.0.1:" + port + "/ok" }, new Target { Name = "Denied", Address = "http://127.0.0.1:" + port + "/denied" } };
                var result = await NetworkTest.Run("Fixture", targets, CancellationToken.None);
                Assert(result.Passed == 1 && result.Total == 2, "HTTP status scoring: " + String.Join(" | ", result.Probes.Select(p => p.Name + ": " + p.Status)));
                Assert(result.Probes.All(p => p.Bytes == 65536), "64 KiB body limit");
                await server;
                using (var cancel = new CancellationTokenSource())
                {
                    cancel.Cancel(); bool caught = false;
                    try { await NetworkTest.Run("Cancel", targets, cancel.Token); } catch (OperationCanceledException) { caught = true; }
                    Assert(caught, "test cancellation");
                }
            }
            finally { listener.Stop(); }
        }
    }
}

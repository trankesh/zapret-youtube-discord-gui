using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ZapretDesktop
{
    public sealed class CurlReply
    {
        public int ExitCode;
        public string Output = "";
        public string Error = "";
    }
    public sealed class ProtocolCheck
    {
        public string Protocol { get; set; }
        public string Status { get; set; }
        public string HttpCode { get; set; }
        public string Detail { get; set; }
    }
    public sealed class StandardRow
    {
        public string Name { get; set; }
        public List<ProtocolCheck> Checks { get; set; }
        public string Ping { get; set; }
    }
    public static class StandardTest
    {
        public static string Classify(CurlReply reply)
        {
            if (Regex.IsMatch(reply.Error, "Could not resolve host|certificate|SSL certificate problem|self[- ]?signed|certificate verify failed|unable to get local issuer certificate", RegexOptions.IgnoreCase)) return "SSL";
            if (reply.ExitCode == 35 || Regex.IsMatch(reply.Error, "does not support|not supported|protocol\\s+'?.+'?\\s+not\\s+supported|unsupported protocol|TLS.*not supported|Unrecognized option|Unknown option|unsupported option|unsupported feature|schannel", RegexOptions.IgnoreCase)) return "UNSUP";
            return reply.ExitCode == 0 ? "OK" : "ERROR";
        }
        public static async Task<CurlReply> Curl(string address, string protocol, CancellationToken token)
        {
            var args = new List<string> { "-I", "-s", "-m", "4", "--connect-timeout", "2", "-o", "NUL", "-w", "%{http_code}", "--show-error" };
            if (protocol == "HTTP") args.Add("--http1.1");
            else { args.Add(protocol == "TLS1.2" ? "--tlsv1.2" : "--tlsv1.3"); args.Add("--tls-max"); args.Add(protocol == "TLS1.2" ? "1.2" : "1.3"); }
            args.Add(address);
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("Для стандартных тестов нужен системный curl.exe (Windows 10/11).", exe);
            using (var p = new Process { StartInfo = new ProcessStartInfo(exe, String.Join(" ", args.Select(Engine.Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } })
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                token.ThrowIfCancellationRequested(); p.Start();
                var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
                timeout.CancelAfter(6000);
                using (timeout.Token.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } }))
                {
                    await Task.Run(() => p.WaitForExit());
                    string text = await output, detail = await error;
                    token.ThrowIfCancellationRequested();
                    return new CurlReply { ExitCode = timeout.IsCancellationRequested ? 28 : p.ExitCode, Output = text.Trim(), Error = detail.Trim() };
                }
            }
        }
        public static async Task<TestResult> Run(string name, List<Target> targets, CancellationToken token, Action<StandardRow> onRow = null)
        {
            using (var limit = new SemaphoreSlim(Math.Min(16, Math.Max(8, Environment.ProcessorCount * 2))))
            {
                var tasks = targets.Select(async target => {
                    await limit.WaitAsync(token);
                    try
                    {
                        var row = new StandardRow { Name = target.Name, Checks = new List<ProtocolCheck>(), Ping = "Timeout" };
                        bool pingOnly = target.Address.StartsWith("PING:");
                        if (!pingOnly)
                        {
                            foreach (var protocol in new[] { "HTTP", "TLS1.2", "TLS1.3" })
                            {
                                token.ThrowIfCancellationRequested();
                                var reply = await Curl(target.Address, protocol, token);
                                row.Checks.Add(new ProtocolCheck { Protocol = protocol, Status = Classify(reply), HttpCode = reply.Output, Detail = reply.Error });
                            }
                        }
                        string host = pingOnly ? target.Address.Substring(5) : new Uri(target.Address).Host;
                        try
                        {
                            using (var ping = new Ping())
                            {
                                var reply = await ping.SendPingAsync(host, 1000);
                                if (reply.Status == IPStatus.Success) row.Ping = reply.RoundtripTime + " ms";
                            }
                        }
                        catch { token.ThrowIfCancellationRequested(); }
                        token.ThrowIfCancellationRequested();
                        if (onRow != null) onRow(row);
                        return row;
                    }
                    finally { limit.Release(); }
                }).ToArray();
                return Summarize(name, (await Task.WhenAll(tasks)).ToList());
            }
        }
        public static TestResult Summarize(string name, List<StandardRow> rows)
        {
            var checks = rows.SelectMany(r => r.Checks).ToList();
            var result = new TestResult { Strategy = name, Rows = rows, Probes = new List<Probe>(),
                Passed = checks.Count(c => c.Status == "OK"), Total = checks.Count,
                Errors = checks.Count(c => c.Status == "ERROR" || c.Status == "SSL"), Unsupported = checks.Count(c => c.Status == "UNSUP"),
                PingOK = rows.Count(r => r.Ping != "Timeout" && r.Ping != "n/a"), PingFail = rows.Count(r => r.Ping == "Timeout" || r.Ping == "n/a") };
            result.Summary = "HTTP OK: " + result.Passed + ", ERR: " + result.Errors + ", UNSUP: " + result.Unsupported + ", Ping OK: " + result.PingOK + ", Fail: " + result.PingFail;
            return result;
        }
    }
    public static class StandardReport
    {
        public static string Row(StandardRow row, int width)
        {
            string tokens = String.Join(" ", row.Checks.Select(c => (c.Protocol + ":" + c.Status).PadRight(13)));
            return "  " + row.Name.PadRight(width) + "    " + tokens + (row.Checks.Count > 0 ? " | Ping: " : "Ping: ") + row.Ping;
        }
        public static string Result(TestResult result)
        {
            var text = new StringBuilder(); text.AppendLine("Config: " + result.Strategy + " (Type: standard)");
            if (result.Rows == null || result.Rows.Count == 0) text.AppendLine("  > " + result.Summary);
            else { int width = result.Rows.Max(r => r.Name.Length); foreach (var row in result.Rows) text.AppendLine(Row(row, width)); }
            return text.ToString();
        }
        public static TestResult Best(IEnumerable<TestResult> candidates)
        {
            return candidates.Where(r => r.Total > 0 && r.Passed > 0).OrderByDescending(r => r.Passed).ThenByDescending(r => r.PingOK).FirstOrDefault();
        }
        public static string Analytics(List<TestResult> results)
        {
            if (results.Count == 0) return "";
            var text = new StringBuilder(); text.AppendLine("=== ANALYTICS ==="); int width = results.Max(r => r.Strategy.Length);
            foreach (var r in results) text.AppendLine(String.Format("{0} : HTTP OK: {1,3}, ERR: {2,3}, UNSUP: {3,3}, Ping OK: {4,3}, Fail: {5,3}", r.Strategy.PadRight(width), r.Passed, r.Errors, r.Unsupported, r.PingOK, r.PingFail));
            var best = Best(results.Where(r => r.Strategy != "Без Zapret"));
            text.AppendLine(); text.AppendLine("Best config: " + (best == null ? "—" : best.Strategy)); return text.ToString();
        }
        public static string Full(List<TestResult> results)
        {
            return String.Join(Environment.NewLine, results.Select(Result)) + Environment.NewLine + Analytics(results);
        }
    }
}

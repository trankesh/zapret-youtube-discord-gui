using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ZapretDesktop
{
    public sealed class DnsProvider
    {
        public string Id, Name, Description, Template, Source;
        public string[] V4, V6, Hosts;
        public override string ToString() { return Name; }
    }
    public sealed class DnsAdapter
    {
        public string Guid { get; set; }
        public string Name { get; set; }
        public string[] Servers { get; set; }
        public bool? DohEnabled { get; set; }
        public bool DohSupported { get; set; }
        public override string ToString() { return Name; }
    }
    public sealed class DnsState
    {
        public bool Doh { get; set; }
        public DnsAdapter[] Adapters { get; set; }
    }
    public static class SecureDns
    {
        public static readonly DnsProvider[] Providers = {
            new DnsProvider { Id="cloudflare", Name="Cloudflare", Description="Без фильтрации контента", V4=new[]{"1.1.1.1","1.0.0.1"}, V6=new[]{"2606:4700:4700::1111","2606:4700:4700::1001"}, Hosts=new[]{"cloudflare-dns.com","one.one.one.one"}, Template="https://cloudflare-dns.com/dns-query", Source="https://developers.cloudflare.com/1.1.1.1/infrastructure/network-operators/" },
            new DnsProvider { Id="google", Name="Google Public DNS", Description="Без фильтрации контента", V4=new[]{"8.8.8.8","8.8.4.4"}, V6=new[]{"2001:4860:4860::8888","2001:4860:4860::8844"}, Hosts=new[]{"dns.google"}, Template="https://dns.google/dns-query", Source="https://developers.google.com/speed/public-dns/docs/using" },
            new DnsProvider { Id="quad9", Name="Quad9", Description="Блокирует вредоносные домены", V4=new[]{"9.9.9.9","149.112.112.112"}, V6=new[]{"2620:fe::fe","2620:fe::9"}, Hosts=new[]{"dns.quad9.net"}, Template="https://dns.quad9.net/dns-query", Source="https://docs.quad9.net/services/" },
            new DnsProvider { Id="adguard", Name="AdGuard DNS", Description="Блокирует рекламу и трекеры", V4=new[]{"94.140.14.14","94.140.15.15"}, V6=new[]{"2a10:50c0::ad1:ff","2a10:50c0::ad2:ff"}, Hosts=new[]{"dns.adguard-dns.com"}, Template="https://dns.adguard-dns.com/dns-query", Source="https://adguard-dns.io/en/public-dns.html" },
            new DnsProvider { Id="opendns", Name="OpenDNS", Description="Защита от фишинга", V4=new[]{"208.67.222.222","208.67.220.220"}, V6=new[]{"2620:119:35::35","2620:119:53::53"}, Hosts=new[]{"dns.opendns.com"}, Template="https://dns.opendns.com/dns-query", Source="https://umbrella.cisco.com/blog/enhancing-support-dns-encryption-with-dns-over-https" }
        };
        public static string HostsEntries()
        {
            var text = new StringBuilder("# Secure DNS: official resolver endpoints\n");
            foreach (var provider in Providers) foreach (string ip in provider.V4.Concat(provider.V6)) text.AppendLine(ip + " " + String.Join(" ", provider.Hosts));
            text.AppendLine("146.112.41.2 doh.opendns.com"); text.AppendLine("2620:119:fc::2 doh.opendns.com");
            return text.ToString();
        }
        public static string HostsSource(string remote, bool include)
        {
            if (!include) return remote;
            var names = Providers.SelectMany(p => p.Hosts).Concat(new[]{"doh.opendns.com"}).ToArray();
            var text = new StringBuilder();
            foreach (string line in remote.Split(new[]{'\r','\n'}, StringSplitOptions.RemoveEmptyEntries)) {
                int comment = line.IndexOf('#'); string body = comment < 0 ? line : line.Substring(0,comment);
                string[] parts = body.Split((char[])null, StringSplitOptions.RemoveEmptyEntries); IPAddress ip;
                if (parts.Length >= 2 && IPAddress.TryParse(parts[0],out ip)) {
                    var kept = parts.Skip(1).Where(n => !names.Contains(n,StringComparer.OrdinalIgnoreCase)).ToArray();
                    if (kept.Length > 0) text.AppendLine(parts[0] + " " + String.Join(" ",kept) + (comment < 0 ? "" : " " + line.Substring(comment)));
                    else if (comment >= 0) text.AppendLine(line.Substring(comment));
                } else text.AppendLine(line);
            }
            return text.ToString() + HostsEntries();
        }
        internal static string Literal(string text) { return "'" + text.Replace("'", "''") + "'"; }
        internal static string Script(string action, string adapter, DnsProvider provider, string backup)
        {
            if (!new[]{"Read","Apply","Restore"}.Contains(action)) throw new ArgumentException("Unknown DNS action");
            if (action == "Apply" && (provider == null || !Providers.Contains(provider))) throw new ArgumentException("Unknown DNS provider");
            if (action == "Apply") { Guid id; if (!Guid.TryParse(adapter,out id)) throw new ArgumentException("Invalid adapter"); }
            string script;
            using (var stream = typeof(SecureDns).Assembly.GetManifestResourceStream("DnsControl.ps1")) using (var reader = new StreamReader(stream, Encoding.UTF8)) script = reader.ReadToEnd();
            var serializer = new JavaScriptSerializer();
            string nativeSource;
            using(var stream=typeof(SecureDns).Assembly.GetManifestResourceStream("InterfaceDns.cs")) using(var reader=new StreamReader(stream,Encoding.UTF8)) nativeSource=reader.ReadToEnd();
            string native="$null=Add-Type -TypeDefinition "+Literal(nativeSource)+" -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll' -ErrorAction Stop;\nfunction Get-ZapretInterfaceDns($id,$af) { [ZapretDesktop.InterfaceDns]::Read([guid]$id,($af -eq 'IPv6')) };\nfunction Test-ZapretInterfaceDns($af,$state) { [ZapretDesktop.InterfaceDns]::Validate(($af -eq 'IPv6'),($state | ConvertTo-Json -Depth 8 -Compress)) };\nfunction Set-ZapretInterfaceDns($id,$af,$state) { [ZapretDesktop.InterfaceDns]::Write([guid]$id,($af -eq 'IPv6'),($state | ConvertTo-Json -Depth 8 -Compress)) };\n";
            return native+"$action=" + Literal(action) + ";$adapterId=" + Literal(adapter ?? "") + ";$backup=" + Literal(backup ?? "") + ";$provider=" + (provider == null ? "$null" : "(" + Literal(serializer.Serialize(new { provider.Id, provider.Template, provider.V4, provider.V6 })) + " | ConvertFrom-Json)") + ";\n" + script;
        }
        public static Task<string> Execute(string action, string adapter, DnsProvider provider, string backup, CancellationToken token)
        {
            string bootstrap="$global:ProgressPreference='SilentlyContinue';[Console]::InputEncoding=New-Object Text.UTF8Encoding($false); & ([scriptblock]::Create([Console]::In.ReadToEnd()))";
            string code=Convert.ToBase64String(Encoding.Unicode.GetBytes(bootstrap));
            return Engine.RunTool(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"), new[]{"-NoProfile","-NonInteractive","-EncodedCommand",code}, token,Script(action,adapter,provider,backup));
        }
        public static async Task<DnsState> Read(CancellationToken token) { return new JavaScriptSerializer().Deserialize<DnsState>(await Execute("Read",null,null,null,token)); }
    }
}

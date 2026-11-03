using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ZapretDesktop
{
    internal sealed class GuiRelease
    {
        public string VersionText;
        public string ReleaseUrl;
        public Version Number;
        internal static Version VersionNumber(string text,out bool prerelease)
        {
            var match=Regex.Match(text ?? "",@"^[vV]?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$");
            int major,minor,patch; prerelease=match.Success && match.Groups[4].Success;
            if(!match.Success || !Int32.TryParse(match.Groups[1].Value,out major) || !Int32.TryParse(match.Groups[2].Value,out minor) || !Int32.TryParse(match.Groups[3].Value,out patch)) throw new InvalidDataException("Не удалось определить номер версии.");
            return new Version(major,minor,patch);
        }
        public int CompareTo(string installed)
        {
            bool prerelease; var current=VersionNumber(installed,out prerelease);
            int compare=Number.CompareTo(current); return compare==0 && prerelease ? 1 : compare;
        }
    }
    internal static class GuiUpdates
    {
        public const string Repository="https://github.com/trankesh/zapret-youtube-discord-gui";
        public const string ApiUrl="https://api.github.com/repos/trankesh/zapret-youtube-discord-gui/releases/latest";
        internal static GuiRelease Parse(string json)
        {
            Dictionary<string,object> values;
            try { values=new JavaScriptSerializer {MaxJsonLength=262144,RecursionLimit=64}.Deserialize<Dictionary<string,object>>(json); }
            catch(ArgumentException error) { throw new InvalidDataException("GitHub вернул некорректные сведения о релизе.",error); }
            catch(InvalidOperationException error) { throw new InvalidDataException("GitHub вернул некорректные сведения о релизе.",error); }
            object tag,draft,preview;
            if(values==null || !values.TryGetValue("tag_name",out tag) || !(tag is string) || !values.TryGetValue("draft",out draft) || !(draft is bool) || (bool)draft || !values.TryGetValue("prerelease",out preview) || !(preview is bool) || (bool)preview) throw new InvalidDataException("Не найден опубликованный стабильный релиз.");
            bool prerelease; var number=GuiRelease.VersionNumber((string)tag,out prerelease);
            if(prerelease) throw new InvalidDataException("Этот релиз предварительный и не предлагается для обновления.");
            // Build the destination ourselves: release body/URLs cannot redirect
            // an elevated application to another repository or executable.
            return new GuiRelease {Number=number,VersionText=number.ToString(3),ReleaseUrl=Repository+"/releases/tag/"+Uri.EscapeDataString((string)tag)};
        }
        public static async Task<GuiRelease> Check(CancellationToken token)
        {
            using(var client=new HttpClient {Timeout=TimeSpan.FromSeconds(15),MaxResponseContentBufferSize=262144}) {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretDesktop-UpdateCheck/1.0");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
                using(var response=await client.GetAsync(ApiUrl,token)) {
                    if(response.StatusCode==HttpStatusCode.NotFound) throw new IOException("На GitHub пока нет доступного стабильного релиза.");
                    if(response.StatusCode==HttpStatusCode.Forbidden || (int)response.StatusCode==429) throw new IOException("GitHub временно ограничил проверки. Попробуй позже.");
                    response.EnsureSuccessStatusCode();
                    return Parse(await response.Content.ReadAsStringAsync());
                }
            }
        }
        internal static void Test()
        {
            Func<string,GuiRelease> release=tag=>Parse("{\"tag_name\":\""+tag+"\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://example.invalid/\"}");
            if(release("v1.10.0").CompareTo("1.9.9")<=0 || release("v2.0.0").CompareTo("1.99.99")<=0 || release("1.0.2").CompareTo("1.0.1")<=0 || release("1.0.1").CompareTo("1.0.1")!=0 || release("v1.0.0").CompareTo("1.0.1")>=0 || release("1.0.1").CompareTo("1.0.1-rc.7")<=0) throw new Exception("FAILED GUI update version comparison");
            if(release("v1.0.1").ReleaseUrl!=Repository+"/releases/tag/v1.0.1") throw new Exception("FAILED GUI update destination");
            foreach(var json in new[]{"{}","[]","invalid","{\"tag_name\":\"v1.0.2\",\"draft\":true,\"prerelease\":false}","{\"tag_name\":\"v1.0.2\",\"draft\":false,\"prerelease\":true}","{\"tag_name\":\"v1.0.2-rc.1\",\"draft\":false,\"prerelease\":false}","{\"tag_name\":\"latest\",\"draft\":false,\"prerelease\":false}"}) {
                bool rejected=false; try { Parse(json); } catch(InvalidDataException) { rejected=true; }
                if(!rejected) throw new Exception("FAILED invalid GUI update response rejection");
            }
        }
    }
}

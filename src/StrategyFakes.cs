using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ZapretDesktop
{
    internal static class StrategyFakes
    {
        // Only file-backed arguments in the bundle's bin folder are selectable.
        static readonly Regex FileOption = new Regex(@"(?<=^|\s)(--dpi-desync-fake-[\w-]+=)""([^""]+\.bin)""", RegexOptions.IgnoreCase);

        static bool InBin(string path, string root)
        {
            return String.Equals(Path.GetDirectoryName(path), Path.GetFullPath(Path.Combine(root, "bin")), StringComparison.OrdinalIgnoreCase);
        }

        public static string[] Slots(string raw, string root)
        {
            return FileOption.Matches(raw).Cast<Match>().Select(m => m.Groups[2].Value)
                .Where(path => InBin(path, root)).Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        static string MapPath(Strategy strategy, string root)
        {
            return Path.Combine(root, "utils", Path.GetFileName(strategy.File) + ".gui-fakes.json");
        }

        public static Dictionary<string,string> Read(Strategy strategy, string root)
        {
            string path = MapPath(strategy, root);
            return File.Exists(path) ? new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(path))
                : new Dictionary<string,string>();
        }

        static string Source(string root, string file)
        {
            if (String.IsNullOrWhiteSpace(file) || file != Path.GetFileName(file) || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !file.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Недопустимое имя пакета.");
            string path = Path.GetFullPath(Path.Combine(root,"bin",file));
            if (!File.Exists(path)) throw new FileNotFoundException("Выбранный файл пакета отсутствует.",path);
            return path;
        }

        public static void Set(Strategy strategy, string root, string slot, string file)
        {
            if (!Slots(Bundle.RawArguments(strategy, root, GameFilter.Read(root)), root).Contains(slot, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Выбранная стратегия не использует этот пакет.");
            var map = Read(strategy, root);
            if (file == null) map.Remove(slot);
            else { Source(root,file); map[slot] = file; }
            Bundle.WriteBackup(MapPath(strategy,root), new JavaScriptSerializer().Serialize(map));
        }

        public static string Apply(Strategy strategy, string root, string raw)
        {
            var map = Read(strategy,root);
            return FileOption.Replace(raw, m => {
                string path = m.Groups[2].Value, file;
                if (!InBin(path,root) || !map.TryGetValue(Path.GetFileName(path),out file)) return m.Value;
                return m.Groups[1].Value + "\"" + Source(root,file) + "\"";
            });
        }
    }
}

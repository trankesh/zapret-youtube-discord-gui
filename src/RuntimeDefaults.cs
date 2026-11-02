using System;
using System.IO;

namespace ZapretDesktop
{
    internal static class RuntimeDefaults
    {
        public static void Initialize()
        {
            AppContext.SetSwitch("Switch.System.Net.DontEnableSystemDefaultTlsVersions",false);
            AppContext.SetSwitch("Switch.System.Net.DontEnableStrongCrypto",false);
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges",false);
        }
        internal static byte[] Config(string exe)
        {
            if (File.Exists(exe+".config")) return File.ReadAllBytes(exe+".config");
            using (var source=typeof(RuntimeDefaults).Assembly.GetManifestResourceStream("Default.config"))
            using (var buffer=new MemoryStream()) { if (source==null) throw new IOException("Не найден встроенный config."); source.CopyTo(buffer); return buffer.ToArray(); }
        }
    }
}

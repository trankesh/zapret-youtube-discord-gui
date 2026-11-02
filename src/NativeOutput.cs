using System.Runtime.InteropServices;
using System.Text;

namespace ZapretDesktop
{
    internal static class NativeOutput
    {
        [DllImport("kernel32.dll")] static extern uint GetOEMCP();

        public static string Decode(byte[] bytes) { return Decode(bytes, (int)GetOEMCP()); }
        internal static string Decode(byte[] bytes, int legacyCodePage)
        {
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            // Native Windows tools use the OEM page on older installations and
            // UTF-8 when the system UTF-8 option is enabled. Decode the bytes
            // before any StreamReader can replace or corrupt Cyrillic letters.
            try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
            catch (DecoderFallbackException) { return Encoding.GetEncoding(legacyCodePage).GetString(bytes); }
        }
    }
}

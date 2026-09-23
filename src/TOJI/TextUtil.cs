using System.Globalization;
using System.Text;

namespace Toji
{
    public static class TextUtil
    {
        // Minusculas y sin tildes: "Telemetría" y "telemetria" se comparan igual
        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s.ToLowerInvariant().Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            return sb.ToString();
        }
    }
}

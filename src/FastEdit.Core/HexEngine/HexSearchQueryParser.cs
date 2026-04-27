using System.Globalization;
using System.Text;

namespace FastEdit.Core.HexEngine;

public static class HexSearchQueryParser
{
    public static byte[]? Parse(string query)
    {
        query = query.Trim();
        if (string.IsNullOrEmpty(query)) return null;

        if (query.StartsWith('"') && query.EndsWith('"') && query.Length >= 2)
        {
            var text = query[1..^1];
            return Encoding.UTF8.GetBytes(text);
        }

        var hex = query.Replace(" ", "").Replace("-", "");
        if (hex.Length % 2 != 0) return null;

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(
                    hex.AsSpan(i * 2, 2),
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out bytes[i]))
            {
                return null;
            }
        }

        return bytes;
    }
}

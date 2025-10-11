using System;
using System.Linq;

namespace Osm.Smo;

public static class ModuleNameSanitizer
{
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Module";
        }

        var trimmed = value.Trim();
        var sanitized = new string(trimmed
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "Module" : sanitized;
    }
}

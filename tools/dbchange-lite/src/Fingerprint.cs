using System;
using System.Security.Cryptography;
using System.Text;

namespace DbChangeLite;

public static class Fingerprint
{
    public static string OfBytes(ReadOnlySpan<byte> bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string OfText(byte[] bytes) => OfBytes(Encoding.UTF8.GetBytes(Canonical(bytes)));

    public static string OfString(string text) => OfBytes(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n")));

    public static string Canonical(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        return text.ReplaceLineEndings("\n");
    }

    public static bool Same(string a, string b) => string.Equals(Bare(a), Bare(b), StringComparison.OrdinalIgnoreCase);

    private static string Bare(string hash) => hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? hash[7..] : hash;
}

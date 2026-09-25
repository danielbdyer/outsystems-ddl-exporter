using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Estate.Kernel;

/// <summary>How DacFx rates a message it writes, as its DacMessageType does: an error, a warning, or a message (a status line, or a deployment script's PRINT output).</summary>
public enum DacFxMessageType
{
    Error,
    Warning,
    Message,
}

/// <summary>
/// One message DacFx writes, in the form it gives each in an exception's text, <c>Error SQL71501: [dbo].[V] has an unresolved reference
/// to object [dbo].[Missing].</c>: its type, its prefix (SQL) and number, its text, and the element type it concerns where DacFx names one.
/// A message that quotes a SQL Server error, as SQL72014 quotes the data-loss check's Msg 50000, carries that error's number in
/// <see cref="SqlServerNumber"/>, read once from the text, so the SQL Server adapter can classify it as it does a SqlException's.
/// </summary>
public sealed record DacFxMessage : IComparable<DacFxMessage>
{
    private static readonly Regex Line = new(@"\A(?<type>Error|Warning|Message)\s+(?<prefix>[A-Za-z]+)(?<number>[0-9]{1,9})\s*:\s*(?<text>.*?)\s*\z", RegexOptions.CultureInvariant);

    private static readonly Regex Msg = new(@"\bMsg ([0-9]{1,9})\b", RegexOptions.CultureInvariant);

    public DacFxMessage(DacFxMessageType type, string prefix, int number, string text, string? elementType)
    {
        (MessageType, Prefix, Number, Text, ElementType) = (type, prefix, number, text, elementType);
        SqlServerNumber = Msg.Match(text) is { Success: true } quoted ? int.Parse(quoted.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    public DacFxMessageType MessageType { get; }

    /// <summary>The code's letters: SQL for every message DacFx 170.5.96 writes.</summary>
    public string Prefix { get; }

    public int Number { get; }

    public string Text { get; }

    /// <summary>The type of the element the message concerns, as DacFx names it (SqlView), or null.</summary>
    public string? ElementType { get; }

    /// <summary>The number of the SQL Server error the text quotes as "Msg 2627", or null when it quotes none.</summary>
    public int? SqlServerNumber { get; }

    /// <summary>The message a line of DacFx's text holds, "Error SQL71501: text" or "Warning SQL71502: text"; null for any other line, such as "Altering Table [dbo].[T]...".</summary>
    public static DacFxMessage? Parse(string line) => Line.Match(line) is { Success: true } m
        ? new DacFxMessage(Enum.Parse<DacFxMessageType>(m.Groups["type"].Value), m.Groups["prefix"].Value, int.Parse(m.Groups["number"].Value, CultureInfo.InvariantCulture), m.Groups["text"].Value, null)
        : null;

    /// <summary>By type (errors first), prefix, number and text, all ordinal.</summary>
    public int CompareTo(DacFxMessage? other) =>
        other is null ? 1
        : MessageType.CompareTo(other.MessageType) is var t and not 0 ? t
        : string.CompareOrdinal(Prefix, other.Prefix) is var p and not 0 ? p
        : Number.CompareTo(other.Number) is var n and not 0 ? n
        : string.CompareOrdinal(Text, other.Text) is var x and not 0 ? x
        : string.CompareOrdinal(ElementType, other.ElementType);

    /// <summary>As DacFx writes it in an exception's text: Error SQL71501: the text.</summary>
    public override string ToString() => MessageType + " " + Prefix + Number.ToString(CultureInfo.InvariantCulture) + ": " + Text;
}

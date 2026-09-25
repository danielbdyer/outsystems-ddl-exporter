using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Estate.Kernel;
using Microsoft.SqlServer.Dac;

namespace Estate.Io;

/// <summary>
/// The pipeline's publish profile, read as data (V3_MILESTONES.md WP 1.5, §1 fact 10): a .publish.xml, through DacFx's own DacProfile, into
/// its deploy options and SQLCMD values alone, its target removed first, refusing a password anywhere in it and a SQLCMD value that is a
/// connection string. What DacFx ignores without a word is named: an element of the profile's PropertyGroup that is no option DacFx reads is
/// the note profile.unknown-option. An error names the file and the property and quotes no value, since a value can be a secret.
/// </summary>
public static class PublishProfiles
{
    /// <summary>The elements of a profile's PropertyGroup that are no DacDeployOptions property: the target, which Load removes, and what Visual Studio writes beside the options.</summary>
    private static readonly string[] ProfileOnly = ["TargetConnectionString", "TargetDatabaseName", "DeployScriptFileName", "ProfileVersionNumber", "IncludeCompositeObjects"];

    /// <summary>The names DacFx reads in a profile's PropertyGroup, ignoring case as DacProfile does (measured): each public property of DacDeployOptions, and the profile-only names.</summary>
    private static readonly HashSet<string> Options = new(typeof(DacDeployOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Concat(ProfileOnly),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>DacProfile's message for a value it cannot read, which quotes the value after the property's name (measured: "Property BlockOnPossibleDataLoss has an invalid value: …").</summary>
    private static readonly Regex InvalidValue = new(@"\AProperty (?<property>\w+) has an invalid value", RegexOptions.CultureInvariant);

    /// <summary>
    /// How a profile is kept, and so fingerprinted: UTF-8 with no byte-order mark, LF line ends, two-space indent. XDocument.Save's
    /// defaults follow Environment.NewLine and write a byte-order mark, so one profile kept on Windows and on Linux hashed apart.
    /// </summary>
    private static readonly XmlWriterSettings Kept = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), Indent = true, NewLineChars = "\n", NewLineHandling = NewLineHandling.Replace,
    };

    /// <summary>
    /// The publish profile at <paramref name="path"/> as Strict: DacFx's reading of it, its target removed first, with a note for each element
    /// DacFx ignores. A password is sought as the file is written and as DacFx reads each value, a comment inside it dropped and a character
    /// reference read. An error names the file and leads with <paramref name="subject"/>: "The profile" and the path, unless the caller says
    /// whose profile it is.
    /// </summary>
    public static Result<PublishProfile.Strict> Load(string path, string? subject = null)
    {
        subject ??= "The profile " + path;
        XDocument profile;
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            profile = XDocument.Load(reader);
            var read = profile.Descendants().SelectMany(e => e.Attributes().Select(a => a.Value).Append(string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value))));
            if (read.Prepend(Encoding.UTF8.GetString(bytes)).Any(ConnectionString.Password.IsMatch))
            {
                return new Error("profile.password", subject + " holds a password in a connection string; a profile gives deploy options and SQLCMD values alone.",
                    "Delete the connection string from " + path + ", and name the connection in " + Posture.Json + " as env:NAME or file:path.");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
            return e is FileNotFoundException or DirectoryNotFoundException ? new Error("profile.missing", "No publish profile at " + path + ".",
                    "Name the pipeline's .publish.xml by its path from the estate's root, as the profile of " + Posture.Json + " does.")
                : new Error("profile.unreadable", subject + (e is XmlException x
                    ? string.Create(CultureInfo.InvariantCulture, $" is not XML at line {x.LineNumber}, position {x.LinePosition}.") : " cannot be opened."),
                    "Correct the file at the place this names, or save the profile again from Visual Studio.");
        }

        profile.Descendants().Where(e => e.Name.LocalName is "TargetConnectionString" or "TargetDatabaseName").Remove();
        using var kept = new MemoryStream();
        using (var writer = XmlWriter.Create(kept, Kept))
        {
            profile.Save(writer);
        }

        IReadOnlyList<Finding> notes = [.. (profile.Root?.Elements().Where(e => e.Name.LocalName == "PropertyGroup").Elements() ?? []).Select(e => e.Name.LocalName)
            .Where(name => !Options.Contains(name)).Distinct(StringComparer.Ordinal)
            .Select(name => Finding.Note("profile.unknown-option", name, subject + " sets " + name + ", which is no option DacFx reads, so DacFx ignores it; an option it may misspell keeps its default."))];
        return DacFx.Guard(() => DacProfile.Load(new MemoryStream(kept.ToArray(), writable: false)).DeployOptions, failure => Unreadable(subject, path, failure))
            .Bind(options => !options.BlockOnPossibleDataLoss
                ? new Error("profile.data-loss-allowed", subject + " sets BlockOnPossibleDataLoss to False; Strict is the pipeline's profile with the data-loss check on.",
                    "Set BlockOnPossibleDataLoss to True in " + path + "; only a copy publishes with the data-loss check off, as Permissive.")
                : Result.All(options.SqlCommandVariableValues.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => ProfileValue(subject, path, v.Key, v.Value ?? "")))
                    .Map(values => new PublishProfile.Strict(path, kept.ToArray(), SortedArray.Of(values), notes)));
    }

    /// <summary>A named environment's profile, its errors led by the environment; io/SqlServer.SqlCmdValues sets the environment's own SQLCMD values over the profile's.</summary>
    public static Result<PublishProfile.Strict> Of(NamedEnvironment environment, string estateRoot) =>
        Load(Path.GetFullPath(Path.Combine(estateRoot, environment.Profile.ToString())), environment.Target + "'s profile " + environment.Profile);

    /// <summary>
    /// A profile DacFx does not read: profile.unreadable naming the property whose value DacFx refused, the value withheld, since DacFx's
    /// message quotes it; the whole message withheld when it names no property.
    /// </summary>
    private static Error Unreadable(string subject, string path, DacFx.DacFxFailure failure) =>
        failure.Messages.Select(m => InvalidValue.Match(m.Text)).FirstOrDefault(m => m.Success) is { } invalid
            ? new Error("profile.unreadable", subject + " sets " + invalid.Groups["property"].Value + " to a value DacFx does not read; the value is withheld, since it can be a secret.",
                "Set " + invalid.Groups["property"].Value + " in " + path + " to a value DacFx reads, as Visual Studio writes it: True or False for a switch.")
            : new Error("profile.unreadable", subject + " is a file DacFx does not read as a publish profile; its message is withheld, since it can quote a value.",
                "Compare it with a profile Visual Studio saves (Publish, then Save Profile As) and correct it.");

    /// <summary>A SQLCMD value a profile gives: a literal, refused under a name shaped like a credential or when it is a connection string.</summary>
    private static Result<SqlCmdVariable> ProfileValue(string subject, string path, string name, string value) =>
        SqlCmdVariable.Of(subject, name, value).Bind(literal => ConnectionString.IsConnection(value) ? new Error("profile.literal-connection",
            subject + " gives $(" + name + ") a literal connection string.",
            "Give $(" + name + ") as env:NAME or file:path in the environment's sqlcmd in " + Posture.Json + ", and delete its value from " + path + ".") : Result.Ok(literal));
}

/// <summary>
/// A publish profile as a plan and a publish use it (§1 fact 10): DacFx's deploy options and the profile's SQLCMD values, nothing else. It
/// keeps the profile's XML, its target removed, and reads a fresh copy of the options on each call, so a change a caller makes to one copy
/// reaches no other. Its two cases are closed: Strict, the pipeline's profile as loaded, and Permissive, the same with the data-loss check off.
/// </summary>
public abstract class PublishProfile
{
    private readonly byte[] _profile;

    private PublishProfile(string source, byte[] profile, SortedArray<SqlCmdVariable> sqlCmd) => (Source, _profile, SqlCmd) = (source, profile, sqlCmd);

    /// <summary>The file it was loaded from.</summary>
    public string Source { get; }

    /// <summary>The profile's own SQLCMD values: literals, none under a name shaped like a credential and none a connection string.</summary>
    public SortedArray<SqlCmdVariable> SqlCmd { get; }

    /// <summary>The provenance's profile input: the fingerprint of the profile as kept, its target removed, in UTF-8 with LF line ends and no byte-order mark on every operating system.</summary>
    public Fingerprint Fingerprint => Fingerprint.Of(_profile);

    public override string ToString() => (this is Strict ? "Strict: " : "Permissive: ") + Source;

    /// <summary>A fresh copy of the options, the data-loss check on for Strict and off for Permissive, for io/DacFx's plan and publish.</summary>
    internal DacDeployOptions Options()
    {
        var options = DacProfile.Load(new MemoryStream(_profile, writable: false)).DeployOptions;
        options.BlockOnPossibleDataLoss = this is Strict;
        return options;
    }

    /// <summary>
    /// The pipeline's profile as loaded, the data-loss check on: every plan uses it, and every publish unless a copy asks for Permissive. Its
    /// notes name each element of the profile DacFx ignores.
    /// </summary>
    public sealed class Strict : PublishProfile
    {
        internal Strict(string source, byte[] profile, SortedArray<SqlCmdVariable> sqlCmd, IReadOnlyList<Finding> notes)
            : base(source, profile, sqlCmd) => Notes = notes;

        /// <summary>A profile.unknown-option note for each element of the PropertyGroup that is no option DacFx reads, in the file's order.</summary>
        public IReadOnlyList<Finding> Notes { get; }
    }

    /// <summary>
    /// Strict with BlockOnPossibleDataLoss off and nothing else changed (§1 fact 10), for a copy alone (§2.1 rule 3), to see what the
    /// data-loss check would have stopped. SqlServer.Copy.Permissive is Of's one caller, so a Permissive profile exists only for a copy;
    /// CapabilityTests' "Permissive never reaches an environment" fails on a call from anywhere else in io.
    /// </summary>
    public sealed class Permissive : PublishProfile
    {
        private Permissive(Strict strict)
            : base(strict.Source, strict._profile, strict.SqlCmd)
        {
        }

        internal static Permissive Of(Strict strict) => new(strict);
    }
}

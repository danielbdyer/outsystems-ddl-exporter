using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using DbChange.Kernel;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;

namespace DbChange.Io;

public static partial class Ssdt
{
    /// <summary>
    /// A package as DacFx reads it, opened once from its bytes (DECISIONS.md, 2026-09-24: never by path, so no assembly beside a dacpac
    /// loads into this process): where it was read from; DacFx's package, which a plan and a publish take; its model, which Elements reads,
    /// read with ThrowOnModelErrors off so a model with blocking errors still reads whole; the pre- and post-deploy scripts as the build
    /// inlined them and its pre-plan script, each null when it has none; its refactorlog's operations in file order; the SQLCMD variables
    /// it declares; and the platform it targets. Its elements are read once, when first asked for. Disposing it releases the package and
    /// the model.
    /// </summary>
    public sealed class Package : IDisposable
    {
        private readonly Lazy<Result<ModelElements>> elements;

        internal Package(string source, DacPackage dac, TSqlModel model, string? preDeploy, string? postDeploy, string? prePlan,
            IReadOnlyList<RefactorLogOperation> refactors,
            SortedArray<SqlCmdName> declared, Platform platform)
        {
            (Source, Dac, Model, PreDeploy, PostDeploy, PrePlan, Refactors, Declared, Platform) = (source, dac, model, preDeploy,
                postDeploy, prePlan, refactors, declared, platform);
            elements = new(() => Ssdt.ReadModel(this));
        }

        /// <summary>Where the package was read from, as a message names it: its path, or the target it was extracted from.</summary>
        public string Source { get; }

        internal DacPackage Dac { get; }

        internal TSqlModel Model { get; }

        public string? PreDeploy { get; }

        public string? PostDeploy { get; }

        /// <summary>The script DacFx runs against a live target before it plans; a plan package to package runs none.</summary>
        public string? PrePlan { get; }

        public IReadOnlyList<RefactorLogOperation> Refactors { get; }

        /// <summary>The SQLCMD variables the package declares (DacPackage.SqlCmdVariables), each needing a value before a plan.</summary>
        public SortedArray<SqlCmdName> Declared { get; }

        public Platform Platform { get; }

        /// <summary>The package's model read into elements, once.</summary>
        public Result<ModelElements> Elements => elements.Value;

        public void Dispose()
        {
            Model.Dispose();
            Dac.Dispose();
        }
    }

    /// <summary>
    /// The package at <paramref name="path"/>, its bytes read once and opened as <see cref="Open(byte[], string)"/> opens them:
    /// package.unreadable for no file there or a file this identity cannot read.
    /// </summary>
    public static Result<Package> Open(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Error("package.unreadable", "No package at " + path + ".",
                "Name a .dacpac a build wrote, or build its project again.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Error("package.unreadable", path + " cannot be read: " + e.Message.TrimEnd('.') + ".",
                "Grant this identity read access to the package, or build its project again.");
        }

        return Open(bytes, path);
    }

    /// <summary>
    /// A package from its bytes, named <paramref name="source"/> in messages: its refactor.xml read through a zip archive over the bytes,
    /// then DacPackage.Load and TSqlModel.LoadFromDacpac each over a stream of the same bytes, so nothing opens the package by path. Bytes
    /// that are not a package DacFx reads are package.unreadable, quoting DacFx's messages; a refactor.xml that is not a refactorlog is
    /// refactorlog.unreadable, naming the package.
    /// </summary>
    internal static Result<Package> Open(byte[] bytes, string source)
    {
        IReadOnlyList<RefactorLogOperation> refactors;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            using var log = zip.GetEntry("refactor.xml")?.Open();
            refactors = log is null ? [] : Operations(log);
        }
        catch (InvalidDataException e)
        {
            return Unreadable(source, e.Message);
        }
        catch (XmlException e)
        {
            return new Error("refactorlog.unreadable", "refactor.xml inside " + source + " is not a refactorlog SSDT reads: " + e.Message,
                "Restore the project's refactorlog from git, then build the package again.");
        }

        var loaded = DacFx.Guard(() =>
            DacPackage.Load(new MemoryStream(bytes, writable: false), DacSchemaModelStorageType.Memory, FileAccess.Read),
            failure => Unreadable(source, failure.Quoted));
        if (loaded.Failed(out var dac, out var unloaded))
        {
            return unloaded;
        }

        var modelled = DacFx.Guard(() => TSqlModel.LoadFromDacpac(new MemoryStream(bytes, writable: false),
            new ModelLoadOptions(DacSchemaModelStorageType.Memory, loadAsScriptBackedModel: false) { ThrowOnModelErrors = false }),
                failure => Unreadable(source, failure.Quoted));
        var made = modelled.Bind(model =>
            Result.All(dac.SqlCmdVariables.Select(name => SqlCmdName.Of(source + " declares a SQLCMD variable that", name)))
            .Bind(declared =>
            Platform.Of(dac.TargetPlatform.ToString()).Map(platform =>
                new Package(source, dac, model, Text(dac.PreDeploymentScript), Text(dac.PostDeploymentScript), Text(dac.PrePlanScript),
                    refactors, SortedArray.Of(declared), platform))));
        if (made is Result<Package>.Failed)
        {
            (modelled as Result<TSqlModel>.Ok)?.Value.Dispose();
            dac.Dispose();
        }

        return made;
    }

    private static Error Unreadable(string source, string why) =>
        new Error("package.unreadable", source + " is not a package DacFx reads: " + why,
        "Name a .dacpac a build wrote, or build its project again.");

    private static string? Text(Stream? script)
    {
        using var reader = script is null ? null : new StreamReader(script);
        return reader?.ReadToEnd();
    }
}

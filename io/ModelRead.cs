using System.Collections.Generic;
using System.IO;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// read as a use case (V3_ARCHITECTURE.md §8.1): a target's model read whole into elements, from where the checkout stands (Standing).
/// A ref is built at its commit and its package read; a package is read as it is; a database is read once (DacFx.Extract), after this
/// identity is found to hold VIEW DEFINITION there, with the SQL Server a copy runs on and what the identity could read there. diff reads
/// its two sides through <see cref="Read"/>.
/// </summary>
public static class ModelRead
{
    /// <summary>
    /// A target's model read whole into elements, with the SQL Server a copy runs on; a package's model carries its refactorlog's renames,
    /// and a database's read says what the identity could read there.
    /// </summary>
    public sealed record Source(Target Target, Ssdt.ModelElements Model, Server? Server, bool IsDatabase, SqlServer.Readable? Readable = null)
    {
        /// <summary>The notes reading the target raised: each error DacFx found in the model, and, for a database, an identity without the server's scope.</summary>
        public IEnumerable<Finding> Notes => [.. Model.Notes(Target.ToString()), .. Readable?.Notes ?? []];
    }

    /// <summary>read: the target's model, stamped with the committed DacFx, the ledger's pin and a copy's SQL Server, as far as the work got.</summary>
    public static Stamped<Source> Run(Checkout checkout, Target target, string? project, SqlServer.QueryLog log)
    {
        var standing = Standing.Of(checkout);
        if (standing.Result is Result<Pin>.Failed { Error: var error })
        {
            return new(standing.Stamp, error);
        }

        var source = Read(checkout, target, project, log);
        return new(standing.Stamp! with { Server = source.Match<Server?>(read => read.Server, _ => null) }, source);
    }

    /// <summary>A target's model read whole, the standing not asked again: diff asks it once for both of its sides.</summary>
    internal static Result<Source> Read(Checkout checkout, Target target, string? project, SqlServer.QueryLog log) => target.Match(
        _ => Modelled(checkout, target, log), _ => Modelled(checkout, target, log), () => Modelled(checkout, target, log),
        reference => Ssdt.Build(checkout.Root, reference.Ref.ToString(), project, checkout.Tool, checkout.WorkingDirectory).Bind(built => Packaged(built.Built.Path))
            .Map(model => new Source(target, model, null, false)),
        dacpac => Packaged(Path.GetFullPath(Path.Combine(checkout.WorkingDirectory, dacpac.Path))).Map(model => new Source(target, model, null, false)));

    /// <summary>A package's model read into elements, the package opened once and released.</summary>
    private static Result<Ssdt.ModelElements> Packaged(string dacpac) => Ssdt.Open(dacpac).Bind(package =>
    {
        using (package)
        {
            return package.Elements;
        }
    });

    /// <summary>A database read once (DacFx.Extract), after this identity is found to hold VIEW DEFINITION there, with a copy's SQL Server and what the identity could read.</summary>
    private static Result<Source> Modelled(Checkout checkout, Target target, SqlServer.QueryLog log) => SqlServer.Resolve(target, checkout.Root).Bind(database =>
        SqlServer.Reach(database, log).Bind(readable => DacFx.Extract(database).Bind(package =>
        {
            using (package)
            {
                return package.Elements;
            }
        })
        .Bind(model => (database is SqlServer.Copy copy ? SqlServer.ServerOf(copy, log).Map(server => (Server?)server) : Result.Ok<Server?>(null))
            .Map(server => new Source(target, model, server, true, readable)))));
}

using System.Linq;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// diff as a use case (V3_ARCHITECTURE.md §8.5): the change between two targets' models, each read through ModelRead, with the renames
/// both refactorlogs record, names compared under the from side's collation, the target a deploy of the to side would plan against.
/// </summary>
public static class ModelDiff
{
    /// <summary>What diff answers: the two sides as read, the change from the one to the other, and the collation its names were compared under.</summary>
    public sealed record Answer(ModelRead.Source From, ModelRead.Source To, Change Change, Collation Collation);

    /// <summary>diff: the change, stamped with the committed DacFx, the ledger's pin and the SQL Server of a side that is a copy, as far as the work got.</summary>
    public static Stamped<Answer> Run(Checkout checkout, Target from, Target to, string? project, SqlServer.QueryLog log)
    {
        var standing = Standing.Of(checkout);
        if (standing.Result is Result<Pin>.Failed { Error: var error })
        {
            return new(standing.Stamp, error);
        }

        var answer = ModelRead.Read(checkout, from, project, log).Bind(before => ModelRead.Read(checkout, to, project, log).Bind(after =>
            Ssdt.CollationOf(before.Model.Elements).Bind(collation =>
                Change.Between(before.Model.Elements, after.Model.Elements, SortedArray.Of(before.Model.Renames.Concat(after.Model.Renames).Distinct()), collation)
                    .Map(change => new Answer(before, after, change, collation)))));
        return new(standing.Stamp! with { Server = answer.Match<Server?>(diff => diff.From.Server ?? diff.To.Server, _ => null) }, answer);
    }
}

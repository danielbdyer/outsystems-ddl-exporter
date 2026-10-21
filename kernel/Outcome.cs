using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DbChange.Kernel;

/// <summary>
/// What a prediction says of one environment (V3_MILESTONES.md §8, WP 2.1): the change applies, or it blocks; and, either way, the
/// operations of the environment's plan the change itself does not make (baseline drift), by object. The cases are closed. The outcome
/// of a proof, and whether a proof applies to a prediction, arrive with WP 4.2.
/// </summary>
public abstract record Outcome
{
    private Outcome()
    {
    }

    /// <summary>The operations of the environment's plan the change does not make, by the object each names.</summary>
    public abstract SortedArray<PlanOperation> BaselineDrift { get; init; }

    public T Match<T>(Func<Applies, T> applies, Func<Blocks, T> blocks) => this switch
    {
        Applies a => applies(a),
        Blocks b => blocks(b),
        _ => throw new UnreachableException(),
    };

    /// <summary>
    /// The outcome an environment's existing data predicts for a change. Each precondition, and TableEmpty on each table a
    /// BlockOnPossibleDataLoss site checks, is read from <paramref name="existing"/>; one it holds no state for is the error
    /// plan.uncounted, since a precondition never counted is never taken as clean, or plan.undeclared for a Computed or an Authored, whose
    /// state a person declares. The change blocks when a table a site checks holds a row, however clean its rows (law 9:
    /// BlockOnPossibleDataLoss reads row presence alone), or when a row breaks a rule, which no row of a Scan or a Rebuild does; else it
    /// applies. A change that blocks ships in two releases when a populated table's site stops a tightening the preconditions make on that
    /// table (NotNull or Fits); else as <paramref name="recognized"/> names it, the shape the operation's recognize: block gives (WP 2.4);
    /// else it is to be proved.
    /// </summary>
    public static Result<Outcome> Predict(ExistingData existing, SortedArray<BlockOnPossibleDataLossSite> sites,
        SortedArray<Precondition> preconditions, SortedArray<PlanOperation> baselineDrift, string? recognized = null)
    {
        var states = new List<PreconditionState>();
        foreach (var precondition in preconditions
            .Concat(sites.Select(s => new Precondition(s.Table, [], new PreconditionKind.TableEmpty()))).Distinct())
        {
            if (existing.State(precondition) is not { } state)
            {
                return precondition.Kind.Declared
                    ? new Error("plan.undeclared",
                        "The existing data holds no state for " + precondition + ", a precondition of the plan that a person declares and"
                            + " no count answers; an undeclared precondition is never taken as clean.",
                        "Look at the rows the statement reads in the environment, and declare a witness row for each shape it meets, or"
                            + " that it meets none.")
                    : new Error("plan.uncounted",
                        "The existing data holds no state for " + precondition + ", a precondition of the plan; a precondition never"
                            + " counted is never taken as clean.",
                        "Predict again, so that every precondition of the plan is counted in the environment.");
            }

            states.Add(state);
        }

        var because = SortedArray.Of(states.Where(s => s.Precondition.Kind is PreconditionKind.TableEmpty ? s.Populated
            : s.Precondition.Kind.Breakable && s.Violating));
        if (because.Count == 0)
        {
            return new Applies(baselineDrift);
        }

        var tightened = because.Any(s => s.Precondition.Kind is PreconditionKind.TableEmpty
            && preconditions.Any(p => p.Table == s.Precondition.Table && p.Kind is PreconditionKind.NotNull or PreconditionKind.Fits));
        return new Blocks(because,
            tightened ? new Shipping.TwoReleases() : recognized is { } name ? new Shipping.Recognized(name) : new Shipping.ProveIt(),
            baselineDrift);
    }

    /// <summary>No precondition stops the change: no table a BlockOnPossibleDataLoss site checks holds a row, and no row breaks a
    /// rule.</summary>
    public sealed record Applies(SortedArray<PlanOperation> BaselineDrift) : Outcome;

    /// <summary>
    /// The states that stop the change, each a TableEmpty whose table holds a row (BlockOnPossibleDataLoss) or a rule a row breaks (SQL
    /// Server refusing the change on existing rows); and how the change can ship.
    /// </summary>
    public sealed record Blocks(SortedArray<PreconditionState> Because, Shipping Shipping, SortedArray<PlanOperation> BaselineDrift)
        : Outcome
    {
        /// <summary>Whether BlockOnPossibleDataLoss stops it, a table it checks holding a row; else a rule a row breaks does.</summary>
        public bool ByBlockOnPossibleDataLoss => Because.Any(s => s.Precondition.Kind is PreconditionKind.TableEmpty);
    }
}

/// <summary>
/// What a prediction carries from an environment to the pull request (V3_MILESTONES.md §3, WP 2.5): the environment; its existing data,
/// the states of its plan's preconditions and its trust list; the counts the states came from, each precondition's rows and the rows
/// that break it, which the privacy closure admits; and the claim's provenance. The machine block of predict's Markdown holds one per
/// environment, and the gate (M5) proves at its existing data.
/// </summary>
public sealed record Prediction(EnvironmentName Environment, ExistingData ExistingData, SortedArray<PreconditionCount> Counts,
    Provenance Provenance);

/// <summary>
/// How a change that blocks can ship: in two releases, when BlockOnPossibleDataLoss stops a tightening of a populated table; else as the
/// operation's recognize: block names it (WP 2.4: drop column, drop table, table rebuild, add mandatory); else it is to be proved. The
/// cases are closed.
/// </summary>
public abstract record Shipping
{
    private Shipping()
    {
    }

    public T Match<T>(Func<T> twoReleases, Func<string, T> recognized, Func<T> proveIt) => this switch
    {
        TwoReleases => twoReleases(),
        Recognized r => recognized(r.Name),
        ProveIt => proveIt(),
        _ => throw new UnreachableException(),
    };

    /// <summary>Two releases: the first makes the change in a pre-deploy script while the model keeps the old shape, and the second
    /// brings the model to it, which then plans nothing (finding F4).</summary>
    public sealed record TwoReleases : Shipping;

    /// <summary>The shape the operation's recognize: block names.</summary>
    public sealed record Recognized(string Name) : Shipping;

    /// <summary>No recognize: block names a shape: prove the change on a copy.</summary>
    public sealed record ProveIt : Shipping;
}

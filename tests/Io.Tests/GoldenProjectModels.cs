using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.SqlServer.Dac.Model;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// The golden project's heads by the names these tests use, each the sample changes of tests/Golden/changes/ it applies, built once per
/// test run by GoldenProject, and the base built a second time into a folder of its own; then the base read again, alone, for the
/// reading's time.
/// </summary>
public sealed class GoldenProjectModels : IAsyncLifetime
{
    /// <summary>The refactorlog key of the rename sample change, tests/Golden/changes/rename-a-column/edits.txt.</summary>
    public const string RenameKey = "6d1c1b5e-3f0a-4c2e-9b7d-2a4f8e6c0d13";

    /// <summary>Each head by its name here, and the sample changes that make it, none for the base.</summary>
    private static readonly Dictionary<string, string[]> Heads = new()
    {
        ["base"] = [],
        ["make-mandatory"] = ["make-mandatory"],
        ["add a nullable column"] = ["add-a-nullable-column"],
        ["drop a column"] = ["drop-a-column"],
        ["widen a column"] = ["widen-a-column"],
        ["add a check constraint"] = ["add-a-check-constraint"],
        ["add a foreign key"] = ["add-a-foreign-key"],
        ["a seed edit"] = ["edit-the-post-deploy-seed"],
        ["a pre-deploy edit"] = ["edit-the-pre-deploy-script"],
        ["rename a column"] = ["rename-a-column"],
        ["unnamed constraints"] = ["add-unnamed-constraints"],
        ["rename beside a Host column"] = ["rename-a-column", "add-a-table-with-an-unnamed-check-on-two-columns"],
        ["grants"] = ["grant-two-roles"],
        ["every serialized form"] = ["add-one-object-of-each-serialized-type"],
    };

    public Dictionary<string, Ssdt.ModelElements> Models { get; } = [];

    public Dictionary<string, string> Dacpacs { get; } = [];

    public TimeSpan ReadingTime { get; private set; }

    public async Task InitializeAsync()
    {
        var built = await Task.WhenAll(Heads.Select(async head =>
        {
            SampleChange[] changes = [.. head.Value.Select(GoldenProject.Change)];
            return (Head: head.Key, Dacpac: await GoldenProject.Built(changes), Model: await GoldenProject.Model(changes));
        }).Append(Again()));
        foreach (var (head, dacpac, model) in built)
        {
            (Models[head], Dacpacs[head]) = (model, dacpac);
        }

        // The base read into elements once more, alone and warm, for its time.
        using var again = Ok(Ssdt.Open(Dacpacs["base"]));
        var clock = Stopwatch.StartNew();
        Assert.Equal(Models["base"], Ok(Ssdt.ReadModel(again)));
        ReadingTime = clock.Elapsed;
    }

    private static async Task<(string Head, string Dacpac, Ssdt.ModelElements Model)> Again() =>
        ("again", await GoldenProject.BuiltAgain(), await GoldenProject.ModelAgain());

    public Task DisposeAsync() => Task.CompletedTask;

    private static T Ok<T>(Result<T> result) =>
        result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));
}

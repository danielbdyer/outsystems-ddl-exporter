using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// What synthetic-copy status and down read of .dbchange/copies.json before they reach the local server (WP 3.6, §4 row 13): a synthetic
/// copy's name carries the first sixteen hexadecimal digits of its base's fingerprint and of its existing data's, and its row records
/// both in full, so status verifies each name against its row; down drops only this machine's rows, and connects to nothing where the
/// registry holds none of them, so the SessionEnd hook's synthetic-copy down --if-idle needs no local server on a machine with no copy.
/// </summary>
public sealed class SyntheticCopyRegistryTests
{
    private static readonly Fingerprint Base = Fingerprint.Of("a base");

    private static readonly Fingerprint Existing = Fingerprint.Of("an existing data");

    /// <summary>
    /// A name verifies against its row exactly when the row's full fingerprints begin with the digits the name carries: the base's, and
    /// the existing data's where the name carries them and only there; a row that records another fingerprint, none, or an existing data
    /// the name does not carry does not verify.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(true, "base", "existing data", true)]
    [InlineData(false, "base", null, true)]
    [InlineData(true, "other", "existing data", false)]
    [InlineData(true, "base", "other", false)]
    [InlineData(true, "base", null, false)]
    [InlineData(false, "base", "existing data", false)]
    [InlineData(true, null, "existing data", false)]
    public void A_synthetic_copy_s_name_verifies_against_its_row_exactly_when_the_row_s_fingerprints_begin_with_the_name_s_digits(
        bool planted, string? recordedBase, string? recordedExisting, bool verified)
    {
        var name = CopyName.Synthetic(Base, planted ? Existing : null);
        var row = new JsonObject
        {
            ["name"] = name.ToString(),
            ["base"] = recordedBase switch { "base" => Base.ToString(), null => null, _ => Fingerprint.Of(recordedBase).ToString() },
            ["existingData"] = recordedExisting switch
            {
                "existing data" => Existing.ToString(), null => null, _ => Fingerprint.Of(recordedExisting).ToString(),
            },
        };

        Assert.Equal(verified, SyntheticCopy.Verified(name, row));
    }

    /// <summary>
    /// down where the registry holds no synthetic copy of this machine: the row of a synthetic copy another machine registered, and a
    /// run's copy of this machine, which is the sweep's, stay as they are; down drops nothing, notes the other machine's copy, and sends
    /// no statement, so it answers without a local server.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Down_drops_nothing_and_connects_to_nothing_where_the_registry_holds_no_synthetic_copy_of_this_machine()
    {
        using var scratch = ScratchFolder.Temporary("synthetic-down");
        var other = CopyName.Synthetic(Base, null);
        var run = CopyName.Make(System.Environment.MachineName, 7, 0xabcdef01);
        var registry = new JsonObject
        {
            ["copies"] = new JsonArray(
                new JsonObject { ["name"] = other.ToString(), ["server"] = "localhost,11434", ["machine"] = "elsewhere", ["base"] = Base.ToString() },
                new JsonObject { ["name"] = run.ToString(), ["server"] = "localhost,11434", ["machine"] = run.Machine }),
        };
        var state = new LocalState(scratch.Path);
        Directory.CreateDirectory(state.Folder);
        File.WriteAllText(state.Copies, registry.ToJsonString());
        var log = SqlServer.QueryLog.Start(scratch.Path);

        var down = Value(SyntheticCopy.Down(new Checkout(scratch.Path, scratch.Path, null, Cli.Contract.Version), ifIdle: true, log));

        Assert.False(down.Busy);
        Assert.Empty(down.Copies);
        var note = Assert.Single(down.Notes);
        Assert.Equal(("synthetic-copy.elsewhere", other.ToString()), (note.Code, note.Subject));
        Assert.Equal(registry.ToJsonString(), File.ReadAllText(state.Copies));
        Assert.False(log.Written, "down sent a statement");
    }
}

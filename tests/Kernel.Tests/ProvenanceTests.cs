using System;
using System.Linq;
using CsCheck;
using Xunit;

namespace DbChange.Kernel.Tests;

/// <summary>
/// A claim's provenance names the inputs it stands on and those it lacks; a SQL Server is what SQL Server reports of itself, compared
/// across servers by its level alone; and the toolchain ledger's window admits the committed DacFx at the pin or the release before it.
/// </summary>
public sealed class ProvenanceTests
{
    private const string Digest = "sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090";

    private static readonly DateTimeOffset At = new(2026, 9, 25, 10, 15, 44, TimeSpan.Zero);

    /// <summary>DECISIONS.md, 2026-09-25: a drift claim's change is the deploy report's fingerprint and its schema the target's, which cli/Check.cs once swapped.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_drift_claim_holds_the_deploy_report_as_its_change_and_the_target_s_elements_as_its_schema()
    {
        var (schema, report, profile) = (Fingerprint.Of("the target's elements"), Fingerprint.Of("the deploy report"), Fingerprint.Of("the profile"));

        var drift = Provenance.Drift(schema, report, Version("170.5.96"), null, profile, Dev, At);

        Assert.Equal((report, schema, profile), (drift.Change, drift.Schema, drift.PublishProfile));
        Assert.Null(drift.ExistingData);
        Assert.Equal(("env:dev", At), (drift.Target.ToString(), drift.At));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_claim_lacks_its_existing_data_and_its_server_where_each_is_null_and_nothing_once_both_are_given()
    {
        var onDev = Provenance.Drift(Fingerprint.Of("schema"), Fingerprint.Of("report"), Version("170.5.96"), null, Fingerprint.Of("profile"), Dev, At);
        var onCopy = onDev with { Server = Server("16.0.4295.3", 160, Digest) };

        Assert.Equal(new[] { Provenance.Input.ExistingData, Provenance.Input.Server }, onDev.Lacking);
        Assert.Equal(new[] { Provenance.Input.ExistingData }, onCopy.Lacking);
        Assert.Empty((onCopy with { ExistingData = Fingerprint.Of("the existing data") }).Lacking);
    }

    /// <summary>R1: a claim transfers between servers of one major version and compatibility level, whatever their build or image.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Two_servers_are_at_one_level_exactly_when_their_major_version_and_compatibility_level_agree()
    {
        var container = Server("16.0.4295.3", 160, Digest);

        Assert.Equal(container.Level, Server("16.0.1000.6", 160, null).Level);
        Assert.NotEqual(container.Level, Server("15.0.4430.1", 160, null).Level);
        Assert.NotEqual(container.Level, Server("16.0.4295.3", 150, Digest).Level);
        Assert.NotEqual(container, Server("16.0.4295.3", 160, null));
        Assert.Equal(new ServerLevel(16, 160), container.Level);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("16.0", 160, null, "server.product-version")]
    [InlineData("16.0.4295.3.1", 160, null, "server.product-version")]
    [InlineData("16.0.4295.x", 160, null, "server.product-version")]
    [InlineData("16.0.4295.3", 165, null, "server.compatibility-level")]
    [InlineData("16.0.4295.3", 160, "sha512:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090", "server.image-digest")]
    [InlineData("16.0.4295.3", 160, "sha256:4402D880DD4C34BFA7D8705E56A86CD6C88DA80A1F6BBBE741F999E76264A090", "server.image-digest")]
    [InlineData("16.0.4295.3", 160, "4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090", "server.image-digest")]
    public void A_server_is_refused_for_a_version_of_other_than_four_numbers_a_level_SQL_Server_never_had_or_a_malformed_digest(string version, int level, string? image, string code) =>
        Assert.Equal(code, Assert.IsType<Result<Server>.Failed>(Kernel.Server.Of(version, level, image)).Error.Code);

    [Fact]
    [Trait("Category", "fast")]
    public void A_server_prints_its_version_its_level_and_its_image_where_it_has_one()
    {
        Assert.Equal("SQL Server 16.0.4295.3, compatibility level 160, image " + Digest, Server("16.0.4295.3", 160, Digest).ToString());
        Assert.Equal("SQL Server 16.0.4295.3, compatibility level 150", Server("16.0.4295.3", 150, null).ToString());
    }

    /// <summary>R13's window: the committed DacFx is the pin or the release immediately before it; anything else, newer included, is rejected.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("170.5.96", true)]
    [InlineData("170.4.71", true)]
    [InlineData("170.6.10", false)]
    [InlineData("170.3.93", false)]
    [InlineData("162.5.57", false)]
    [Trait("Value", "R1")]
    [Trait("Exit", "M1.6")]
    public void The_committed_DacFx_is_inside_the_pin_s_window_only_at_the_pin_or_the_release_before_it(string committed, bool inside)
    {
        var pin = Assert.IsType<Result<Pin>.Ok>(Pin.Of("170.5.96", "170.4.71")).Value;

        var error = pin.Rejects(Version(committed));

        Assert.Equal(inside ? null : "toolchain.outside-window", error?.Code);
    }

    /// <summary>R13: while the ledger's row reads UNPINNED, every DacFx is inside the window; DriftTests holds that each answer then says so.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "R1")]
    [Trait("Exit", "M1.6")]
    public void Unpinned_admits_every_DacFx_release()
    {
        Pin unpinned = new Pin.Unpinned();
        Assert.Null(unpinned.Rejects(Version("170.5.96")));
        Assert.Null(unpinned.Rejects(Version("162.5.57")));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("")]
    [InlineData("170")]
    [InlineData("v170.5.96")]
    [InlineData("170.5.96-preview")]
    [InlineData(" 170.5.96")]
    [InlineData("170..96")]
    [InlineData("1.2.3.4.5")]
    public void A_DacFx_version_is_two_to_four_groups_of_digits_and_nothing_else(string text) =>
        Assert.Equal("toolchain.dacfx-version", Assert.IsType<Result<DacFxVersion>.Failed>(DacFxVersion.Of(text)).Error.Code);

    /// <summary>
    /// A ledger row is a pin only when both its versions are DacFx release versions and the release before is older than the pin, so
    /// the window never admits a newer release through it.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("170.5.96", "170.6.10", "toolchain.window-order")]
    [InlineData("170.5.96", "170.5.96", "toolchain.window-order")]
    [InlineData("170.5.96", "171.0.1", "toolchain.window-order")]
    [InlineData("170.10.1", "170.9.95", null)]
    [InlineData("170.5.96", "170.5.9", null)]
    [InlineData("170.5.96", null, null)]
    [InlineData("latest", null, "toolchain.dacfx-version")]
    [InlineData("170.5.96", "the one before", "toolchain.dacfx-version")]
    [Trait("Value", "R1")]
    public void A_ledger_row_is_rejected_for_a_text_that_is_no_version_or_a_release_before_that_is_not_older_than_the_pin(string release, string? before, string? code) =>
        Assert.Equal(code, (Pin.Of(release, before) as Result<Pin>.Failed)?.Error.Code);

    /// <summary>
    /// Versions order as System.Version orders the same numbers, so 170.10.0 follows 170.9.99 and a version comes before a longer one it
    /// prefixes; two writings of one number, 170.05.96 and 170.5.96, are two versions, and the order agrees with equality.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void DacFx_versions_order_group_by_group_as_numbers_and_agree_with_equality()
    {
        var group = Gen.Select(Gen.OneOf(Gen.Int[0, 3], Gen.Int[0, int.MaxValue]), Gen.Bool);   // a number, and whether a zero is written before it
        var version = group.Array[2, 4];
        Gen.Select(version, version).Sample((a, b) =>
        {
            var (x, y) = (Version(Written(a)), Version(Written(b)));
            var numbers = new System.Version(string.Join('.', a.Select(g => g.Item1))).CompareTo(new System.Version(string.Join('.', b.Select(g => g.Item1))));
            return (numbers == 0 || Math.Sign(x.CompareTo(y)) == Math.Sign(numbers))
                && (x.CompareTo(y) == 0) == (x == y) && Math.Sign(x.CompareTo(y)) == -Math.Sign(y.CompareTo(x));
        });

        static string Written((int Number, bool Zero)[] groups) => string.Join('.', groups.Select(g => (g.Zero ? "0" : "") + g.Number));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_default_DacFx_version_is_no_version_and_throws_when_printed() =>
        Assert.Throws<InvalidOperationException>(() => default(DacFxVersion).ToString());

    private static Target Dev => Assert.IsType<Result<Target>.Ok>(Target.Parse("env:dev", "--target")).Value;

    private static DacFxVersion Version(string text) => Assert.IsType<Result<DacFxVersion>.Ok>(DacFxVersion.Of(text)).Value;

    private static Server Server(string version, int level, string? image) => Assert.IsType<Result<Server>.Ok>(Kernel.Server.Of(version, level, image)).Value;
}

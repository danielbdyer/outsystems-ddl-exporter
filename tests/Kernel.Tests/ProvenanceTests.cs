using System;
using Xunit;

namespace Estate.Kernel.Tests;

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
        Assert.Null(drift.DataConditions);
        Assert.Equal(("env:dev", At), (drift.Target.ToString(), drift.At));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_claim_lacks_its_data_conditions_and_its_server_where_each_is_null_and_nothing_once_both_are_given()
    {
        var onDev = Provenance.Drift(Fingerprint.Of("schema"), Fingerprint.Of("report"), Version("170.5.96"), null, Fingerprint.Of("profile"), Dev, At);
        var onCopy = onDev with { Server = Server("16.0.4295.3", 160, Digest) };

        Assert.Equal(new[] { Provenance.Input.DataConditions, Provenance.Input.Server }, onDev.Lacking);
        Assert.Equal(new[] { Provenance.Input.DataConditions }, onCopy.Lacking);
        Assert.Empty((onCopy with { DataConditions = Fingerprint.Of("the data conditions") }).Lacking);
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
    public void The_committed_DacFx_is_inside_the_pin_s_window_only_at_the_pin_or_the_release_before_it(string committed, bool inside)
    {
        var pin = Assert.IsType<Result<Pin>.Ok>(Pin.Of("170.5.96", "170.4.71")).Value;

        var error = pin.Rejects(Version(committed));

        Assert.Equal(inside, error is null);
        Assert.Equal(inside ? null : "toolchain.outside-window", error?.Code);
        Assert.Equal("170.5.96", pin.ToString());
        var pinned = Assert.IsType<Pin.Pinned>(pin);
        Assert.Equal((Version("170.5.96"), (DacFxVersion?)Version("170.4.71")), (pinned.Release, pinned.Before));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Unpinned_admits_every_DacFx_release_and_says_so()
    {
        Pin unpinned = new Pin.Unpinned();
        Assert.Null(unpinned.Rejects(Version("170.5.96")));
        Assert.Null(unpinned.Rejects(Version("162.5.57")));
        Assert.Equal("UNPINNED", unpinned.ToString());
        Assert.True(unpinned.Match(_ => true, _ => false));
        Assert.Equal("toolchain.dacfx-version", Assert.IsType<Result<Pin>.Failed>(Pin.Of("latest", null)).Error.Code);
        Assert.Equal("toolchain.dacfx-version", Assert.IsType<Result<Pin>.Failed>(Pin.Of("170.5.96", "the one before")).Error.Code);
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

    /// <summary>A ledger row whose release before is not older than its pin is rejected, so the window never admits a newer release through it; versions order group by group as numbers.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("170.5.96", "170.6.10", false)]
    [InlineData("170.5.96", "170.5.96", false)]
    [InlineData("170.5.96", "171.0.1", false)]
    [InlineData("170.10.1", "170.9.95", true)]
    [InlineData("170.5.96", "170.5.9", true)]
    [InlineData("170.5.96", null, true)]
    public void A_release_before_that_is_not_older_than_the_pin_is_rejected(string release, string? before, bool made)
    {
        var pin = Pin.Of(release, before);

        Assert.Equal(made ? null : "toolchain.window-order", (pin as Result<Pin>.Failed)?.Error.Code);
        Assert.True(Version("170.10.0").CompareTo(Version("170.9.99")) > 0);
        Assert.True(Version("170.5").CompareTo(Version("170.5.0")) < 0);
        Assert.Equal(0, Version("170.5.96").CompareTo(Version("170.5.96")));
        Assert.Throws<InvalidOperationException>(() => default(DacFxVersion).ToString());
    }

    private static Target Dev => Assert.IsType<Result<Target>.Ok>(Target.Parse("env:dev", "--target")).Value;

    private static DacFxVersion Version(string text) => Assert.IsType<Result<DacFxVersion>.Ok>(DacFxVersion.Of(text)).Value;

    private static Server Server(string version, int level, string? image) => Assert.IsType<Result<Server>.Ok>(Kernel.Server.Of(version, level, image)).Value;
}

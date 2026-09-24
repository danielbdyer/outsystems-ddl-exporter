using System;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>A receipt carries the five inputs or names the one it lacks; its engine is DacFx and the image digest.</summary>
public sealed class ReceiptTests
{
    private const string Digest = "sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090";

    [Fact]
    [Trait("Category", "fast")]
    public void A_receipt_names_the_input_it_lacks()
    {
        var receipt = new Receipt(
            Fingerprint.Of("delta"), Fingerprint.Of("target"), Fingerprint.Of("facts"), Pinned(), Fingerprint.Of("profile"),
            "gate", new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
        Assert.Null(receipt.Lacking);
        Assert.Equal(Receipt.Input.DataFacts, (receipt with { DataFacts = null }).Lacking);
        Assert.Equal(receipt, receipt with { Target = Fingerprint.Of("target") });
        Assert.NotEqual(receipt, receipt with { Engine = Made(Engine.Of("170.5.96")) });
    }

    [Fact]
    [Trait("Category", "fast")]
    public void An_engine_is_a_dacfx_release_and_the_digest_of_the_image_it_ran_on()
    {
        Assert.Equal("DacFx 170.5.96, image " + Digest, Pinned().ToString());
        Assert.Equal("DacFx 170.5.96", Made(Engine.Of("170.5.96")).ToString());
        Assert.Equal(Pinned(), Made(Engine.Of("170.5.96", Digest)));
        Assert.NotEqual(Pinned(), Made(Engine.Of("162.5.57", Digest)));
        foreach (var version in new[] { "", "170", "v170.5.96", "170.5.96-preview", " 170.5.96", "170..96", "1.2.3.4.5" })
        {
            Assert.Equal("engine.dacfx-version", Refused(Engine.Of(version, Digest)));
        }

        foreach (var image in new[] { "", Digest[7..], Digest.ToUpperInvariant(), "sha512:" + Digest[7..] })
        {
            Assert.Equal("engine.image-digest", Refused(Engine.Of("170.5.96", image)));
        }

        Assert.Throws<InvalidOperationException>(() => default(Engine).DacFx);
    }

    /// <summary>R13's window: the committed engine is the pin or the release immediately before it; anything else, newer included, is refused.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("170.5.96", true)]
    [InlineData("170.4.71", true)]
    [InlineData("170.6.10", false)]
    [InlineData("170.3.93", false)]
    [InlineData("162.5.57", false)]
    public void An_engine_is_inside_the_pin_s_window_only_at_the_pin_or_the_release_before_it(string committed, bool inside)
    {
        var pin = Assert.IsType<Result<Pin>.Ok>(Pin.Of("170.5.96", "170.4.71")).Value;

        var refusal = pin.Refuses(Made(Engine.Of(committed, Digest)));

        Assert.Equal(inside, refusal is null);
        Assert.Equal(inside ? null : "toolchain.outside-window", refusal?.Code);
        Assert.Equal("170.5.96", pin.ToString());
        var pinned = Assert.IsType<Pin.Pinned>(pin);
        Assert.Equal((Version("170.5.96"), (DacFxVersion?)Version("170.4.71")), (pinned.Release, pinned.Before));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Unpinned_admits_every_engine_and_says_so()
    {
        Pin unpinned = new Pin.Unpinned();
        Assert.Null(unpinned.Refuses(Pinned()));
        Assert.Null(unpinned.Refuses(Made(Engine.Of("162.5.57"))));
        Assert.Equal("UNPINNED", unpinned.ToString());
        Assert.True(unpinned.Match(_ => true, _ => false));
        Assert.Equal("engine.dacfx-version", Assert.IsType<Result<Pin>.Refused>(Pin.Of("latest", null)).Refusal.Code);
        Assert.Equal("engine.dacfx-version", Assert.IsType<Result<Pin>.Refused>(Pin.Of("170.5.96", "the one before")).Refusal.Code);
    }

    /// <summary>A ledger row whose release before is not older than its pin is refused, so the window never admits a newer engine through it; versions order group by group as numbers.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("170.5.96", "170.6.10", false)]
    [InlineData("170.5.96", "170.5.96", false)]
    [InlineData("170.5.96", "171.0.1", false)]
    [InlineData("170.10.1", "170.9.95", true)]
    [InlineData("170.5.96", "170.5.9", true)]
    [InlineData("170.5.96", null, true)]
    public void A_release_before_that_is_not_older_than_the_pin_is_refused(string release, string? before, bool made)
    {
        var pin = Pin.Of(release, before);

        Assert.Equal(made ? null : "toolchain.window-order", (pin as Result<Pin>.Refused)?.Refusal.Code);
        Assert.True(Version("170.10.0").CompareTo(Version("170.9.99")) > 0);
        Assert.True(Version("170.5").CompareTo(Version("170.5.0")) < 0);
        Assert.Equal(0, Version("170.5.96").CompareTo(Version("170.5.96")));
        Assert.Throws<InvalidOperationException>(() => default(DacFxVersion).ToString());
    }

    private static DacFxVersion Version(string text) => Assert.IsType<Result<DacFxVersion>.Ok>(DacFxVersion.Of(text)).Value;

    private static Engine Pinned() => Made(Engine.Of("170.5.96", Digest));

    private static Engine Made(Result<Engine> result) => Assert.IsType<Result<Engine>.Ok>(result).Value;

    private static string Refused(Result<Engine> result) => Assert.IsType<Result<Engine>.Refused>(result).Refusal.Code;
}

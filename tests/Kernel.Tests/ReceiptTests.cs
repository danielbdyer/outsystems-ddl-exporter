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

    private static Engine Pinned() => Made(Engine.Of("170.5.96", Digest));

    private static Engine Made(Result<Engine> result) => Assert.IsType<Result<Engine>.Ok>(result).Value;

    private static string Refused(Result<Engine> result) => Assert.IsType<Result<Engine>.Refused>(result).Refusal.Code;
}

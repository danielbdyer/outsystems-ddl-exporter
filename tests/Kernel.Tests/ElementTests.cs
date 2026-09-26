using System;
using System.Globalization;
using System.Linq;
using CsCheck;
using DbChange.Tests;
using Xunit;
using static DbChange.Kernel.Tests.ElementSets;

namespace DbChange.Kernel.Tests;

/// <summary>
/// An element is one DacFx object as io/Ssdt.ReadModel reads it, keyed by its type and name path, with its properties and
/// relationships in canonical order; a model is a SortedArray of them, and its fingerprint is law 3′'s kernel half: stable
/// whatever the order of construction, and changed by any edit. Its law tests carry that law as a trait.
/// </summary>
public sealed class ElementTests
{
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "3′ the model is complete")]
    [Trait("Exit", "M1.4")]
    public void The_fingerprint_of_a_model_is_independent_of_the_order_its_elements_are_given_in() =>
        Sets.SelectMany(set => Gen.Shuffle(set.ToArray()).Select(shuffled => (set, shuffled))).Sample((set, shuffled) =>
            Fingerprint.Of(SortedArray.Of(shuffled.Select(Rebuilt))) == Fingerprint.Of(set)
            && SortedArray.Of(shuffled.Select(Rebuilt)) == set);

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "3′ the model is complete")]
    [Trait("Exit", "M1.4")]
    public void Two_models_fingerprint_equally_exactly_when_their_elements_are_equal()
    {
        Gen.Select(Sets, Sets).Sample((a, b) => (Fingerprint.Of(a) == Fingerprint.Of(b)) == (a == b));
        Assert.Equal(Fingerprint.Of(SortedArray.Of<Element>()), Fingerprint.Of(default(SortedArray<Element>)));
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "3′ the model is complete")]
    [Trait("Exit", "M1.4")]
    public void Any_single_edit_to_a_key_a_property_value_a_relationship_target_or_a_script_changes_the_fingerprint() =>
        Edits.Sample((before, edit) => Fingerprint.Of(before) != Fingerprint.Of(edit.After), print: x => x.Item2.Kind, iter: 1000);

    [Theory]
    [Trait("Category", "fast")]
    [Trait("Law", "3′ the model is complete")]
    [MemberData(nameof(SampleChanges.Names), MemberType = typeof(SampleChanges))]
    [Trait("Exit", "M1.4")]
    public void Every_sample_change_changes_the_fingerprint(string sample)
    {
        var (before, after, _) = SampleChanges.Pair(sample);
        Assert.NotEqual(Fingerprint.Of(before), Fingerprint.Of(after));
    }

    // Pairs that run together without the mechanism each names: lengths, value tags, counts, UTF-16 code units. The label names the case in the runner's output.
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Law", "3′ the model is complete")]
    [InlineData("lengths: a two-part name split at another place", 0)]
    [InlineData("lengths: an enumeration's type and member split at another place", 1)]
    [InlineData("tags: a text whose length and code units spell an integer", 2)]
    [InlineData("tags: false and zero", 3)]
    [InlineData("counts: a null property and an absent one", 4)]
    [InlineData("counts: one relationship of two targets and two of one", 5)]
    [InlineData("code units: two lone surrogates", 6)]
    [InlineData("parents: a child key and a top-level key", 7)]
    [InlineData("tags: a text and a script of one content", 8)]
    [Trait("Exit", "M1.4")]
    public void Models_a_serialization_without_lengths_tags_or_counts_would_confuse_fingerprint_differently(string what, int pair)
    {
        var t = Key("Table", "dbo", "T");
        var (a, b) = pair switch
        {
            0 => (New(Key("Table", "ab", "c"), []), New(Key("Table", "a", "bc"), [])),
            1 => (New(t, [("Order", new Value.Enumeration("Ab", "C"))]), New(t, [("Order", new Value.Enumeration("A", "bC"))])),
            2 => (New(t, [("Length", Text("\u0000\u0001"))]), New(t, [("Length", Int(0x0000_0002_0000_0001))])),
            3 => (New(t, [("IsClustered", Bool(false))]), New(t, [("IsClustered", Int(0))])),
            4 => (New(t, [("Collation", new Value.Null())]), New(t, [])),
            5 => (New(t, [], [("Columns", [t, t])]), New(t, [], [("Columns", [t]), ("Keys", [t])])),
            6 => (New(t, [("Text", Text("\uD800"))]), New(t, [("Text", Text("\uD801"))])),
            7 => (New(Key(t, "Column", "c"), []), New(Key("Column", "dbo", "T.c"), [])),
            _ => (New(t, [("Expression", Text("(0)"))]), New(t, [("Expression", new Value.Script("(0)"))])),
        };
        Assert.True(Fingerprint.Of([a]) != Fingerprint.Of([b]), what + ": the two models fingerprint alike");
        Assert.NotEqual(a, b);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Values_are_equal_exactly_when_their_order_says_so_and_the_order_is_antisymmetric() =>
        Gen.Select(Values, Values).Sample((a, b) =>
            (a.CompareTo(b) == 0) == (a == b) && Math.Sign(a.CompareTo(b)) == -Math.Sign(b.CompareTo(a)) && a.CompareTo(null) > 0);

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("")]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [Trait("Value", "D2")]
    public void A_value_compares_and_renders_ordinally_whatever_the_culture(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.True(Text("B").CompareTo(Text("a")) < 0);
            Assert.True(Text("I").CompareTo(Text("i")) < 0);
            Assert.NotEqual(Text("i"), Text("I"));
            Assert.NotEqual<Value>(Text("1"), Int(1));
            Assert.Equal(
                new[] { "NULL", "false", "true", "-1234567", "'it''s'", "SqlDataType.NVarChar", "'SELECT 1;'" },
                SortedArray.Of<Value>(new Value.Script("SELECT 1;"), new Value.Enumeration("SqlDataType", "NVarChar"), Text("it's"), Int(-1234567), Bool(true), Bool(false), new Value.Null())
                    .Select(v => v.ToString()));
            Assert.NotEqual<Value>(Text("(0)"), new Value.Script("(0)"));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_column_is_keyed_under_its_table_and_sorts_after_it()
    {
        var customer = Key("Table", "dbo", "Customer");
        var email = Key(customer, "Column", "Email");

        Assert.Equal("Column [dbo].[Customer].[Email]", email.ToString());
        Assert.Equal(customer, email.Parent);
        Assert.Equal(
            new[] { customer, email, Key(customer, "Index", "Email"), Key("Table", "dbo", "Customers") },
            SortedArray.Of(Key("Table", "dbo", "Customers"), Key(customer, "Index", "Email"), email, customer));
        Assert.NotEqual(Key("Table", "dbo", "Customer"), Key("View", "dbo", "Customer"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void An_element_is_the_same_whatever_order_its_properties_and_relationships_come_in() =>
        Sets.Sample(set => set.All(e => Rebuilt(e) == e
            && e.Properties.Select(p => p.Name).SequenceEqual(e.Properties.Select(p => p.Name).Order(StringComparer.Ordinal))
            && e.Relationships.Select(r => r.Name).SequenceEqual(e.Relationships.Select(r => r.Name).Order(StringComparer.Ordinal))));

    [Fact]
    [Trait("Category", "fast")]
    public void A_relationship_keeps_its_targets_in_the_order_given()
    {
        var customer = Key("Table", "dbo", "Customer");
        var (id, email) = (Key(customer, "Column", "Id"), Key(customer, "Column", "Email"));

        Assert.Equal(new[] { email, id }, Element.Relationship.Of("Columns", [email, id]).Targets.Select(t => t.Key));
        Assert.NotEqual(Element.Relationship.Of("Columns", [email, id]), Element.Relationship.Of("Columns", [id, email]));
        Assert.Empty(New(customer, [], [("Columns", [])]).Relationships);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void An_element_rejects_a_blank_or_repeated_property_or_relationship_name_and_a_key_rejects_a_malformed_part()
    {
        var t = Key("Table", "dbo", "T");
        var one = new Element.Property("Nullable", Bool(true));

        Assert.Equal("element.property-name", Code(Element.Of(t, [one, one with { Value = Bool(false) }], [])));
        Assert.Equal("element.property-name", Code(Element.Of(t, [new Element.Property(" ", Bool(true))], [])));
        Assert.Equal("element.relationship-name", Code(Element.Of(t, [], [Element.Relationship.Of("Columns", [t]), Element.Relationship.Of("Columns", [t, t])])));
        Assert.Equal("element.type-blank", Code(ElementKey.Of(" ", Ok(Name.Of("T")))));
        Assert.Equal("element.name-missing", Code(ElementKey.Of("Table", default)));
        Assert.Equal("element.child-name", Code(ElementKey.Of(t, "Column", Ok(Name.Of("dbo", "c")))));
    }

    /// <summary>One text gives two different script elements, one per deploy script; an entry renders as RefactorLogOperation [key] and carries only the properties given.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_deploy_scripts_and_a_refactorlog_entry_are_elements_of_their_own_types()
    {
        var entry = Ok(Element.RefactorLogEntry("7f1a2c3e-0b4d-4e5f-8a9b-0c1d2e3f4a5b", [new Element.Property("NewName", Text("[EmailAddress]"))]));

        Assert.NotEqual(Element.PreDeploy("PRINT 1;"), Element.PostDeploy("PRINT 1;"));
        Assert.Equal("RefactorLogOperation [7f1a2c3e-0b4d-4e5f-8a9b-0c1d2e3f4a5b]", entry.Key.ToString());
        Assert.Equal(Text("[EmailAddress]"), entry["NewName"]);
        Assert.Null(entry["ElementName"]);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_rename_keeps_the_parent_and_the_schema_and_changes_the_name_alone()
    {
        var customer = Key("Table", "dbo", "Customer");

        Assert.Equal(new Rename(customer, Key("Table", "dbo", "Client")), Ok(Rename.Of(customer, "Client")));
        Assert.Equal(Key(customer, "Column", "EmailAddress"), Ok(Rename.Of(Key(customer, "Column", "Email"), "EmailAddress")).After);
        Assert.Equal("name.blank", Code(Rename.Of(customer, "")));
    }

    private static string Code<T>(Result<T> result) => Expect.Failed(result).Code;
}

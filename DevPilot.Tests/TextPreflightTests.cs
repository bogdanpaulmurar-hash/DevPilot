using DevPilot.Core.Guard;
using Xunit;

namespace DevPilot.Tests;

public class TextPreflightTests
{
    [Fact]
    public void Clean_text_has_no_hits()
    {
        Assert.True(TextPreflight.Check("Please summarise the meeting notes.\nThanks!").IsClean);
        Assert.True(TextPreflight.Check(null).IsClean);
        Assert.True(TextPreflight.Check("").IsClean);
    }

    [Theory]
    [InlineData("contact me: jane.doe@example.com", TextPreflight.EmailCategory)]
    [InlineData("pay to DE89370400440532013000 today", TextPreflight.IbanCategory)]
    [InlineData("card 4111 1111 1111 1111 expires", TextPreflight.CardCategory)]
    [InlineData("call +49 170 1234567 now", TextPreflight.PhoneCategory)]
    [InlineData("key = AKIAABCDEFGHIJKLMNOP", TextPreflight.SecretCategory)]
    [InlineData("password = \"hunter2hunter2\"", TextPreflight.SecretCategory)]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----", TextPreflight.SecretCategory)]
    public void Finds_common_patterns(string text, string category) =>
        Assert.Contains(TextPreflight.Check(text).Hits, h => h.Category == category);

    [Fact]
    public void Numbers_that_fail_the_checksums_are_not_flagged()
    {
        Assert.DoesNotContain(TextPreflight.Check("order 4111 1111 1111 1112").Hits, h => h.Category == TextPreflight.CardCategory);
        Assert.DoesNotContain(TextPreflight.Check("ref DE89370400440532013001").Hits, h => h.Category == TextPreflight.IbanCategory);
        Assert.DoesNotContain(TextPreflight.Check("id 0000 0000 0000 0000").Hits, h => h.Category == TextPreflight.CardCategory);
    }

    [Fact]
    public void Deny_terms_are_found_by_position_without_naming_the_term()
    {
        var policy = new LocalPolicy { DenyTerms = ["Project-X"] };
        var result = TextPreflight.Check("first line\nnotes on project-x and PROJECT-X again", policy);
        Assert.Equal(2, result.Hits.Count(h => h.Category == TextPreflight.DenyCategory));
        var hit = result.Hits.First(h => h.Category == TextPreflight.DenyCategory);
        Assert.Equal(2, hit.Line);
        Assert.Equal(10, hit.Column);
        Assert.DoesNotContain("project-x", result.Describe(), StringComparison.OrdinalIgnoreCase);
        Assert.True(TextPreflight.Check("notes on project-x").IsClean);
    }

    [Fact]
    public void Positions_are_one_based_and_sorted()
    {
        var result = TextPreflight.Check("ok\r\nmail a@example.com\r\nand b@example.com");
        var positions = result.Hits.Select(h => (h.Line, h.Column)).ToList();
        Assert.Equal(2, positions.Count);
        Assert.Equal((2, 6), positions[0]);
        Assert.Equal((3, 5), positions[1]);
    }

    [Fact]
    public void Describe_lists_categories_and_positions_but_never_the_matched_text()
    {
        var result = TextPreflight.Check("mail jane.doe@example.com and password = \"hunter2hunter2\"");
        var text = result.Describe();
        Assert.Contains(TextPreflight.EmailCategory, text);
        Assert.Contains("line 1", text);
        Assert.DoesNotContain("jane.doe", text);
        Assert.DoesNotContain("hunter2", text);
        Assert.Equal("Nothing suspicious found.", TextPreflight.Check("fine").Describe());
    }

    [Fact]
    public void Many_hits_are_capped()
    {
        var text = string.Join('\n', Enumerable.Repeat("a@example.com", TextPreflight.MaxHits + 50));
        var result = TextPreflight.Check(text);
        Assert.Equal(TextPreflight.MaxHits, result.Hits.Count);
        Assert.True(result.Truncated);
    }

    [Theory]
    [InlineData("4111111111111111", true)]
    [InlineData("4111111111111112", false)]
    [InlineData("411111111111", false)]
    public void Luhn(string digits, bool valid) => Assert.Equal(valid, TextPreflight.IsValidCard(digits));
}

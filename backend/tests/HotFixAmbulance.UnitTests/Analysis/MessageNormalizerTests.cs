using FluentAssertions;
using HotFixAmbulance.Analysis;
using Xunit;

namespace HotFixAmbulance.UnitTests.Analysis;

public sealed class MessageNormalizerTests
{
    [Fact]
    public void Normalize_lowercases_and_collapses_whitespace()
    {
        MessageNormalizer.Normalize("  Hello   WORLD  ").Should().Be("hello world");
    }

    [Fact]
    public void Normalize_replaces_guids_with_token()
    {
        MessageNormalizer.Normalize("user 6f1b8a2c-44a9-4f9a-8b6e-9f7f7f7f7f7f failed")
            .Should().Be("user <id> failed");
    }

    [Fact]
    public void Normalize_replaces_long_numbers_with_token()
    {
        MessageNormalizer.Normalize("order 1234567 failed (retry 3)")
            .Should().Be("order <id> failed (retry 3)");
    }

    [Fact]
    public void Normalize_replaces_quoted_strings_with_token()
    {
        MessageNormalizer.Normalize("rejected \"abc-001\" because")
            .Should().Be("rejected <str> because");
    }

    [Fact]
    public void Normalize_returns_empty_for_null_or_blank()
    {
        MessageNormalizer.Normalize(null).Should().BeEmpty();
        MessageNormalizer.Normalize("   ").Should().BeEmpty();
    }
}

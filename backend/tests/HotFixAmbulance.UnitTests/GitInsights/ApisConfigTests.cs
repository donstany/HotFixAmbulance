using FluentAssertions;
using HotFixAmbulance.GitInsights;
using Xunit;

namespace HotFixAmbulance.UnitTests.GitInsights;

public sealed class ApisConfigTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"hfa-apis-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_tempPath))
        {
            File.Delete(_tempPath);
        }
    }

    [Fact]
    public void LoadFromFile_parses_known_apis()
    {
        File.WriteAllText(_tempPath, """
        {
          "apis": {
            "checkout-api": { "url": "https://github.com/myPOStech/checkout-api.git", "branch": "main" },
            "orders-api":   { "url": "https://github.com/myPOStech/orders-api.git",   "branch": "release" }
          }
        }
        """);

        var config = ApisConfig.LoadFromFile(_tempPath);

        config.TryGet("checkout-api", out var entry).Should().BeTrue();
        entry!.Url.Should().Be(new Uri("https://github.com/myPOStech/checkout-api.git"));
        entry.Branch.Should().Be("main");
        config.TryGet("orders-api", out var orders).Should().BeTrue();
        orders!.Branch.Should().Be("release");
    }

    [Fact]
    public void TryGet_returns_false_for_unknown_api()
    {
        File.WriteAllText(_tempPath, """
        { "apis": { "checkout-api": { "url": "https://x/checkout-api.git", "branch": "main" } } }
        """);

        var config = ApisConfig.LoadFromFile(_tempPath);

        config.TryGet("unknown-api", out var entry).Should().BeFalse();
        entry.Should().BeNull();
    }

    [Fact]
    public void LoadFromFile_throws_when_file_missing()
    {
        var act = () => ApisConfig.LoadFromFile(Path.Combine(Path.GetTempPath(), "no-such.json"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void LoadFromFile_defaults_branch_to_main_when_omitted()
    {
        File.WriteAllText(_tempPath, """
        { "apis": { "x-api": { "url": "https://x/x-api.git" } } }
        """);

        var config = ApisConfig.LoadFromFile(_tempPath);

        config.TryGet("x-api", out var entry).Should().BeTrue();
        entry!.Branch.Should().Be("main");
    }
}

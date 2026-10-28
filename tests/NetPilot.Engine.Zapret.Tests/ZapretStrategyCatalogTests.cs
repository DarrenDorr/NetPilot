using NetPilot.Engine.Zapret;

namespace NetPilot.Engine.Zapret.Tests;

public sealed class ZapretStrategyCatalogTests
{
    [Theory]
    [InlineData("general.bat", true)]
    [InlineData("general (ALT).bat", true)]
    [InlineData("general (ALT11).bat", true)]
    [InlineData("general (ALT42).bat", true)]
    [InlineData("general (EXPERIMENT_2).bat", true)]
    [InlineData("general (SIMPLE FAKE).bat", true)]
    [InlineData("general (FAKE TLS AUTO).bat", true)]
    [InlineData("service.bat", false)]
    [InlineData("general evil.bat", false)]
    [InlineData("..\\general (ALT11).bat", false)]
    [InlineData("general (ALT11).cmd", false)]
    [InlineData("general (BAD!).bat", false)]
    public void Validates_official_strategy_filename_shape(string fileName, bool expected)
    {
        Assert.Equal(expected, ZapretStrategyCatalog.IsOfficialStrategyFileName(fileName));
    }

    [Theory]
    [InlineData("general (ALT42).bat", "ALT42")]
    [InlineData("general (SIMPLE FAKE).bat", "SIMPLE_FAKE")]
    [InlineData("general (FAKE TLS AUTO).bat", "FAKE_TLS_AUTO")]
    public void Strategy_id_is_stable_and_safe(string fileName, string expectedId)
    {
        Assert.Equal(expectedId, ZapretStrategyCatalog.GetStrategyId(fileName));
    }

    [Fact]
    public void Discovery_is_top_level_and_ignores_unrelated_scripts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"NetPilot-Zapret-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var nested = Directory.CreateDirectory(Path.Combine(root, "nested"));

        try
        {
            File.WriteAllText(Path.Combine(root, "general.bat"), "test");
            File.WriteAllText(Path.Combine(root, "general (ALT11).bat"), "test");
            File.WriteAllText(Path.Combine(root, "general (SIMPLE FAKE).bat"), "test");
            File.WriteAllText(Path.Combine(root, "general evil.bat"), "test");
            File.WriteAllText(Path.Combine(root, "service.bat"), "test");
            File.WriteAllText(Path.Combine(nested.FullName, "general (ALT99).bat"), "test");

            var files = ZapretStrategyCatalog.DiscoverStrategyFiles(root)
                .Select(Path.GetFileName)
                .ToArray();

            Assert.Equal(3, files.Length);
            Assert.Contains("general.bat", files);
            Assert.Contains("general (ALT11).bat", files);
            Assert.Contains("general (SIMPLE FAKE).bat", files);
            Assert.DoesNotContain("general (ALT99).bat", files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

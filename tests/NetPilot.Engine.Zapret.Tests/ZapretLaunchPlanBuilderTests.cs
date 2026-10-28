using NetPilot.Engine.Zapret;

namespace NetPilot.Engine.Zapret.Tests;

public sealed class ZapretLaunchPlanBuilderTests
{
    [Fact]
    public void Builds_absolute_argument_list_inside_trusted_engine_root()
    {
        using var fixture = new EngineFixture();
        fixture.CreateBinFile("quic_initial.bin");
        fixture.CreateListFile("list-general.txt");

        const string source = """
            start "zapret" /min "%BIN%winws.exe" --wf-tcp=80,443,%GameFilterTCP% ^
            --wf-udp=443,%GameFilterUDP% --hostlist="%LISTS%list-general.txt" ^
            --dpi-desync-fake-quic="%BIN%quic_initial.bin"
            """;

        var strategy = ZapretStrategyParser.Parse("general (ALT11).bat", source);
        var plan = ZapretLaunchPlanBuilder.Build(
            strategy,
            fixture.Root,
            new ZapretRuntimeVariables("27015-27050,443", "50000-50100"));

        Assert.Equal(Path.GetFullPath(Path.Combine(fixture.Bin, "winws.exe")), plan.ExecutablePath);
        Assert.Equal(Path.GetFullPath(fixture.Bin), plan.WorkingDirectory);
        Assert.Equal(4, plan.ArgumentList.Count);
        Assert.Equal("--wf-tcp=80,443,27015-27050,443", plan.ArgumentList[0]);
        Assert.Equal("--wf-udp=443,50000-50100", plan.ArgumentList[1]);
        Assert.Equal($"--hostlist={Path.GetFullPath(Path.Combine(fixture.Lists, "list-general.txt"))}", plan.ArgumentList[2]);
        Assert.Equal($"--dpi-desync-fake-quic={Path.GetFullPath(Path.Combine(fixture.Bin, "quic_initial.bin"))}", plan.ArgumentList[3]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("500-100")]
    [InlineData("443,--new")]
    [InlineData("443 444")]
    public void Rejects_invalid_dynamic_game_filter(string invalidValue)
    {
        using var fixture = new EngineFixture();

        const string source = """
            start "zapret" /min "%BIN%winws.exe" --wf-tcp=80,%GameFilterTCP% --wf-udp=443,%GameFilterUDP%
            """;

        var strategy = ZapretStrategyParser.Parse("general.bat", source);

        Assert.Throws<ZapretStrategyParseException>(() =>
            ZapretLaunchPlanBuilder.Build(
                strategy,
                fixture.Root,
                new ZapretRuntimeVariables(invalidValue, "50000-50100")));
    }

    [Fact]
    public void Rejects_missing_referenced_payload()
    {
        using var fixture = new EngineFixture();

        const string source = """
            start "zapret" /min "%BIN%winws.exe" --dpi-desync-fake-quic="%BIN%missing.bin"
            """;

        var strategy = ZapretStrategyParser.Parse("general (ALT).bat", source);

        var exception = Assert.Throws<ZapretStrategyParseException>(() =>
            ZapretLaunchPlanBuilder.Build(
                strategy,
                fixture.Root,
                new ZapretRuntimeVariables("443", "443")));

        Assert.Equal("ReferencedFileMissing", exception.Code);
    }

    [Fact]
    public void Rejects_missing_winws_binary()
    {
        using var fixture = new EngineFixture(createExecutable: false);

        const string source = """
            start "zapret" /min "%BIN%winws.exe" --wf-tcp=443
            """;

        var strategy = ZapretStrategyParser.Parse("general.bat", source);

        var exception = Assert.Throws<ZapretStrategyParseException>(() =>
            ZapretLaunchPlanBuilder.Build(
                strategy,
                fixture.Root,
                new ZapretRuntimeVariables("443", "443")));

        Assert.Equal("WinwsMissing", exception.Code);
    }

    private sealed class EngineFixture : IDisposable
    {
        public EngineFixture(bool createExecutable = true)
        {
            Root = Path.Combine(Path.GetTempPath(), $"NetPilot-Zapret-Plan-{Guid.NewGuid():N}");
            Bin = Directory.CreateDirectory(Path.Combine(Root, "bin")).FullName;
            Lists = Directory.CreateDirectory(Path.Combine(Root, "lists")).FullName;

            if (createExecutable)
            {
                File.WriteAllBytes(Path.Combine(Bin, "winws.exe"), [0x4D, 0x5A]);
            }
        }

        public string Root { get; }
        public string Bin { get; }
        public string Lists { get; }

        public void CreateBinFile(string fileName) =>
            File.WriteAllBytes(Path.Combine(Bin, fileName), [1, 2, 3]);

        public void CreateListFile(string fileName) =>
            File.WriteAllText(Path.Combine(Lists, fileName), "example.org");

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

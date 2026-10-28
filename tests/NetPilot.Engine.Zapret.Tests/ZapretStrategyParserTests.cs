using NetPilot.Engine.Zapret;

namespace NetPilot.Engine.Zapret.Tests;

public sealed class ZapretStrategyParserTests
{
    [Fact]
    public void Parses_multiline_official_style_strategy_without_executing_preamble()
    {
        const string source = """
            @echo off
            call service.bat status_zapret
            call service.bat check_updates
            set "BIN=%~dp0bin\\"
            set "LISTS=%~dp0lists\\"
            start "zapret: %~n0" /min "%BIN%winws.exe" --wf-tcp=80,443,%GameFilterTCP% ^
            --filter-udp=443 --hostlist="%LISTS%list-general.txt" --dpi-desync=fake ^
            --dpi-desync-fake-quic="%BIN%quic_initial_www_google_com.bin" --new ^
            --filter-udp=%GameFilterUDP% --dpi-desync=fake
            """;

        var strategy = ZapretStrategyParser.Parse("general (ALT11).bat", source);

        Assert.Equal("ALT11", strategy.Id);
        Assert.Equal("general (ALT11).bat", strategy.SourceFileName);
        Assert.Equal(7, strategy.Arguments.Count);
        Assert.Contains("${GameFilterTcp}", strategy.Arguments[0].ToDisplayString());
        Assert.Contains("${Lists}", strategy.Arguments[2].ToDisplayString());
        Assert.Contains("${EngineBin}", strategy.Arguments[4].ToDisplayString());
        Assert.Contains("${GameFilterUdp}", strategy.Arguments[6].ToDisplayString());
    }

    [Fact]
    public void Parses_default_general_strategy()
    {
        const string source = """
            @echo off
            start "zapret" /min "%BIN%winws.exe" --wf-tcp=80,443 --dpi-desync=fake
            """;

        var strategy = ZapretStrategyParser.Parse("general.bat", source);

        Assert.Equal("DEFAULT", strategy.Id);
        Assert.Equal("Стандартная", strategy.DisplayName);
    }

    [Fact]
    public void Rejects_unknown_environment_variable()
    {
        const string source = """
            start "zapret" /min "%BIN%winws.exe" --wf-tcp=%UNTRUSTED_PORTS%
            """;

        var exception = Assert.Throws<ZapretStrategyParseException>(
            () => ZapretStrategyParser.Parse("general (ALT20).bat", source));

        Assert.Equal("UnknownVariable", exception.Code);
    }

    [Fact]
    public void Rejects_shell_metacharacter_in_winws_arguments()
    {
        const string source = """
            start "zapret" /min "%BIN%winws.exe" --wf-tcp=443 & unexpected-command
            """;

        var exception = Assert.Throws<ZapretStrategyParseException>(
            () => ZapretStrategyParser.Parse("general (ALT20).bat", source));

        Assert.Equal("ShellMetacharacterRejected", exception.Code);
    }

    [Fact]
    public void Rejects_executable_payload_as_argument_value()
    {
        const string source = """
            start "zapret" /min "%BIN%winws.exe" --hostlist=%LISTS%helper.exe
            """;

        var exception = Assert.Throws<ZapretStrategyParseException>(
            () => ZapretStrategyParser.Parse("general (ALT20).bat", source));

        Assert.Equal("ExecutablePayloadRejected", exception.Code);
    }

    [Fact]
    public void Rejects_path_traversal_after_trusted_placeholder()
    {
        const string source = """
            start "zapret" /min "%BIN%winws.exe" --hostlist="%LISTS%..\\private.txt"
            """;

        var exception = Assert.Throws<ZapretStrategyParseException>(
            () => ZapretStrategyParser.Parse("general (ALT20).bat", source));

        Assert.Equal("UnsafeRelativePath", exception.Code);
    }

    [Fact]
    public void Rejects_missing_winws_invocation()
    {
        const string source = """
            @echo off
            call service.bat status_zapret
            """;

        var exception = Assert.Throws<ZapretStrategyParseException>(
            () => ZapretStrategyParser.Parse("general (ALT20).bat", source));

        Assert.Equal("WinwsInvocationMissing", exception.Code);
    }

    [Fact]
    public void Rejects_multiple_winws_invocations()
    {
        const string source = """
            start "zapret1" /min "%BIN%winws.exe" --wf-tcp=443
            start "zapret2" /min "%BIN%winws.exe" --wf-tcp=80
            """;

        var exception = Assert.Throws<ZapretStrategyParseException>(
            () => ZapretStrategyParser.Parse("general (ALT20).bat", source));

        Assert.Equal("MultipleWinwsInvocations", exception.Code);
    }

    [Fact]
    public void Rejects_non_official_strategy_filename()
    {
        const string source = """
            start "zapret" /min "%BIN%winws.exe" --wf-tcp=443
            """;

        var exception = Assert.Throws<ZapretStrategyParseException>(
            () => ZapretStrategyParser.Parse("custom-script.bat", source));

        Assert.Equal("InvalidStrategyFileName", exception.Code);
    }
}

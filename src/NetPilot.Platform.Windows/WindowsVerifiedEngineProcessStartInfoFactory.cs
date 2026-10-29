using System.Diagnostics;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Platform.Windows;

public sealed class WindowsEngineProcessStartInfoException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// Создаёт только non-starting ProcessStartInfo из уже проверенной цепочки.
/// Никакой overload с executable/path/arguments strings нет. Process.Start
/// намеренно отсутствует.
/// </summary>
public static class WindowsVerifiedEngineProcessStartInfoFactory
{
    public static ProcessStartInfo Create(
        WindowsCompiledEngineArgumentPlan compiledArgumentPlan)
    {
        ArgumentNullException.ThrowIfNull(compiledArgumentPlan);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows engine ProcessStartInfo factory доступна только в Windows.");
        }

        compiledArgumentPlan.EnsureReady();

        var handoff = compiledArgumentPlan.PreLaunchHandoff;
        var binding = handoff.LaunchIdentityBinding;
        if (binding.Platform != EngineHostPlatform.Windows)
        {
            throw new WindowsEngineProcessStartInfoException(
                "ProcessPlatformMismatch",
                "ProcessStartInfo можно создать только для Windows launch binding.");
        }

        EnsureArgumentPlanMatchesHandoff(compiledArgumentPlan, handoff);

        var verifiedPackage = handoff.VerifiedPackage;
        var identityPrimary = binding.ExecutableIdentity.Components
            .Where(static component =>
                component.Kind == EngineExecutableComponentKind.PrimaryExecutable)
            .ToArray();
        if (identityPrimary.Length != 1)
        {
            throw new WindowsEngineProcessStartInfoException(
                "PrimaryIdentityComponentMismatch",
                "Immutable identity должна содержать ровно один PrimaryExecutable.");
        }

        var verifiedPrimary = verifiedPackage.Components
            .Where(static component =>
                component.Kind == EngineExecutableComponentKind.PrimaryExecutable)
            .ToArray();
        if (verifiedPrimary.Length != 1)
        {
            throw new WindowsEngineProcessStartInfoException(
                "PrimaryVerifiedComponentMismatch",
                "Verified package должен содержать ровно один PrimaryExecutable.");
        }

        var expectedPrimary = identityPrimary[0];
        var actualPrimary = verifiedPrimary[0];
        if (!string.Equals(
                expectedPrimary.ComponentId,
                actualPrimary.ComponentId,
                StringComparison.Ordinal)
            || !string.Equals(
                expectedPrimary.Sha256,
                actualPrimary.Sha256,
                StringComparison.Ordinal))
        {
            throw new WindowsEngineProcessStartInfoException(
                "PrimaryComponentIdentityMismatch",
                "Verified PrimaryExecutable не соответствует exact immutable identity.");
        }

        var engineRoot = NormalizeDirectory(verifiedPackage.EngineRoot);
        var executablePath = Path.GetFullPath(actualPrimary.FullPath);
        EnsureStrictChild(engineRoot, executablePath, "ExecutableOutsideEngineRoot");

        var workingDirectory = Path.GetDirectoryName(executablePath)
            ?? throw new WindowsEngineProcessStartInfoException(
                "WorkingDirectoryUnavailable",
                "Не удалось определить verified working directory PrimaryExecutable.");
        workingDirectory = NormalizeDirectory(workingDirectory);
        EnsureInsideOrEqual(engineRoot, workingDirectory, "WorkingDirectoryOutsideEngineRoot");

        if (!File.Exists(executablePath) || !Directory.Exists(workingDirectory))
        {
            throw new WindowsEngineProcessStartInfoException(
                "VerifiedLaunchPathMissing",
                "Verified executable или working directory исчезли до подготовки ProcessStartInfo.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            ErrorDialog = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        startInfo.Environment.Clear();
        AddMinimalSystemEnvironment(startInfo);

        foreach (var argument in compiledArgumentPlan.Arguments)
        {
            ValidateCompiledArgument(argument);
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(startInfo.Arguments))
        {
            throw new WindowsEngineProcessStartInfoException(
                "RawArgumentsUnexpected",
                "Raw command-line Arguments не должны использоваться; разрешён только ArgumentList.");
        }

        compiledArgumentPlan.EnsureReady();
        return startInfo;
    }

    private static void EnsureArgumentPlanMatchesHandoff(
        WindowsCompiledEngineArgumentPlan compiledPlan,
        WindowsVerifiedEnginePreLaunchHandoff handoff)
    {
        var actual = compiledPlan.Declaration.LaunchIdentityBinding;
        var expected = handoff.LaunchIdentityBinding;

        if (actual.Backend != expected.Backend
            || actual.Platform != expected.Platform
            || actual.Architecture != expected.Architecture
            || actual.ProfileId != expected.ProfileId
            || !ReferenceEquals(actual.ExecutableIdentity, expected.ExecutableIdentity))
        {
            throw new WindowsEngineProcessStartInfoException(
                "ProcessArgumentBindingMismatch",
                "Compiled argument plan не относится к exact verified pre-launch handoff.");
        }
    }

    private static void AddMinimalSystemEnvironment(ProcessStartInfo startInfo)
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory)
            || !Path.IsPathFullyQualified(windowsDirectory)
            || !Directory.Exists(windowsDirectory))
        {
            throw new WindowsEngineProcessStartInfoException(
                "WindowsDirectoryUnavailable",
                "Не удалось определить trusted Windows system directory для минимального environment.");
        }

        var normalized = NormalizeDirectory(windowsDirectory);
        startInfo.Environment["SystemRoot"] = normalized;
        startInfo.Environment["WINDIR"] = normalized;
    }

    private static void ValidateCompiledArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument)
            || argument.Length > 16 * 1024
            || argument.Any(static character => character is '\0' or '\r' or '\n'))
        {
            throw InvalidCompiledArgument();
        }

        // WindowsCompiledEngineArgumentPlan has no public constructor and can only
        // be emitted by WindowsTypedEngineArgumentPlanCompiler. This second gate
        // nevertheless accepts only the structural shapes that compiler can emit:
        // long option, exact one-letter short option, unsigned decimal, or the
        // fixed loopback literal. Arbitrary host/path/URL/raw text is rejected.
        var valid = argument.StartsWith("--", StringComparison.Ordinal)
            || IsShortOption(argument)
            || IsUnsignedDecimal(argument)
            || string.Equals(argument, "127.0.0.1", StringComparison.Ordinal);

        if (!valid)
        {
            throw InvalidCompiledArgument();
        }
    }

    private static bool IsShortOption(string value) =>
        value.Length == 2
        && value[0] == '-'
        && value[1] is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static bool IsUnsignedDecimal(string value) =>
        value.Length <= 20
        && value.All(static character => character is >= '0' and <= '9');

    private static WindowsEngineProcessStartInfoException InvalidCompiledArgument() =>
        new(
            "CompiledArgumentInvalid",
            "Compiled typed argument имеет недопустимый формат.");

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static void EnsureInsideOrEqual(
        string root,
        string candidate,
        string errorCode)
    {
        var normalizedRoot = NormalizeDirectory(root);
        var normalizedCandidate = NormalizeDirectory(candidate);

        if (!string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
            && !normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsEngineProcessStartInfoException(
                errorCode,
                "Prepared process path вышел за пределы exact verified engine root.");
        }
    }

    private static void EnsureStrictChild(
        string root,
        string candidate,
        string errorCode)
    {
        var normalizedRoot = NormalizeDirectory(root);
        var normalizedCandidate = Path.GetFullPath(candidate);

        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
            || !normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsEngineProcessStartInfoException(
                errorCode,
                "Prepared executable path не является strict child exact verified engine root.");
        }
    }
}

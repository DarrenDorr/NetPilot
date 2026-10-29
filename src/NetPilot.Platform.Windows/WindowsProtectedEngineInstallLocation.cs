using NetPilot.Engine.Abstractions;

namespace NetPilot.Platform.Windows;

/// <summary>
/// Разрешённый абсолютный путь одного immutable component внутри managed root.
/// Объект не является командой запуска.
/// </summary>
public sealed record WindowsResolvedEngineComponentPath(
    string ComponentId,
    string FullPath);

/// <summary>
/// Неисполняемый результат fixed-root resolution. Resolver ничего не создаёт
/// на диске и не проверяет runtime admission.
/// </summary>
public sealed class WindowsProtectedEngineInstallLayout
{
    internal WindowsProtectedEngineInstallLayout(
        string engineRoot,
        IReadOnlyList<WindowsResolvedEngineComponentPath> components)
    {
        EngineRoot = engineRoot;
        Components = components;
    }

    public string EngineRoot { get; }

    public IReadOnlyList<WindowsResolvedEngineComponentPath> Components { get; }
}

/// <summary>
/// Преобразует trusted binding + trusted package layout в фиксированное место
/// под Program Files/NetPilot/Engines/Managed. Public API не принимает base path,
/// filename, URL или произвольный filesystem location.
/// </summary>
public static class WindowsProtectedEngineInstallLocationResolver
{
    public static WindowsProtectedEngineInstallLayout Resolve(
        EngineLaunchIdentityBinding binding,
        EnginePackageLayoutDeclaration packageLayout)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(packageLayout);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows protected engine location доступен только в Windows.");
        }

        if (binding.Platform != EngineHostPlatform.Windows)
        {
            throw new InvalidOperationException(
                "Binding относится не к Windows platform boundary.");
        }

        if (!ReferenceEquals(
                packageLayout.ExecutableIdentity,
                binding.ExecutableIdentity))
        {
            throw new InvalidOperationException(
                "Package layout не относится к exact immutable identity launch binding.");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            throw new InvalidOperationException(
                "Не удалось определить защищённый системный каталог Program Files.");
        }

        var engineId = ValidatePathSegment(binding.EngineId, "EngineId");
        var version = ValidatePathSegment(binding.ExecutableIdentity.Version, "Version");
        var architecture = binding.Architecture switch
        {
            EngineBinaryArchitecture.X64 => "x64",
            EngineBinaryArchitecture.Arm64 => "arm64",
            _ => throw new InvalidOperationException(
                "Неизвестная architecture не может быть разрешена в protected install root.")
        };

        var managedRoot = Path.GetFullPath(
            Path.Combine(programFiles, "NetPilot", "Engines", "Managed"));
        var engineRoot = Path.GetFullPath(
            Path.Combine(managedRoot, engineId, version, architecture));

        EnsureInsideRoot(managedRoot, engineRoot);

        var resolved = new List<WindowsResolvedEngineComponentPath>(
            packageLayout.Components.Count);

        foreach (var component in packageLayout.Components)
        {
            var platformRelativePath = component.RelativePath
                .Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(
                Path.Combine(engineRoot, platformRelativePath));

            EnsureInsideRoot(engineRoot, fullPath);
            resolved.Add(
                new WindowsResolvedEngineComponentPath(
                    component.ComponentId,
                    fullPath));
        }

        return new WindowsProtectedEngineInstallLayout(
            engineRoot,
            Array.AsReadOnly(resolved.ToArray()));
    }

    private static string ValidatePathSegment(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.EndsWith(".", StringComparison.Ordinal)
            || IsWindowsReservedDeviceName(value)
            || value.Any(static character =>
                character is '/' or '\\' or ':'
                    or '<' or '>' or '"' or '|'
                    or '?' or '*'
                    || char.IsControl(character)))
        {
            throw new InvalidOperationException(
                $"{fieldName} не может использоваться как protected install path segment.");
        }

        return value;
    }

    private static bool IsWindowsReservedDeviceName(string value)
    {
        var dotIndex = value.IndexOf('.');
        var baseName = dotIndex >= 0 ? value[..dotIndex] : value;

        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return baseName.Length == 4
            && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && baseName[3] is >= '1' and <= '9';
    }

    private static void EnsureInsideRoot(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);

        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
            || !normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Resolved engine path вышел за пределы доверенного managed root.");
        }
    }
}

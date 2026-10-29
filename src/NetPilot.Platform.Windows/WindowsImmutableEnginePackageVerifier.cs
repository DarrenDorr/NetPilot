using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Platform.Windows;

public sealed class WindowsEnginePackageVerificationException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// Проверенный immutable component. Объект создаётся только verifier после
/// exact layout/path/type/hash проверки и не является разрешением запуска.
/// </summary>
public sealed class WindowsVerifiedEngineComponent
{
    internal WindowsVerifiedEngineComponent(
        string componentId,
        EngineExecutableComponentKind kind,
        string fullPath,
        string sha256)
    {
        ComponentId = componentId;
        Kind = kind;
        FullPath = fullPath;
        Sha256 = sha256;
    }

    public string ComponentId { get; }

    public EngineExecutableComponentKind Kind { get; }

    public string FullPath { get; }

    public string Sha256 { get; }
}

/// <summary>
/// Результат immutable on-disk verification. Пока объект не Dispose, verifier
/// удерживает read-only handles с FileShare.Read, чтобы новые write/delete opens
/// не могли подменить уже проверенные файлы между verification и будущим consumer.
/// Это не admission и не execution token.
/// </summary>
public sealed class WindowsVerifiedEnginePackage : IDisposable
{
    private FileStream[]? _verificationLocks;

    internal WindowsVerifiedEnginePackage(
        EngineExecutableIdentityDeclaration executableIdentity,
        EnginePackageLayoutDeclaration packageLayout,
        string engineRoot,
        IReadOnlyList<WindowsVerifiedEngineComponent> components,
        FileStream[] verificationLocks)
    {
        ExecutableIdentity = executableIdentity;
        PackageLayout = packageLayout;
        EngineRoot = engineRoot;
        Components = components;
        _verificationLocks = verificationLocks;
    }

    public EngineExecutableIdentityDeclaration ExecutableIdentity { get; }

    public EnginePackageLayoutDeclaration PackageLayout { get; }

    public string EngineRoot { get; }

    public IReadOnlyList<WindowsVerifiedEngineComponent> Components { get; }

    public bool IsVerificationLockHeld => _verificationLocks is not null;

    public void Dispose()
    {
        var locks = Interlocked.Exchange(ref _verificationLocks, null);
        if (locks is null)
        {
            return;
        }

        foreach (var stream in locks)
        {
            stream.Dispose();
        }
    }

    internal void EnsureVerificationLockHeld()
    {
        if (_verificationLocks is null)
        {
            throw new ObjectDisposedException(
                nameof(WindowsVerifiedEnginePackage),
                "Проверенный package больше не удерживает verification locks.");
        }
    }
}

/// <summary>
/// Проверяет exact managed package на диске, но ничего не запускает и не создаёт.
/// Public API принимает только trusted binding + trusted package layout; произвольный
/// base path, filename, URL, working directory или command line отсутствуют.
/// </summary>
public static class WindowsImmutableEnginePackageVerifier
{
    public static WindowsVerifiedEnginePackage Verify(
        EngineLaunchIdentityBinding binding,
        EnginePackageLayoutDeclaration packageLayout)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(packageLayout);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows immutable engine verification доступна только в Windows.");
        }

        if (!ReferenceEquals(packageLayout.ExecutableIdentity, binding.ExecutableIdentity))
        {
            throw new WindowsEnginePackageVerificationException(
                "PackageIdentityMismatch",
                "Package layout не относится к exact immutable identity launch binding.");
        }

        var resolved = WindowsProtectedEngineInstallLocationResolver.Resolve(binding, packageLayout);
        return VerifyResolvedLayout(binding.ExecutableIdentity, packageLayout, resolved);
    }

    // Internal test seam. Production callers должны использовать только Verify(binding, layout),
    // который сам выводит fixed Program Files root через protected resolver.
    internal static WindowsVerifiedEnginePackage VerifyResolvedLayout(
        EngineExecutableIdentityDeclaration executableIdentity,
        EnginePackageLayoutDeclaration packageLayout,
        WindowsProtectedEngineInstallLayout resolvedLayout)
    {
        ArgumentNullException.ThrowIfNull(executableIdentity);
        ArgumentNullException.ThrowIfNull(packageLayout);
        ArgumentNullException.ThrowIfNull(resolvedLayout);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows immutable engine verification доступна только в Windows.");
        }

        if (!ReferenceEquals(packageLayout.ExecutableIdentity, executableIdentity))
        {
            throw new WindowsEnginePackageVerificationException(
                "PackageIdentityMismatch",
                "Package layout не соответствует exact immutable executable identity.");
        }

        try
        {
            return VerifyResolvedLayoutCore(executableIdentity, packageLayout, resolvedLayout);
        }
        catch (WindowsEnginePackageVerificationException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new WindowsEnginePackageVerificationException(
                "PackageAccessDenied",
                "Windows отказала в безопасном чтении managed engine package.",
                exception);
        }
        catch (IOException exception)
        {
            throw new WindowsEnginePackageVerificationException(
                "PackageIoError",
                "Не удалось безопасно проверить managed engine package.",
                exception);
        }
        catch (CryptographicException exception)
        {
            throw new WindowsEnginePackageVerificationException(
                "PackageHashError",
                "Не удалось вычислить SHA-256 managed engine component.",
                exception);
        }
    }

    private static WindowsVerifiedEnginePackage VerifyResolvedLayoutCore(
        EngineExecutableIdentityDeclaration executableIdentity,
        EnginePackageLayoutDeclaration packageLayout,
        WindowsProtectedEngineInstallLayout resolvedLayout)
    {
        var engineRoot = Path.GetFullPath(resolvedLayout.EngineRoot);
        if (!Directory.Exists(engineRoot))
        {
            throw new WindowsEnginePackageVerificationException(
                "EngineRootMissing",
                "Managed engine root отсутствует.");
        }

        if (resolvedLayout.Components.Count != executableIdentity.Components.Count)
        {
            throw new WindowsEnginePackageVerificationException(
                "ResolvedComponentSetMismatch",
                "Resolved package содержит неверное число компонентов.");
        }

        var identityById = executableIdentity.Components.ToDictionary(
            static component => component.ComponentId,
            StringComparer.Ordinal);
        var layoutById = packageLayout.Components.ToDictionary(
            static component => component.ComponentId,
            StringComparer.Ordinal);

        var resolvedById = new Dictionary<string, WindowsResolvedEngineComponentPath>(
            StringComparer.Ordinal);
        foreach (var component in resolvedLayout.Components)
        {
            if (!resolvedById.TryAdd(component.ComponentId, component))
            {
                throw new WindowsEnginePackageVerificationException(
                    "DuplicateResolvedComponent",
                    "Resolved package содержит повторяющийся component id.");
            }
        }

        if (resolvedById.Count != identityById.Count
            || identityById.Keys.Any(id => !resolvedById.ContainsKey(id)))
        {
            throw new WindowsEnginePackageVerificationException(
                "ResolvedComponentSetMismatch",
                "Resolved package не соответствует exact immutable component set.");
        }

        var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var componentId in identityById.Keys)
        {
            var layoutComponent = layoutById[componentId];
            var resolvedComponent = resolvedById[componentId];
            var expectedPath = Path.GetFullPath(
                Path.Combine(
                    engineRoot,
                    layoutComponent.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            var actualPath = Path.GetFullPath(resolvedComponent.FullPath);

            EnsureStrictChild(engineRoot, expectedPath);
            EnsureStrictChild(engineRoot, actualPath);

            if (!string.Equals(expectedPath, actualPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new WindowsEnginePackageVerificationException(
                    "ResolvedPathMismatch",
                    "Resolved component path не соответствует trusted package layout.");
            }

            if (!expectedFiles.Add(actualPath))
            {
                throw new WindowsEnginePackageVerificationException(
                    "DuplicateResolvedPath",
                    "Несколько компонентов указывают на один filesystem path.");
            }
        }

        ValidateExactPackageTree(engineRoot, expectedFiles);

        var streams = new List<FileStream>(identityById.Count);
        var verified = new List<WindowsVerifiedEngineComponent>(identityById.Count);

        try
        {
            foreach (var identityComponent in executableIdentity.Components)
            {
                var resolvedComponent = resolvedById[identityComponent.ComponentId];
                var stream = OpenAndVerifyComponent(identityComponent, resolvedComponent.FullPath);
                streams.Add(stream);
                verified.Add(
                    new WindowsVerifiedEngineComponent(
                        identityComponent.ComponentId,
                        identityComponent.Kind,
                        Path.GetFullPath(resolvedComponent.FullPath),
                        identityComponent.Sha256));
            }

            // Повторный exact-tree проход выполняется уже при удерживаемых file handles.
            // Это fail-closed обнаруживает extra/substituted entries, появившиеся во время
            // component verification. ACL/ownership остаются отдельным future gate.
            ValidateExactPackageTree(engineRoot, expectedFiles);

            return new WindowsVerifiedEnginePackage(
                executableIdentity,
                packageLayout,
                engineRoot,
                Array.AsReadOnly(verified.ToArray()),
                streams.ToArray());
        }
        catch
        {
            foreach (var stream in streams)
            {
                stream.Dispose();
            }

            throw;
        }
    }

    private static FileStream OpenAndVerifyComponent(
        EngineExecutableComponentIdentity identity,
        string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new WindowsEnginePackageVerificationException(
                "ComponentMissing",
                "Ожидаемый immutable engine component отсутствует.");
        }

        var pathAttributes = File.GetAttributes(fullPath);
        if ((pathAttributes & FileAttributes.Directory) != 0)
        {
            throw new WindowsEnginePackageVerificationException(
                "ComponentNotRegularFile",
                "Immutable engine component не является обычным файлом.");
        }

        if ((pathAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new WindowsEnginePackageVerificationException(
                "UnsafeReparsePoint",
                "Reparse point или symbolic-link component запрещён.");
        }

        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        try
        {
            var before = GetFileInformation(stream.SafeFileHandle);
            ValidateFileInformation(before);

            var finalPath = GetFinalPath(stream.SafeFileHandle);
            if (!string.Equals(
                    Path.GetFullPath(finalPath),
                    fullPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new WindowsEnginePackageVerificationException(
                    "ComponentFinalPathMismatch",
                    "Открытый component разрешился не в ожидаемый exact filesystem path.");
            }

            var firstHash = ComputeSha256(stream);
            var afterFirstHash = GetFileInformation(stream.SafeFileHandle);
            if (!HasStableIdentity(before, afterFirstHash))
            {
                throw new WindowsEnginePackageVerificationException(
                    "ComponentChangedDuringVerification",
                    "Immutable component изменился во время verification.");
            }

            if (!string.Equals(firstHash, identity.Sha256, StringComparison.Ordinal))
            {
                throw new WindowsEnginePackageVerificationException(
                    "ComponentHashMismatch",
                    "SHA-256 immutable engine component не соответствует trusted identity.");
            }

            var secondHash = ComputeSha256(stream);
            var afterSecondHash = GetFileInformation(stream.SafeFileHandle);
            if (!HasStableIdentity(afterFirstHash, afterSecondHash)
                || !string.Equals(firstHash, secondHash, StringComparison.Ordinal))
            {
                throw new WindowsEnginePackageVerificationException(
                    "ComponentChangedDuringVerification",
                    "Immutable component изменился во время verification.");
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void ValidateExactPackageTree(
        string engineRoot,
        IReadOnlySet<string> expectedFiles)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(engineRoot));
        var expectedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            normalizedRoot
        };

        foreach (var file in expectedFiles)
        {
            var current = Path.GetDirectoryName(Path.GetFullPath(file));
            while (!string.IsNullOrEmpty(current))
            {
                var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
                if (string.Equals(normalized, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    expectedDirectories.Add(normalizedRoot);
                    break;
                }

                EnsureStrictChild(normalizedRoot, normalized);
                expectedDirectories.Add(normalized);
                current = Path.GetDirectoryName(normalized);
            }
        }

        var pending = new Stack<string>();
        pending.Push(normalizedRoot);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var directoryAttributes = File.GetAttributes(directory);

            if ((directoryAttributes & FileAttributes.Directory) == 0)
            {
                throw new WindowsEnginePackageVerificationException(
                    "PackageDirectoryTypeMismatch",
                    "Ожидаемый package directory не является каталогом.");
            }

            if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new WindowsEnginePackageVerificationException(
                    "UnsafeReparsePoint",
                    "Reparse point или junction внутри managed package запрещён.");
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var fullEntry = Path.GetFullPath(entry);
                EnsureStrictChild(normalizedRoot, fullEntry);

                var attributes = File.GetAttributes(fullEntry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new WindowsEnginePackageVerificationException(
                        "UnsafeReparsePoint",
                        "Reparse point или symbolic link внутри managed package запрещён.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    var normalizedDirectory = Path.TrimEndingDirectorySeparator(fullEntry);
                    if (!expectedDirectories.Contains(normalizedDirectory))
                    {
                        throw new WindowsEnginePackageVerificationException(
                            "UnexpectedPackageEntry",
                            "Managed engine package содержит неожиданный каталог.");
                    }

                    pending.Push(normalizedDirectory);
                    continue;
                }

                if (!expectedFiles.Contains(fullEntry))
                {
                    throw new WindowsEnginePackageVerificationException(
                        "UnexpectedPackageEntry",
                        "Managed engine package содержит неожиданный файл.");
                }
            }
        }
    }

    private static string ComputeSha256(FileStream stream)
    {
        stream.Position = 0;
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static NativeFileInformation GetFileInformation(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information))
        {
            var error = Marshal.GetLastWin32Error();
            throw new WindowsEnginePackageVerificationException(
                "ComponentHandleInspectionFailed",
                "Не удалось проверить Win32 identity открытого immutable component.",
                new Win32Exception(error));
        }

        return information;
    }

    private static void ValidateFileInformation(NativeFileInformation information)
    {
        var attributes = (FileAttributes)information.FileAttributes;
        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new WindowsEnginePackageVerificationException(
                "ComponentNotRegularFile",
                "Immutable engine component не является обычным файлом.");
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new WindowsEnginePackageVerificationException(
                "UnsafeReparsePoint",
                "Reparse point component запрещён.");
        }

        if (information.NumberOfLinks != 1)
        {
            throw new WindowsEnginePackageVerificationException(
                "ComponentHardlinkRejected",
                "Immutable engine component имеет несколько hardlink-имён и отклонён.");
        }
    }

    private static bool HasStableIdentity(
        NativeFileInformation left,
        NativeFileInformation right) =>
        left.VolumeSerialNumber == right.VolumeSerialNumber
        && left.FileIndexHigh == right.FileIndexHigh
        && left.FileIndexLow == right.FileIndexLow
        && left.FileSizeHigh == right.FileSizeHigh
        && left.FileSizeLow == right.FileSizeLow
        && left.LastWriteTimeHigh == right.LastWriteTimeHigh
        && left.LastWriteTimeLow == right.LastWriteTimeLow
        && left.NumberOfLinks == right.NumberOfLinks
        && left.FileAttributes == right.FileAttributes;

    private static string GetFinalPath(SafeFileHandle handle)
    {
        var capacity = 512;
        while (true)
        {
            var builder = new StringBuilder(capacity);
            var length = GetFinalPathNameByHandle(
                handle,
                builder,
                checked((uint)builder.Capacity),
                0);

            if (length == 0)
            {
                var error = Marshal.GetLastWin32Error();
                throw new WindowsEnginePackageVerificationException(
                    "ComponentFinalPathInspectionFailed",
                    "Не удалось получить final path открытого immutable component.",
                    new Win32Exception(error));
            }

            if (length < builder.Capacity)
            {
                return NormalizeFinalPath(builder.ToString());
            }

            capacity = checked((int)length + 1);
        }
    }

    private static string NormalizeFinalPath(string value)
    {
        const string uncPrefix = "\\\\?\\UNC\\";
        const string localPrefix = "\\\\?\\";

        if (value.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return "\\\\" + value[uncPrefix.Length..];
        }

        if (value.StartsWith(localPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return value[localPrefix.Length..];
        }

        return value;
    }

    private static void EnsureStrictChild(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);

        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
            || !normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsEnginePackageVerificationException(
                "ResolvedPathOutsideEngineRoot",
                "Engine component path вышел за пределы exact managed engine root.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out NativeFileInformation lpFileInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);
}

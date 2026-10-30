using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using NetPilot.Updater;

namespace NetPilot.ByeDpiR42PackageBuilder;

internal static class Program
{
    private const string PackageVersion = "0.17.3";
    private const string SourceRepository = "hufrea/byedpi";
    private const string SourceCommit = "7efde1b1296eaaa187b70e951894dde17527489c";
    private const string SourceTree = "8d13f6f5939190cd27ee68696247073a00b3e7dc";
    private const string LicenseBlob = "342905d38be83e9d540deae14987896826a74858";
    private const string ExpectedBinarySha256 = "866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd";
    private const string ExpectedLicenseSha256 = "18c854c6c25dffefe4b522cf1bdf5d15278a25ca785948258bddb94393f575eb";
    private const long ExpectedLicenseBytes = 1063;
    private const string Compiler = "x86_64-w64-mingw32-gcc";
    private const string LinkerFlags = "-Wl,--no-insert-timestamp -lws2_32 -lmswsock";

    private static readonly (string Name, string Sha256)[] Patches =
    [
        ("0001-loopback-only-listener.patch", "3e9a744830082d224592d730a816c4afcf9df15a2a6ab93870409ccf5c94c068"),
        ("0002-kavl-iterator-no-before-array-sentinel.patch", "6701c48ef00f81ca7fa95f98a1e6406f657fbdfc028371f568c7918106cd5fb7"),
        ("0003-pid-file-fail-safe-write.patch", "5d10702499581eeba2f632aa506b5d6c51ceb3e8e093bff25b8dcbc821bb3738"),
        ("0004-kavl-empty-erase-guard.patch", "fdc13219be9cb9d654ce7995df825decb6ee0f05e63d72481560977bbf3b3c4a"),
        ("0005-windows-socket-handle-narrowing-guard.patch", "7c8f4eee9ff2624346cb886027df66378773d0af04f5f84f54c30815104b4356"),
        ("0006-windows-wait-result-range.patch", "136b9fdf34b68a55f0f1a6ebf16e8b61c9b88e6d15eeb923594bdd7bf0fa2a3e"),
        ("0007-windows-aux-socket-probes.patch", "778f47d3ecf2d0f6c8ce0152766c0eb51e2d6e460923227bed49844ba18c0d25")
    ];

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: <ciadpi.exe> <LICENSE> <output-directory>");
            return 2;
        }

        try
        {
            var binaryPath = Path.GetFullPath(args[0]);
            var licensePath = Path.GetFullPath(args[1]);
            var outputRoot = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(outputRoot);
            if (Directory.EnumerateFileSystemEntries(outputRoot).Any())
            {
                throw new InvalidOperationException("R42.5 package output directory must be empty.");
            }

            var binary = await ReadExactFileAsync(
                binaryPath,
                UpdateSecurityLimits.MaxSingleExtractedFileBytes);
            var license = await ReadExactFileAsync(licensePath, 16 * 1024);

            RequireSha256(binary, ExpectedBinarySha256, "ByeDPI Windows binary");
            RequireSha256(license, ExpectedLicenseSha256, "ByeDPI LICENSE");
            if (license.LongLength != ExpectedLicenseBytes)
            {
                throw new InvalidDataException("ByeDPI LICENSE size drifted from the reviewed source tree.");
            }
            if (binary.Length < 2 || binary[0] != 0x4D || binary[1] != 0x5A)
            {
                throw new InvalidDataException("Reviewed ByeDPI Windows candidate is not a PE image.");
            }

            var manifest = new ByeDpiPackageManifest(
                ByeDpiPackageManifest.RequiredEngineId,
                PackageVersion,
                ByeDpiPackageManifest.RequiredArchitecture,
                ByeDpiPackageManifest.RequiredExecutableRelativePath,
                ByeDpiPackageManifest.RequiredLicenseRelativePath,
                new[]
                {
                    ManifestFile("bin/ciadpi.exe", binary),
                    ManifestFile("LICENSE", license)
                });

            var assetStem = $"NetPilot-ByeDPI-{PackageVersion}-x64";
            var zipPath = Path.Combine(outputRoot, assetStem + ".zip");
            var manifestPath = Path.Combine(outputRoot, assetStem + ".manifest.json");
            var provenancePath = Path.Combine(outputRoot, assetStem + ".provenance.json");

            WriteDeterministicZip(zipPath, binary, license);
            var manifestBytes = ByeDpiPackageManifestDocument.Serialize(manifest);
            await File.WriteAllBytesAsync(manifestPath, manifestBytes);

            var zipSha = ComputeSha256(await File.ReadAllBytesAsync(zipPath));
            var manifestSha = ComputeSha256(manifestBytes);
            var provenance = new ProvenanceDocument
            {
                SchemaVersion = 1,
                PackageVersion = PackageVersion,
                Architecture = "x64",
                SourceRepository = SourceRepository,
                SourceCommit = SourceCommit,
                SourceTree = SourceTree,
                LicenseBlob = LicenseBlob,
                Compiler = Compiler,
                LinkerFlags = LinkerFlags,
                ExpectedBinarySha256 = ExpectedBinarySha256,
                ExpectedLicenseSha256 = ExpectedLicenseSha256,
                PayloadCanonicalManifestSha256 = manifest.CanonicalManifestSha256,
                PackageZipSha256 = zipSha,
                ManifestDocumentSha256 = manifestSha,
                Patches = Patches.Select(static patch => new PatchDocument
                {
                    Name = patch.Name,
                    Sha256 = patch.Sha256
                }).ToArray()
            };

            var provenanceBytes = JsonSerializer.SerializeToUtf8Bytes(
                provenance,
                new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllBytesAsync(provenancePath, provenanceBytes);

            // Re-read every output before declaring success.
            var parsedManifest = ByeDpiPackageManifestDocument.Parse(
                await File.ReadAllBytesAsync(manifestPath));
            if (!string.Equals(
                    parsedManifest.CanonicalManifestSha256,
                    manifest.CanonicalManifestSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("R42.5 manifest round-trip drifted.");
            }
            VerifyZipExact(zipPath, manifest);

            Console.WriteLine("R42_5_PACKAGE_BUILDER=PASS");
            Console.WriteLine("PACKAGE_VERSION=" + PackageVersion);
            Console.WriteLine("BINARY_SHA256=" + ExpectedBinarySha256);
            Console.WriteLine("LICENSE_SHA256=" + ExpectedLicenseSha256);
            Console.WriteLine("CANONICAL_MANIFEST_SHA256=" + manifest.CanonicalManifestSha256);
            Console.WriteLine("PACKAGE_ZIP_SHA256=" + zipSha);
            Console.WriteLine("MANIFEST_DOCUMENT_SHA256=" + manifestSha);
            Console.WriteLine("NATIVE_EXECUTION_TESTED=False");
            Console.WriteLine("PRODUCTION_ADMISSION=BLOCK");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.GetType().Name + ": " + exception.Message);
            return 1;
        }
    }

    private static async Task<byte[]> ReadExactFileAsync(string path, long maxBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 || info.Length > maxBytes)
        {
            throw new InvalidDataException("R42.5 input file size is outside the fixed boundary.");
        }
        return await File.ReadAllBytesAsync(path);
    }

    private static ByeDpiPackageFileManifest ManifestFile(string path, byte[] bytes) =>
        new(path, bytes.LongLength, ComputeSha256(bytes));

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void RequireSha256(byte[] bytes, string expected, string label)
    {
        var actual = ComputeSha256(bytes);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{label} SHA-256 drifted from the reviewed identity.");
        }
    }

    private static void WriteDeterministicZip(
        string zipPath,
        byte[] binary,
        byte[] license)
    {
        var fixedTimestamp = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        WriteEntry(archive, "LICENSE", license, fixedTimestamp);
        WriteEntry(archive, "bin/ciadpi.exe", binary, fixedTimestamp);
    }

    private static void WriteEntry(
        ZipArchive archive,
        string name,
        byte[] bytes,
        DateTimeOffset timestamp)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = timestamp;
        using var target = entry.Open();
        target.Write(bytes);
    }

    private static void VerifyZipExact(string zipPath, ByeDpiPackageManifest manifest)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries
            .OrderBy(static entry => entry.FullName, StringComparer.Ordinal)
            .ToArray();
        if (entries.Length != manifest.Files.Count)
        {
            throw new InvalidDataException("R42.5 ZIP file count does not match the canonical manifest.");
        }

        foreach (var expected in manifest.Files)
        {
            var entry = entries.SingleOrDefault(candidate =>
                string.Equals(candidate.FullName, expected.RelativePath, StringComparison.Ordinal));
            if (entry is null || entry.Length != expected.SizeBytes)
            {
                throw new InvalidDataException("R42.5 ZIP entry set/size does not match the canonical manifest.");
            }
            using var source = entry.Open();
            var digest = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
            if (!string.Equals(digest, expected.Sha256Hex, StringComparison.Ordinal))
            {
                throw new InvalidDataException("R42.5 ZIP entry SHA-256 does not match the canonical manifest.");
            }
        }
    }

    private sealed class ProvenanceDocument
    {
        public int SchemaVersion { get; set; }
        public string PackageVersion { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public string SourceRepository { get; set; } = string.Empty;
        public string SourceCommit { get; set; } = string.Empty;
        public string SourceTree { get; set; } = string.Empty;
        public string LicenseBlob { get; set; } = string.Empty;
        public string Compiler { get; set; } = string.Empty;
        public string LinkerFlags { get; set; } = string.Empty;
        public string ExpectedBinarySha256 { get; set; } = string.Empty;
        public string ExpectedLicenseSha256 { get; set; } = string.Empty;
        public string PayloadCanonicalManifestSha256 { get; set; } = string.Empty;
        public string PackageZipSha256 { get; set; } = string.Empty;
        public string ManifestDocumentSha256 { get; set; } = string.Empty;
        public PatchDocument[] Patches { get; set; } = [];
    }

    private sealed class PatchDocument
    {
        public string Name { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
    }
}

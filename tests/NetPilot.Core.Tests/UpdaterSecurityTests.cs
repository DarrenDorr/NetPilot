using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class UpdaterSecurityTests
{
    [Fact]
    public void Official_flowseal_zip_asset_is_accepted()
    {
        var asset = new ReleaseAsset(
            "1.10.4",
            "zapret-discord-youtube-1.10.4.zip",
            new Uri("https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.10.4/zapret-discord-youtube-1.10.4.zip"),
            1_518_649,
            "sha256:291af673bd0238506f9548e0e94ca0abf244000f4c9a06960544fb01c5f97b34");

        Assert.Equal("1.10.4", asset.Version);
        Assert.Equal("291af673bd0238506f9548e0e94ca0abf244000f4c9a06960544fb01c5f97b34", asset.Sha256Hex);
    }

    [Theory]
    [InlineData("https://evil.example/zapret.zip")]
    [InlineData("http://github.com/Flowseal/zapret-discord-youtube/releases/download/1.10.4/zapret-discord-youtube-1.10.4.zip")]
    [InlineData("https://github.com/Other/zapret-discord-youtube/releases/download/1.10.4/zapret-discord-youtube-1.10.4.zip")]
    [InlineData("https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.10.3/zapret-discord-youtube-1.10.4.zip")]
    public void Untrusted_asset_uri_is_rejected(string uri)
    {
        Assert.Throws<ArgumentException>(() => new ReleaseAsset(
            "1.10.4",
            "zapret-discord-youtube-1.10.4.zip",
            new Uri(uri),
            1_000,
            new string('a', 64)));
    }

    [Fact]
    public async Task Sha256_verifier_accepts_matching_content()
    {
        var bytes = Encoding.UTF8.GetBytes("NetPilot update test");
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await using var stream = new MemoryStream(bytes);

        await Sha256Verifier.VerifyAsync(
            stream,
            digest,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Sha256_verifier_rejects_mismatch()
    {
        var bytes = Encoding.UTF8.GetBytes("NetPilot update test");
        await using var stream = new MemoryStream(bytes);

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => Sha256Verifier.VerifyAsync(
                stream,
                new string('0', 64),
                TestContext.Current.CancellationToken));

        Assert.Equal("DigestMismatch", exception.Code);
    }

    [Fact]
    public async Task Safe_zip_extracts_normal_file_inside_staging()
    {
        await using var zip = CreateZip(("bin/winws.exe", "safe"));
        using var staging = new TemporaryDirectory();

        await SafeZipExtractor.ExtractAsync(
            zip,
            staging.Path,
            TestContext.Current.CancellationToken);

        Assert.Equal("safe", File.ReadAllText(System.IO.Path.Combine(staging.Path, "bin", "winws.exe")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("folder/../../escape.txt")]
    [InlineData("C:/Windows/System32/escape.txt")]
    [InlineData("/absolute/escape.txt")]
    public async Task Safe_zip_rejects_path_escape(string entryName)
    {
        await using var zip = CreateZip((entryName, "bad"));
        using var staging = new TemporaryDirectory();

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => SafeZipExtractor.ExtractAsync(
                zip,
                staging.Path,
                TestContext.Current.CancellationToken));

        Assert.Contains(exception.Code, new[] { "ArchiveTraversal", "InvalidArchivePath" });
    }

    [Fact]
    public async Task Safe_zip_rejects_case_insensitive_duplicate_destination()
    {
        await using var zip = CreateZip(
            ("bin/winws.exe", "one"),
            ("BIN/WINWS.EXE", "two"));
        using var staging = new TemporaryDirectory();

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => SafeZipExtractor.ExtractAsync(
                zip,
                staging.Path,
                TestContext.Current.CancellationToken));

        Assert.Equal("DuplicateArchivePath", exception.Code);
    }

    [Fact]
    public async Task Safe_zip_requires_empty_staging_directory()
    {
        await using var zip = CreateZip(("bin/winws.exe", "safe"));
        using var staging = new TemporaryDirectory();
        File.WriteAllText(System.IO.Path.Combine(staging.Path, "existing.txt"), "existing");

        var exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => SafeZipExtractor.ExtractAsync(
                zip,
                staging.Path,
                TestContext.Current.CancellationToken));

        Assert.Equal("StagingNotEmpty", exception.Code);
    }

    private static MemoryStream CreateZip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "NetPilotTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

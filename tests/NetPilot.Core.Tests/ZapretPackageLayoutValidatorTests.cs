using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ZapretPackageLayoutValidatorTests
{
    [Fact]
    public void Direct_expected_layout_is_accepted()
    {
        using var fixture = new PackageFixture();
        fixture.CreateValidLayout(fixture.Root);

        var resolved = ZapretPackageLayoutValidator.ResolvePackageRoot(fixture.Root);

        Assert.Equal(Path.GetFullPath(fixture.Root), resolved);
    }

    [Fact]
    public void Single_wrapper_directory_with_expected_layout_is_accepted()
    {
        using var fixture = new PackageFixture();
        var wrapped = Path.Combine(fixture.Root, "zapret-discord-youtube-1.10.4");
        Directory.CreateDirectory(wrapped);
        fixture.CreateValidLayout(wrapped);

        var resolved = ZapretPackageLayoutValidator.ResolvePackageRoot(fixture.Root);

        Assert.Equal(Path.GetFullPath(wrapped), resolved);
    }

    [Fact]
    public void Missing_winws_is_rejected()
    {
        using var fixture = new PackageFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "lists"));
        File.WriteAllText(Path.Combine(fixture.Root, "general (ALT11).bat"), "@echo off");

        var exception = Assert.Throws<UpdateSecurityException>(
            () => ZapretPackageLayoutValidator.ResolvePackageRoot(fixture.Root));

        Assert.Equal("InvalidPackageLayout", exception.Code);
    }

    [Fact]
    public void Ambiguous_multiple_package_roots_are_rejected()
    {
        using var fixture = new PackageFixture();
        var first = Path.Combine(fixture.Root, "one");
        var second = Path.Combine(fixture.Root, "two");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        fixture.CreateValidLayout(first);
        fixture.CreateValidLayout(second);

        var exception = Assert.Throws<UpdateSecurityException>(
            () => ZapretPackageLayoutValidator.ResolvePackageRoot(fixture.Root));

        Assert.Equal("InvalidPackageLayout", exception.Code);
    }

    private sealed class PackageFixture : IDisposable
    {
        public PackageFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "NetPilotTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void CreateValidLayout(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            Directory.CreateDirectory(Path.Combine(root, "lists"));
            File.WriteAllBytes(Path.Combine(root, "bin", "winws.exe"), [0x4D, 0x5A]);
            File.WriteAllText(Path.Combine(root, "general (ALT11).bat"), "@echo off");
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
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

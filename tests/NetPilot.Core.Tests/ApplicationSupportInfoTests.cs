using System.Reflection;
using NetPilot.App.Services;

namespace NetPilot.Core.Tests;

public sealed class ApplicationSupportInfoTests
{
    [Fact]
    public void KeepsOnlyApprovedPublicFields()
    {
        var propertyNames = typeof(ApplicationSupportInfo)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "Architecture", "Platform", "Version" },
            propertyNames);
    }

    [Fact]
    public void NormalizesControlCharactersAndBoundsValues()
    {
        var longVersion = new string('v', 200) + "\r\nsecret";
        var info = new ApplicationSupportInfo(
            longVersion,
            "Windows\r\nDesktop",
            "x64\tprocess");

        Assert.Equal(128, info.Version.Length);
        Assert.DoesNotContain('\r', info.Version);
        Assert.DoesNotContain('\n', info.Version);
        Assert.DoesNotContain('\r', info.Platform);
        Assert.DoesNotContain('\n', info.Platform);
        Assert.DoesNotContain('\t', info.Architecture);
    }

    [Fact]
    public void EmptyValuesBecomeUnknown()
    {
        var info = new ApplicationSupportInfo(" ", null, "\r\n");

        Assert.Equal(ApplicationSupportInfo.UnknownValue, info.Version);
        Assert.Equal(ApplicationSupportInfo.UnknownValue, info.Platform);
        Assert.Equal(ApplicationSupportInfo.UnknownValue, info.Architecture);
    }
}

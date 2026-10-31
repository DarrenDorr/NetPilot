using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ByeDpiIsolatedHttpTransportTests
{
    private static readonly byte[] PayloadBytes = Encoding.UTF8.GetBytes("payload-r42.9");
    private static readonly byte[] ManifestBytes = Encoding.UTF8.GetBytes("manifest-r42.9");
    private static readonly byte[] ProvenanceBytes = Encoding.UTF8.GetBytes("provenance-r42.9");

    [Fact]
    public async Task MetadataUsesExactTagPinnedHeadersAndNoAuthorization()
    {
        var descriptor = CreateDescriptor();
        var json = CreateValidMetadataJson(descriptor);
        HttpRequestMessage? observed = null;

        using var handler = new StubHandler((request, _) =>
        {
            observed = CloneRequestHeaders(request);
            return Task.FromResult(JsonResponse(request, json));
        });
        using var transport = new ByeDpiIsolatedHttpTransport(handler);

        var metadata = await transport.FetchReleaseMetadataAsync(
            descriptor,
            TestContext.Current.CancellationToken);

        Assert.Equal("byedpi-v0.17.3", metadata.Tag);
        Assert.NotNull(observed);
        Assert.Equal(
            "https://api.github.com/repos/DarrenDorr/NetPilot/releases/tags/byedpi-v0.17.3",
            observed.RequestUri!.AbsoluteUri);
        Assert.Equal(ByeDpiIsolatedHttpTransport.UserAgent, observed.Headers.UserAgent.ToString());
        Assert.Equal(
            ByeDpiAcquisitionTrustPolicy.ReleaseMetadataAccept,
            Assert.Single(observed.Headers.Accept).MediaType);
        Assert.Equal(
            ByeDpiAcquisitionTrustPolicy.GithubApiVersion,
            Assert.Single(observed.Headers.GetValues("X-GitHub-Api-Version")));
        Assert.Null(observed.Headers.Authorization);
        Assert.False(observed.Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task DirectAssetSuccessIsExactLengthAndHashVerifiedInMemory()
    {
        var plan = CreatePlan();
        var asset = plan.Assets[0];

        using var handler = new StubHandler((request, _) =>
            Task.FromResult(BinaryResponse(request, HttpStatusCode.OK, PayloadBytes, includeLength: true)));
        using var transport = new ByeDpiIsolatedHttpTransport(handler);

        var bytes = await transport.FetchVerifiedAssetAsync(
            plan,
            asset,
            TestContext.Current.CancellationToken);

        Assert.Equal(PayloadBytes, bytes);
    }

    [Fact]
    public async Task SingleTrustedRedirectSucceedsWithoutForwardingApiOrSensitiveHeaders()
    {
        var plan = CreatePlan();
        var asset = plan.Assets[0];
        var redirect = new Uri(
            "https://release-assets.githubusercontent.com/github-production-release-asset/12345/payload.zip?sig=r42-9");
        var call = 0;

        using var handler = new StubHandler((request, _) =>
        {
            call++;
            if (call == 1)
            {
                Assert.Equal(asset.AssetApiUri, request.RequestUri);
                Assert.Equal(
                    ByeDpiAcquisitionTrustPolicy.ReleaseAssetAccept,
                    Assert.Single(request.Headers.Accept).MediaType);
                Assert.Equal(
                    ByeDpiAcquisitionTrustPolicy.GithubApiVersion,
                    Assert.Single(request.Headers.GetValues("X-GitHub-Api-Version")));
                Assert.Null(request.Headers.Authorization);
                return Task.FromResult(RedirectResponse(request, redirect));
            }

            Assert.Equal(redirect, request.RequestUri);
            Assert.Empty(request.Headers.Accept);
            Assert.False(request.Headers.Contains("X-GitHub-Api-Version"));
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Equal(ByeDpiIsolatedHttpTransport.UserAgent, request.Headers.UserAgent.ToString());
            return Task.FromResult(BinaryResponse(request, HttpStatusCode.OK, PayloadBytes, includeLength: true));
        });
        using var transport = new ByeDpiIsolatedHttpTransport(handler);

        var bytes = await transport.FetchVerifiedAssetAsync(
            plan,
            asset,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, call);
        Assert.Equal(PayloadBytes, bytes);
    }

    [Fact]
    public async Task UntrustedRedirectIsRejectedBeforeSecondRequest()
    {
        var plan = CreatePlan();
        var call = 0;

        using var handler = new StubHandler((request, _) =>
        {
            call++;
            return Task.FromResult(RedirectResponse(request, new Uri("https://example.com/payload.zip")));
        });
        using var transport = new ByeDpiIsolatedHttpTransport(handler);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            transport.FetchVerifiedAssetAsync(plan, plan.Assets[0], TestContext.Current.CancellationToken));

        Assert.Equal(1, call);
    }

    [Fact]
    public async Task SecondRedirectIsRejected()
    {
        var plan = CreatePlan();
        var firstRedirect = new Uri(
            "https://release-assets.githubusercontent.com/github-production-release-asset/12345/payload.zip?sig=one");
        var secondRedirect = new Uri(
            "https://release-assets.githubusercontent.com/github-production-release-asset/12345/payload2.zip?sig=two");
        var call = 0;

        using var handler = new StubHandler((request, _) =>
        {
            call++;
            return Task.FromResult(call == 1
                ? RedirectResponse(request, firstRedirect)
                : RedirectResponse(request, secondRedirect));
        });
        using var transport = new ByeDpiIsolatedHttpTransport(handler);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            transport.FetchVerifiedAssetAsync(plan, plan.Assets[0], TestContext.Current.CancellationToken));

        Assert.Equal(2, call);
    }

    [Fact]
    public async Task RedirectWithContentLengthIsRejected()
    {
        var plan = CreatePlan();
        var redirect = new Uri(
            "https://release-assets.githubusercontent.com/github-production-release-asset/12345/payload.zip?sig=length");

        using var handler = new StubHandler((request, _) =>
        {
            var response = RedirectResponse(request, redirect);
            response.Content = new ByteArrayContent([1]);
            response.Content.Headers.ContentLength = 1;
            return Task.FromResult(response);
        });
        using var transport = new ByeDpiIsolatedHttpTransport(handler);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            transport.FetchVerifiedAssetAsync(plan, plan.Assets[0], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WrongContentLengthIsRejectedBeforeBodyTrust()
    {
        var plan = CreatePlan();
        var asset = plan.Assets[0];

        using var handler = new StubHandler((request, _) =>
        {
            var response = BinaryResponse(request, HttpStatusCode.OK, PayloadBytes, includeLength: true);
            response.Content.Headers.ContentLength = asset.SizeBytes + 1;
            return Task.FromResult(response);
        });
        using var transport = new ByeDpiIsolatedHttpTransport(handler);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            transport.FetchVerifiedAssetAsync(plan, asset, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChunkedAssetOverrunAndUnderrunAreRejected()
    {
        var plan = CreatePlan();
        var asset = plan.Assets[0];
        var over = PayloadBytes.Concat([0x42]).ToArray();
        var under = PayloadBytes[..^1];
        var current = over;

        using var handler = new StubHandler((request, _) =>
            Task.FromResult(BinaryResponse(request, HttpStatusCode.OK, current, includeLength: false)));
        using var transport = new ByeDpiIsolatedHttpTransport(handler);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            transport.FetchVerifiedAssetAsync(plan, asset, TestContext.Current.CancellationToken));

        current = under;

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            transport.FetchVerifiedAssetAsync(plan, asset, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExactLengthWithWrongSha256IsRejected()
    {
        var plan = CreatePlan();
        var asset = plan.Assets[0];
        var wrong = PayloadBytes.ToArray();
        wrong[0] ^= 0x20;

        using var handler = new StubHandler((request, _) =>
            Task.FromResult(BinaryResponse(request, HttpStatusCode.OK, wrong, includeLength: false)));
        using var transport = new ByeDpiIsolatedHttpTransport(handler);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            transport.FetchVerifiedAssetAsync(plan, asset, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChunkedMetadataOverFixedCapIsRejected()
    {
        var descriptor = CreateDescriptor();
        var oversized = Enumerable.Repeat((byte)' ', ByeDpiAcquisitionTrustPolicy.MaxReleaseMetadataBytes + 1).ToArray();

        using var handler = new StubHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = StreamBody(oversized, "application/json")
            };
            return Task.FromResult(response);
        });
        using var transport = new ByeDpiIsolatedHttpTransport(handler);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            transport.FetchReleaseMetadataAsync(descriptor, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InternalTimeoutIsTypedAndCallerCancellationRemainsCancellation()
    {
        var descriptor = CreateDescriptor();
        using var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        });
        using var transport = new ByeDpiIsolatedHttpTransport(
            handler,
            TimeSpan.FromMilliseconds(30));

        var timeout = await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            transport.FetchReleaseMetadataAsync(descriptor));
        Assert.Equal("ByeDpiHttpTransportTimeout", timeout.Code);

        using var caller = new CancellationTokenSource();
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transport.FetchReleaseMetadataAsync(descriptor, caller.Token));
    }

    [Fact]
    public async Task HiddenAutomaticRedirectIsRejected()
    {
        var descriptor = CreateDescriptor();
        var json = CreateValidMetadataJson(descriptor);

        using var handler = new StubHandler((request, _) =>
        {
            var response = JsonResponse(request, json);
            response.RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                "https://release-assets.githubusercontent.com/hidden/final.json");
            return Task.FromResult(response);
        });
        using var transport = new ByeDpiIsolatedHttpTransport(handler);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            transport.FetchReleaseMetadataAsync(descriptor, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TimeoutCannotExceedFixedR427Limit()
    {
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException());

        Assert.Throws<UpdateSecurityException>(() =>
            new ByeDpiIsolatedHttpTransport(
                handler,
                TimeSpan.FromSeconds(ByeDpiAcquisitionTrustPolicy.NetworkTimeoutSeconds + 1)));
    }

    private static ByeDpiAcquisitionPlan CreatePlan()
    {
        var descriptor = CreateDescriptor();
        var metadata = GithubByeDpiReleaseParser.Parse(
            Encoding.UTF8.GetBytes(CreateValidMetadataJson(descriptor)));
        return ByeDpiAcquisitionTrustPolicy.CreatePlan(descriptor, metadata, "0.17.3");
    }

    private static ByeDpiImmutableReleaseDescriptor CreateDescriptor() =>
        new(
            "byedpi",
            "0.17.3",
            "x64",
            "DarrenDorr",
            "NetPilot",
            "byedpi-v0.17.3",
            "7efde1b1296eaaa187b70e951894dde17527489c",
            "8d13f6f5939190cd27ee68696247073a00b3e7dc",
            new ByeDpiImmutableReleaseAsset(
                "NetPilot-ByeDPI-0.17.3-x64.zip",
                PayloadBytes.Length,
                Sha256Hex(PayloadBytes)),
            new ByeDpiImmutableReleaseAsset(
                "NetPilot-ByeDPI-0.17.3-x64.manifest.json",
                ManifestBytes.Length,
                Sha256Hex(ManifestBytes)),
            new ByeDpiImmutableReleaseAsset(
                "NetPilot-ByeDPI-0.17.3-x64.provenance.json",
                ProvenanceBytes.Length,
                Sha256Hex(ProvenanceBytes)));

    private static string CreateValidMetadataJson(ByeDpiImmutableReleaseDescriptor descriptor) =>
        "{" +
        "\"id\":5001," +
        $"\"tag_name\":\"{descriptor.ReleaseTag}\"," +
        "\"draft\":false," +
        "\"prerelease\":false," +
        "\"immutable\":true," +
        "\"assets\":[" +
        AssetJson(1001, descriptor.Payload) + "," +
        AssetJson(1002, descriptor.Manifest) + "," +
        AssetJson(1003, descriptor.Provenance) +
        "]}";

    private static string AssetJson(long id, ByeDpiImmutableReleaseAsset asset) =>
        "{" +
        $"\"url\":\"https://api.github.com/repos/DarrenDorr/NetPilot/releases/assets/{id}\"," +
        $"\"id\":{id}," +
        $"\"name\":\"{asset.Name}\"," +
        "\"state\":\"uploaded\"," +
        $"\"size\":{asset.SizeBytes}," +
        $"\"digest\":\"sha256:{asset.Sha256Hex}\"" +
        "}";

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static HttpResponseMessage JsonResponse(HttpRequestMessage request, string json)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = content
        };
    }

    private static HttpResponseMessage BinaryResponse(
        HttpRequestMessage request,
        HttpStatusCode status,
        byte[] bytes,
        bool includeLength)
    {
        HttpContent content = includeLength
            ? new ByteArrayContent(bytes)
            : StreamBody(bytes, "application/octet-stream");

        if (includeLength)
        {
            content.Headers.ContentLength = bytes.Length;
        }

        return new HttpResponseMessage(status)
        {
            RequestMessage = request,
            Content = content
        };
    }

    private static HttpResponseMessage RedirectResponse(HttpRequestMessage request, Uri redirect) =>
        new(HttpStatusCode.Found)
        {
            RequestMessage = request,
            Content = StreamBody([], null),
            Headers = { Location = redirect }
        };

    private static HttpContent StreamBody(byte[] bytes, string? mediaType)
    {
        var content = new StreamContent(new MemoryStream(bytes, writable: false));
        content.Headers.ContentLength = null;
        if (mediaType is not null)
        {
            content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        }
        return content;
    }

    private static HttpRequestMessage CloneRequestHeaders(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            callback(request, cancellationToken);
    }
}

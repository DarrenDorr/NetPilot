using System.Text.Json;

namespace NetPilot.Updater;

public sealed record GithubZapretRelease(
    long ReleaseId,
    string Version,
    Uri ReleasePageUri,
    long AssetId,
    ReleaseAsset Asset);

public static class GithubZapretReleaseParser
{
    public const string LatestReleaseApiUrl =
        "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32
    };

    public static GithubZapretRelease Parse(ReadOnlyMemory<byte> json)
    {
        if (json.IsEmpty || json.Length > UpdateNetworkLimits.MaxReleaseMetadataBytes)
        {
            throw new UpdateSecurityException(
                "ReleaseMetadataSize",
                "Размер metadata релиза выходит за безопасный лимит.");
        }

        try
        {
            using var document = JsonDocument.Parse(json, JsonOptions);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw InvalidMetadata("Корень metadata релиза должен быть JSON-объектом.");
            }

            var releaseId = GetPositiveInt64(root, "id");
            var version = GetRequiredString(root, "tag_name", 64);
            var releasePage = GetRequiredUri(root, "html_url");

            if (GetRequiredBoolean(root, "draft")
                || GetRequiredBoolean(root, "prerelease")
                || !GetRequiredBoolean(root, "immutable"))
            {
                throw new UpdateSecurityException(
                    "UntrustedReleaseState",
                    "NetPilot принимает только опубликованный стабильный immutable-релиз Flowseal.");
            }

            ValidateReleasePage(releasePage, version);

            if (!root.TryGetProperty("assets", out var assets)
                || assets.ValueKind != JsonValueKind.Array
                || assets.GetArrayLength() is <= 0 or > 64)
            {
                throw InvalidMetadata("Список release assets отсутствует или имеет недопустимый размер.");
            }

            var expectedName = $"zapret-discord-youtube-{version}.zip";
            GithubZapretRelease? selected = null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object)
                {
                    throw InvalidMetadata("Release asset имеет недопустимый формат.");
                }

                var name = GetRequiredString(asset, "name", 160);
                if (!string.Equals(name, expectedName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (selected is not null)
                {
                    throw new UpdateSecurityException(
                        "AmbiguousReleaseAsset",
                        "Релиз содержит несколько ZIP assets с ожидаемым именем.");
                }

                var state = GetRequiredString(asset, "state", 32);
                var contentType = GetRequiredString(asset, "content_type", 64);
                if (!string.Equals(state, "uploaded", StringComparison.Ordinal)
                    || !string.Equals(contentType, "application/zip", StringComparison.OrdinalIgnoreCase))
                {
                    throw new UpdateSecurityException(
                        "UntrustedReleaseAssetState",
                        "ZIP asset не находится в ожидаемом опубликованном состоянии.");
                }

                var assetId = GetPositiveInt64(asset, "id");
                var size = GetPositiveInt64(asset, "size");
                var digest = GetRequiredString(asset, "digest", 96);
                var browserDownloadUri = GetRequiredUri(asset, "browser_download_url");
                var assetApiUri = GetRequiredUri(asset, "url");

                ValidateAssetApiUri(assetApiUri, assetId);

                var releaseAsset = new ReleaseAsset(
                    version,
                    name,
                    browserDownloadUri,
                    size,
                    digest);

                selected = new GithubZapretRelease(
                    releaseId,
                    version,
                    releasePage,
                    assetId,
                    releaseAsset);
            }

            return selected ?? throw new UpdateSecurityException(
                "ReleaseZipAssetMissing",
                "В официальном релизе отсутствует ожидаемый ZIP asset с SHA-256 digest.");
        }
        catch (JsonException exception)
        {
            throw new UpdateSecurityException(
                "InvalidReleaseMetadata",
                "Metadata официального релиза не является допустимым JSON.",
                exception);
        }
    }

    public static Uri GetAssetApiUri(long assetId)
    {
        if (assetId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        return new Uri(
            $"https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/assets/{assetId}");
    }

    private static void ValidateReleasePage(Uri uri, string version)
    {
        var expected = $"https://github.com/Flowseal/zapret-discord-youtube/releases/tag/{Uri.EscapeDataString(version)}";
        if (!string.Equals(uri.AbsoluteUri, expected, StringComparison.Ordinal))
        {
            throw new UpdateSecurityException(
                "UnexpectedReleasePage",
                "Страница релиза не соответствует официальному репозиторию Flowseal.");
        }
    }

    private static void ValidateAssetApiUri(Uri uri, long assetId)
    {
        var expected = GetAssetApiUri(assetId);
        if (!string.Equals(uri.AbsoluteUri, expected.AbsoluteUri, StringComparison.Ordinal))
        {
            throw new UpdateSecurityException(
                "UnexpectedAssetApiUri",
                "API URL release asset не соответствует официальному репозиторию Flowseal.");
        }
    }

    private static string GetRequiredString(JsonElement element, string name, int maxLength)
    {
        if (!element.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            throw InvalidMetadata($"Поле {name} отсутствует или имеет неверный тип.");
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw InvalidMetadata($"Поле {name} имеет недопустимое значение.");
        }

        return value;
    }

    private static Uri GetRequiredUri(JsonElement element, string name)
    {
        var value = GetRequiredString(element, name, 512);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw InvalidMetadata($"Поле {name} не содержит допустимый абсолютный URI.");
        }

        return uri;
    }

    private static long GetPositiveInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt64(out var value)
            || value <= 0)
        {
            throw InvalidMetadata($"Поле {name} должно содержать положительное целое число.");
        }

        return value;
    }

    private static bool GetRequiredBoolean(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw InvalidMetadata($"Поле {name} должно содержать boolean.");
        }

        return property.GetBoolean();
    }

    private static UpdateSecurityException InvalidMetadata(string message) =>
        new("InvalidReleaseMetadata", message);
}

public static class UpdateNetworkLimits
{
    public const int MaxReleaseMetadataBytes = 1024 * 1024;
    public const int NetworkTimeoutSeconds = 20;
    public const int MaxRedirects = 1;
}

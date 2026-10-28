using System.Security.Cryptography;

namespace NetPilot.Updater;

public static class Sha256Verifier
{
    public static async Task VerifyAsync(
        Stream stream,
        string expectedSha256Hex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);

        var expected = Convert.FromHexString(expectedSha256Hex);
        if (expected.Length != SHA256.HashSizeInBytes)
        {
            throw new UpdateSecurityException("InvalidDigest", "Ожидаемый SHA-256 имеет недопустимый размер.");
        }

        using var sha256 = SHA256.Create();
        var actual = await sha256.ComputeHashAsync(stream, cancellationToken);

        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new UpdateSecurityException(
                "DigestMismatch",
                "SHA-256 загруженного файла не совпадает с digest официального release asset.");
        }
    }
}

public sealed class UpdateSecurityException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

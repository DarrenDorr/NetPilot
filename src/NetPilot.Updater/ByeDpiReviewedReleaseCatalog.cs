namespace NetPilot.Updater;

/// <summary>
/// R42.12.1 exact reviewed distribution identity for the first ByeDPI package release.
///
/// These constants are derived from the deterministic R42.5 package build and are intended
/// to be the single production descriptor consumed by later live-acquisition wiring. This
/// catalog performs no network access, release creation, filesystem mutation or execution.
/// </summary>
public static class ByeDpiReviewedReleaseCatalog
{
    public const string R42_5Checkpoint = "dc28fc4f0b635c34c63bbaed99ea7658d8a9c827";
    public const string Version = "0.17.3";
    public const string ReleaseTag = "byedpi-v0.17.3";

    public const string ReviewedSourceCommit = "7efde1b1296eaaa187b70e951894dde17527489c";
    public const string ReviewedSourceTree = "8d13f6f5939190cd27ee68696247073a00b3e7dc";

    public const string PayloadName = "NetPilot-ByeDPI-0.17.3-x64.zip";
    public const long PayloadSizeBytes = 496_964;
    public const string PayloadSha256 = "c0fe998e3d81ccb871f09d126e7b5ee0ab82d15daf78bc44bcd63b7762313db7";

    public const string ManifestName = "NetPilot-ByeDPI-0.17.3-x64.manifest.json";
    public const long ManifestSizeBytes = 511;
    public const string ManifestSha256 = "eaa6f063fcb8e012810f849ed08b6bd82d4d046d4796c2fb4b903c4195985f54";

    public const string ProvenanceName = "NetPilot-ByeDPI-0.17.3-x64.provenance.json";
    public const long ProvenanceSizeBytes = 1_726;
    public const string ProvenanceSha256 = "e1f98760eed94f5daa7d37a868015755220cb17d389000bc49e031da511f7f31";

    private static readonly ByeDpiImmutableReleaseDescriptor Reviewed = new(
        ByeDpiImmutableReleaseDocument.RequiredEngineId,
        Version,
        ByeDpiImmutableReleaseDocument.RequiredArchitecture,
        ByeDpiImmutableReleaseDocument.RequiredRepositoryOwner,
        ByeDpiImmutableReleaseDocument.RequiredRepositoryName,
        ReleaseTag,
        ReviewedSourceCommit,
        ReviewedSourceTree,
        new ByeDpiImmutableReleaseAsset(
            PayloadName,
            PayloadSizeBytes,
            PayloadSha256),
        new ByeDpiImmutableReleaseAsset(
            ManifestName,
            ManifestSizeBytes,
            ManifestSha256),
        new ByeDpiImmutableReleaseAsset(
            ProvenanceName,
            ProvenanceSizeBytes,
            ProvenanceSha256));

    public static ByeDpiImmutableReleaseDescriptor Current
    {
        get
        {
            // Force the strict R42.6 document contract whenever the catalog is consumed.
            _ = ByeDpiImmutableReleaseDocument.Serialize(Reviewed);
            return Reviewed;
        }
    }
}

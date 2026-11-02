using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using NetPilot.Platform.Windows;
using NetPilot.Updater;

if (args.Length != 2)
{
    return 64;
}

var resultPath = args[0];
var expectedOwnershipToken = args[1];
var evidence = new List<string>();

try
{
    if (!OperatingSystem.IsWindows())
    {
        throw new InvalidOperationException("R42.13 live acquisition lab requires Windows.");
    }

    using var identity = WindowsIdentity.GetCurrent();
    var sid = identity.User?.Value;
    if (!string.Equals(sid, "S-1-5-18", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("R42.13 live acquisition lab must run as LocalSystem.");
    }
    evidence.Add("IDENTITY_LOCALSYSTEM=PASS");

    if (Process.GetProcessesByName("ciadpi").Length != 0)
    {
        throw new InvalidOperationException("ciadpi process exists before R42.13 acquisition lab.");
    }
    evidence.Add("CIADPI_PROCESS_ABSENT_BEFORE=PASS");

    var productRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "NetPilot");
    var ownershipMarker = Path.Combine(productRoot, ".r42-13-lab-owned");
    if (!File.Exists(ownershipMarker)
        || !string.Equals(
            File.ReadAllText(ownershipMarker).Trim(),
            expectedOwnershipToken,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("R42.13 lab ownership marker is missing or mismatched.");
    }
    evidence.Add("LAB_OWNERSHIP_MARKER=PASS");

    var initialState = WindowsByeDpiAtomicPackagePromoter.GetState();
    if (initialState.CurrentVersion is not null)
    {
        throw new InvalidOperationException("R42.13 lab requires a clean ByeDPI managed state.");
    }
    evidence.Add("INITIAL_MANAGED_STATE_EMPTY=PASS");

    var pipeline = new WindowsByeDpiLiveAcquisitionPipeline();
    var result = await pipeline.ApplyReviewedPackageAsync(
        engineLeaseActive: false,
        CancellationToken.None);

    if (result.Action != WindowsByeDpiLiveAcquisitionAction.Install
        || !result.Changed
        || !string.Equals(
            result.CurrentVersion,
            ByeDpiReviewedReleaseCatalog.Version,
            StringComparison.Ordinal)
        || result.RetainedPreviousVersion is not null)
    {
        throw new InvalidOperationException("R42.13 live acquisition returned an unexpected install result.");
    }
    evidence.Add("LIVE_EXACT_TAG_ACQUISITION=PASS");
    evidence.Add("VERIFIED_DOWNLOADER=PASS");
    evidence.Add("PROTECTED_STAGING=PASS");
    evidence.Add("ATOMIC_INSTALL_PROMOTION=PASS");

    var state = WindowsByeDpiAtomicPackagePromoter.GetState();
    if (!string.Equals(
            state.CurrentVersion,
            ByeDpiReviewedReleaseCatalog.Version,
            StringComparison.Ordinal)
        || state.RetainedPreviousVersion is not null)
    {
        throw new InvalidOperationException("R42.13 protected managed state does not match reviewed release.");
    }
    evidence.Add("PROTECTED_MANAGED_STATE=PASS");

    var managedRoot = Path.Combine(
        productRoot,
        "Engines",
        "Managed",
        "byedpi",
        ByeDpiReviewedReleaseCatalog.Version,
        "x64");
    var executablePath = Path.Combine(managedRoot, "bin", "ciadpi.exe");
    var licensePath = Path.Combine(managedRoot, "LICENSE");

    if (!File.Exists(executablePath) || !File.Exists(licensePath))
    {
        throw new InvalidOperationException("R42.13 managed package files are incomplete.");
    }

    var executableHash = Convert.ToHexString(
        await SHA256.HashDataAsync(
            File.OpenRead(executablePath),
            CancellationToken.None)).ToLowerInvariant();
    if (!string.Equals(
            executableHash,
            "866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd",
            StringComparison.Ordinal)
        || new FileInfo(executablePath).Length != 495_685)
    {
        throw new InvalidOperationException("R42.13 managed ciadpi.exe identity does not match reviewed R42.5 bytes.");
    }
    evidence.Add("MANAGED_CIADPI_EXACT_IDENTITY=PASS");

    var licenseHash = Convert.ToHexString(
        await SHA256.HashDataAsync(
            File.OpenRead(licensePath),
            CancellationToken.None)).ToLowerInvariant();
    if (!string.Equals(
            licenseHash,
            "18c854c6c25dffefe4b522cf1bdf5d15278a25ca785948258bddb94393f575eb",
            StringComparison.Ordinal)
        || new FileInfo(licensePath).Length != 1_063)
    {
        throw new InvalidOperationException("R42.13 managed LICENSE identity does not match reviewed R42.5 bytes.");
    }
    evidence.Add("MANAGED_LICENSE_EXACT_IDENTITY=PASS");

    if (Process.GetProcessesByName("ciadpi").Length != 0)
    {
        throw new InvalidOperationException("R42.13 live acquisition lab unexpectedly launched ciadpi.");
    }
    evidence.Add("CIADPI_PROCESS_ABSENT_AFTER=PASS");
    evidence.Add("NATIVE_EXECUTION_TESTED=False");
    evidence.Add("SERVICE_PRODUCTION_WIRING=ABSENT");
    evidence.Add("PRODUCTION_ADMISSION=BLOCK");
    evidence.Add("RESULT=R42_13_LIVE_ACQUISITION_LOCALSYSTEM_PASS_BLOCK");

    Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
    await File.WriteAllLinesAsync(resultPath, evidence);
    return 0;
}
catch (Exception exception)
{
    evidence.Add("ERROR_TYPE=" + exception.GetType().FullName);
    evidence.Add("ERROR_MESSAGE=" + exception.Message.Replace('\r', ' ').Replace('\n', ' '));
    evidence.Add("NATIVE_EXECUTION_TESTED=False");
    evidence.Add("PRODUCTION_ADMISSION=BLOCK");
    evidence.Add("RESULT=R42_13_LIVE_ACQUISITION_LOCALSYSTEM_FAIL_BLOCK");

    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        await File.WriteAllLinesAsync(resultPath, evidence);
    }
    catch
    {
    }

    return 1;
}

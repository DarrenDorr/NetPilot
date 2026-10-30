using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using NetPilot.Platform.Windows;
using NetPilot.Updater;

if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException("R42.1 staging lab requires Windows.");
}

using var identity = WindowsIdentity.GetCurrent();
var principal = new WindowsPrincipal(identity);
if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
{
    throw new InvalidOperationException("R42.1 staging lab requires an elevated hosted Windows runner.");
}

const string transactionId = "42104210421042104210421042104210";
var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
var netPilotRoot = Path.Combine(programFiles, "NetPilot");
if (Directory.Exists(netPilotRoot))
{
    throw new InvalidOperationException(
        "R42.1 refuses to modify a pre-existing Program Files\\NetPilot tree.");
}

var stagingRoot = WindowsByeDpiProtectedStagingSecurityVerifier.ResolveTransactionRoot(transactionId);
var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

try
{
    CreateProtectedDirectoryChain(netPilotRoot, stagingRoot, adminSid, systemSid, usersSid);

    var executablePath = Path.Combine(stagingRoot, "bin", "ciadpi.exe");
    var licensePath = Path.Combine(stagingRoot, "LICENSE");
    Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
    HardenDirectory(Path.GetDirectoryName(executablePath)!, adminSid, systemSid, usersSid);

    // Synthetic text bytes only: deliberately not a PE/native executable.
    await File.WriteAllTextAsync(
        executablePath,
        "R42.1 synthetic non-PE ByeDPI package fixture",
        TestCancellation.Token);
    await File.WriteAllTextAsync(
        licensePath,
        "MIT synthetic fixture - not a distributed ByeDPI binary",
        TestCancellation.Token);
    HardenFile(executablePath, adminSid, systemSid, usersSid);
    HardenFile(licensePath, adminSid, systemSid, usersSid);

    var manifest = BuildManifest("0.17.3", executablePath, licensePath, stagingRoot);
    var verified = await ByeDpiPackageManifestVerifier.VerifyAsync(
        stagingRoot,
        manifest,
        TestCancellation.Token);
    Console.WriteLine("R42_1_EXACT_MANIFEST=PASS");
    Console.WriteLine($"R42_1_MANIFEST_SHA256={manifest.CanonicalManifestSha256}");

    var security = WindowsByeDpiProtectedStagingSecurityVerifier.Verify(
        transactionId,
        verified.RelativePaths);
    if (!string.Equals(
            security.StagingRoot,
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingRoot)),
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("R42.1 protected staging root identity mismatch.");
    }

    Console.WriteLine("R42_1_PROTECTED_STAGING_ACL=PASS");
    Console.WriteLine($"R42_1_VERIFIED_OBJECTS={security.VerifiedObjectCount}");

    // Prove exact-byte drift is rejected even when the file set is unchanged.
    await File.WriteAllTextAsync(
        executablePath,
        "R42.1 synthetic non-PE ByeDPI package fixture tampered",
        TestCancellation.Token);
    HardenFile(executablePath, adminSid, systemSid, usersSid);
    await ExpectManifestFailureAsync(
        stagingRoot,
        manifest,
        "R42_1_HASH_DRIFT_REJECTED");

    // Restore exact bytes and prove an unmanifested file is rejected.
    await File.WriteAllTextAsync(
        executablePath,
        "R42.1 synthetic non-PE ByeDPI package fixture",
        TestCancellation.Token);
    HardenFile(executablePath, adminSid, systemSid, usersSid);
    var extraPath = Path.Combine(stagingRoot, "extra.txt");
    await File.WriteAllTextAsync(extraPath, "unexpected", TestCancellation.Token);
    HardenFile(extraPath, adminSid, systemSid, usersSid);
    await ExpectManifestFailureAsync(
        stagingRoot,
        manifest,
        "R42_1_EXTRA_FILE_REJECTED");
    File.Delete(extraPath);

    // Reverify exact bytes before the independent Windows ACL gate.
    verified = await ByeDpiPackageManifestVerifier.VerifyAsync(
        stagingRoot,
        manifest,
        TestCancellation.Token);

    // Introduce a write-capable ordinary Users ACE and require fail-closed ACL rejection.
    var rootInfo = new DirectoryInfo(stagingRoot);
    var rootSecurity = FileSystemAclExtensions.GetAccessControl(
        rootInfo,
        AccessControlSections.Access | AccessControlSections.Owner);
    rootSecurity.AddAccessRule(
        new FileSystemAccessRule(
            usersSid,
            FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    FileSystemAclExtensions.SetAccessControl(rootInfo, rootSecurity);

    try
    {
        WindowsByeDpiProtectedStagingSecurityVerifier.Verify(
            transactionId,
            verified.RelativePaths);
        throw new InvalidOperationException(
            "R42.1 ACL drift unexpectedly passed protected staging verification.");
    }
    catch (WindowsEngineSecurityVerificationException exception)
        when (exception.Code == "UntrustedMutationAcl")
    {
        Console.WriteLine("R42_1_USER_WRITABLE_ACL_REJECTED=PASS");
    }

    Console.WriteLine("R42_1_NATIVE_EXECUTION_TESTED=False");
    Console.WriteLine("R42_1_PRODUCTION_ADMISSION=BLOCK");
    Console.WriteLine("RESULT=R42_1_PROTECTED_STAGING_VERIFICATION_PASS_BLOCK");
}
finally
{
    try
    {
        if (Directory.Exists(netPilotRoot))
        {
            Directory.Delete(netPilotRoot, recursive: true);
        }
    }
    catch (Exception cleanupException)
    {
        Console.Error.WriteLine($"R42_1_CLEANUP_WARNING={cleanupException.GetType().Name}");
    }
}

static ByeDpiPackageManifest BuildManifest(
    string version,
    string executablePath,
    string licensePath,
    string root)
{
    var files = new[]
    {
        CreateFileManifest(root, executablePath),
        CreateFileManifest(root, licensePath)
    };

    return new ByeDpiPackageManifest(
        ByeDpiPackageManifest.RequiredEngineId,
        version,
        ByeDpiPackageManifest.RequiredArchitecture,
        ByeDpiPackageManifest.RequiredExecutableRelativePath,
        ByeDpiPackageManifest.RequiredLicenseRelativePath,
        files);
}

static ByeDpiPackageFileManifest CreateFileManifest(string root, string path)
{
    var bytes = File.ReadAllBytes(path);
    var relative = Path.GetRelativePath(root, path)
        .Replace(Path.DirectorySeparatorChar, '/');
    return new ByeDpiPackageFileManifest(
        relative,
        bytes.LongLength,
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
}

static async Task ExpectManifestFailureAsync(
    string stagingRoot,
    ByeDpiPackageManifest manifest,
    string marker)
{
    try
    {
        await ByeDpiPackageManifestVerifier.VerifyAsync(
            stagingRoot,
            manifest,
            TestCancellation.Token);
        throw new InvalidOperationException(
            $"R42.1 expected manifest failure did not occur: {marker}");
    }
    catch (UpdateSecurityException)
    {
        Console.WriteLine($"{marker}=PASS");
    }
}

static void CreateProtectedDirectoryChain(
    string netPilotRoot,
    string stagingRoot,
    SecurityIdentifier adminSid,
    SecurityIdentifier systemSid,
    SecurityIdentifier usersSid)
{
    var current = netPilotRoot;
    Directory.CreateDirectory(current);
    HardenDirectory(current, adminSid, systemSid, usersSid);

    var relative = Path.GetRelativePath(netPilotRoot, stagingRoot);
    foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
    {
        current = Path.Combine(current, segment);
        Directory.CreateDirectory(current);
        HardenDirectory(current, adminSid, systemSid, usersSid);
    }
}

static void HardenDirectory(
    string path,
    SecurityIdentifier adminSid,
    SecurityIdentifier systemSid,
    SecurityIdentifier usersSid)
{
    var info = new DirectoryInfo(path);
    var security = new DirectorySecurity();
    security.SetOwner(adminSid);
    security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
    security.AddAccessRule(
        new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    security.AddAccessRule(
        new FileSystemAccessRule(
            adminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    security.AddAccessRule(
        new FileSystemAccessRule(
            usersSid,
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    FileSystemAclExtensions.SetAccessControl(info, security);
}

static void HardenFile(
    string path,
    SecurityIdentifier adminSid,
    SecurityIdentifier systemSid,
    SecurityIdentifier usersSid)
{
    var info = new FileInfo(path);
    var security = new FileSecurity();
    security.SetOwner(adminSid);
    security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
    security.AddAccessRule(
        new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
    security.AddAccessRule(
        new FileSystemAccessRule(
            adminSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
    security.AddAccessRule(
        new FileSystemAccessRule(
            usersSid,
            FileSystemRights.ReadAndExecute,
            AccessControlType.Allow));
    FileSystemAclExtensions.SetAccessControl(info, security);
}

static class TestCancellation
{
    public static CancellationToken Token => CancellationToken.None;
}

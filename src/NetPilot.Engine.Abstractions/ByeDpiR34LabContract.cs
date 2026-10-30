namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Exact lab-only contract для R34. Объект не добавляется ни в один production catalog
/// и не является runtime admission. Он нужен только изолированному функциональному lab.
/// </summary>
public sealed class ByeDpiR34LabContract
{
    public const string CandidateSha256 =
        "866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd";

    public const string SourceCommit =
        "7efde1b1296eaaa187b70e951894dde17527489c";

    public const string PackageVersion =
        "0.17.3-netpilot-r34-lab";

    private const string LayoutSha256 =
        "b51aa50080033d2a5e975d1521d78c297bb9d823e361fc09c4aa3699cf6426e1";

    private const string ProvenanceSha256 =
        "4019d5add4d30c827f837c01c6525f82e096ff912c3b5d12298f28e523eae12a";

    private ByeDpiR34LabContract(
        EngineLaunchIdentityBinding launchIdentityBinding,
        EnginePackageLayoutDeclaration packageLayout,
        EngineArgumentPlanDeclaration argumentPlan)
    {
        LaunchIdentityBinding = launchIdentityBinding;
        PackageLayout = packageLayout;
        ArgumentPlan = argumentPlan;
    }

    public EngineLaunchIdentityBinding LaunchIdentityBinding { get; }

    public EnginePackageLayoutDeclaration PackageLayout { get; }

    public EngineArgumentPlanDeclaration ArgumentPlan { get; }

    public static ByeDpiR34LabContract Create(ushort loopbackPort)
    {
        if (loopbackPort < 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(loopbackPort),
                "R34 lab proxy port должен быть в непривилегированном диапазоне 1024-65535.");
        }

        var capability = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.ByeDpi,
            EngineHostPlatform.Windows);

        // Эти budget-поля обязательны для общей launch metadata, но ByeDPI R34 argv
        // их не потребляет. Значения фиксированы и не являются production tuning.
        var budget = new EngineResourceBudgetDeclaration(
            BuiltInEngineBackendKind.ByeDpi,
            EngineHostPlatform.Windows,
            conntrackMaxEntries: 64,
            delayedQueueMaxBytes: 1024 * 1024);

        var prerequisites = new EngineRuntimePrerequisiteDeclaration(
            capability,
            budget);

        var metadata = new EngineLaunchPlanMetadata(
            prerequisites,
            EngineLabProfileCatalog.ByeDpiLoopbackR34Id);

        var primary = new EngineExecutableComponentIdentity(
            "ciadpi",
            EngineExecutableComponentKind.PrimaryExecutable,
            CandidateSha256);

        var identity = new EngineExecutableIdentityDeclaration(
            BuiltInEngineBackendKind.ByeDpi,
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64,
            PackageVersion,
            CandidateSha256,
            LayoutSha256,
            ProvenanceSha256,
            Array.AsReadOnly(new[] { primary }));

        var binding = new EngineLaunchIdentityBinding(
            metadata,
            identity,
            EngineBinaryArchitecture.X64);

        var layout = new EnginePackageLayoutDeclaration(
            identity,
            Array.AsReadOnly(
            [
                new EnginePackageComponentLayout("ciadpi", "bin/ciadpi.exe")
            ]));

        var argumentPlan = new EngineArgumentPlanDeclaration(
            binding,
            Array.AsReadOnly(
            [
                new EngineArgumentTokenTemplate(
                    EngineArgumentTokenKind.ShortLoopbackIpv4,
                    "-i"),
                new EngineArgumentTokenTemplate(
                    EngineArgumentTokenKind.ShortFixedPositiveUInt,
                    "-p",
                    loopbackPort),
                new EngineArgumentTokenTemplate(
                    EngineArgumentTokenKind.ShortFlag,
                    "-N"),
                new EngineArgumentTokenTemplate(
                    EngineArgumentTokenKind.ShortFlag,
                    "-U"),
                new EngineArgumentTokenTemplate(
                    EngineArgumentTokenKind.ShortFixedPositiveUInt,
                    "-c",
                    8),
                new EngineArgumentTokenTemplate(
                    EngineArgumentTokenKind.ShortFixedNonNegativeUInt,
                    "-x",
                    0)
            ]));

        return new ByeDpiR34LabContract(binding, layout, argumentPlan);
    }
}

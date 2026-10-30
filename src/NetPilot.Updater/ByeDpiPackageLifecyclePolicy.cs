namespace NetPilot.Updater;

/// <summary>
/// Неисполняемые lifecycle операции будущего production package manager ByeDPI.
/// Этот enum не является runtime admission и не запускает native code.
/// </summary>
public enum ByeDpiPackageLifecycleOperation
{
    Install,
    Upgrade,
    Repair,
    Rollback,
    Uninstall
}

public enum ByeDpiPackageLifecycleDecisionCode
{
    AllowInstall,
    AllowUpgrade,
    AllowRepair,
    AllowRollback,
    AllowUninstall,
    BlockActiveEngineLease,
    BlockInvalidTargetVersion,
    BlockPackageNotVerified,
    BlockUnprotectedStaging,
    BlockCrossVolumeStaging,
    BlockCurrentPackageAlreadyExists,
    BlockCurrentPackageMissing,
    BlockUpgradeNotNewer,
    BlockRepairVersionMismatch,
    BlockRollbackUnavailable,
    BlockRollbackTargetMismatch,
    BlockUnexpectedTargetVersion
}

/// <summary>
/// Снимок доверенного service-owned состояния. Здесь намеренно нет path/URL/argv:
/// policy принимает только типизированные факты, полученные более узкими boundary-компонентами.
/// </summary>
public sealed record ByeDpiPackageLifecycleRequest(
    ByeDpiPackageLifecycleOperation Operation,
    string? TargetVersion,
    string? CurrentVersion,
    string? RetainedPreviousVersion,
    bool PackageVerified,
    bool ProtectedStaging,
    bool SameVolumeStaging,
    bool EngineLeaseActive);

public sealed record ByeDpiPackageLifecycleDecision(
    bool Allowed,
    ByeDpiPackageLifecycleDecisionCode Code,
    string? CanonicalTargetVersion);

/// <summary>
/// Fail-closed policy будущего ByeDPI package lifecycle.
///
/// Важные свойства:
/// - активный engine lease блокирует любую мутацию package state;
/// - install/upgrade/repair/rollback требуют заранее verified package tree;
/// - staging должен быть service-owned/protected и на том же volume для atomic move boundary;
/// - upgrade не допускает downgrade/equal version;
/// - rollback возможен только на конкретную retained previous version;
/// - uninstall не принимает target package;
/// - public policy API не принимает filesystem path, URL или executable arguments.
/// </summary>
public static class ByeDpiPackageLifecyclePolicy
{
    public static ByeDpiPackageLifecycleDecision Evaluate(
        ByeDpiPackageLifecycleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.EngineLeaseActive)
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockActiveEngineLease);
        }

        return request.Operation switch
        {
            ByeDpiPackageLifecycleOperation.Install => EvaluateInstall(request),
            ByeDpiPackageLifecycleOperation.Upgrade => EvaluateUpgrade(request),
            ByeDpiPackageLifecycleOperation.Repair => EvaluateRepair(request),
            ByeDpiPackageLifecycleOperation.Rollback => EvaluateRollback(request),
            ByeDpiPackageLifecycleOperation.Uninstall => EvaluateUninstall(request),
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
    }

    private static ByeDpiPackageLifecycleDecision EvaluateInstall(
        ByeDpiPackageLifecycleRequest request)
    {
        if (HasVersion(request.CurrentVersion))
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockCurrentPackageAlreadyExists);
        }

        if (!TryCanonicalVersion(request.TargetVersion, out var target))
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockInvalidTargetVersion);
        }

        var packageBoundary = RequireVerifiedProtectedPackage(request);
        if (packageBoundary is not null)
        {
            return packageBoundary;
        }

        return Allow(ByeDpiPackageLifecycleDecisionCode.AllowInstall, target);
    }

    private static ByeDpiPackageLifecycleDecision EvaluateUpgrade(
        ByeDpiPackageLifecycleRequest request)
    {
        if (!TryCanonicalVersion(request.CurrentVersion, out var current))
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockCurrentPackageMissing);
        }

        if (!TryCanonicalVersion(request.TargetVersion, out var target))
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockInvalidTargetVersion);
        }

        if (CompareVersions(target, current) <= 0)
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockUpgradeNotNewer);
        }

        var packageBoundary = RequireVerifiedProtectedPackage(request);
        if (packageBoundary is not null)
        {
            return packageBoundary;
        }

        return Allow(ByeDpiPackageLifecycleDecisionCode.AllowUpgrade, target);
    }

    private static ByeDpiPackageLifecycleDecision EvaluateRepair(
        ByeDpiPackageLifecycleRequest request)
    {
        if (!TryCanonicalVersion(request.CurrentVersion, out var current))
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockCurrentPackageMissing);
        }

        if (!TryCanonicalVersion(request.TargetVersion, out var target))
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockInvalidTargetVersion);
        }

        if (!string.Equals(current, target, StringComparison.Ordinal))
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockRepairVersionMismatch);
        }

        var packageBoundary = RequireVerifiedProtectedPackage(request);
        if (packageBoundary is not null)
        {
            return packageBoundary;
        }

        return Allow(ByeDpiPackageLifecycleDecisionCode.AllowRepair, target);
    }

    private static ByeDpiPackageLifecycleDecision EvaluateRollback(
        ByeDpiPackageLifecycleRequest request)
    {
        if (!TryCanonicalVersion(request.CurrentVersion, out _))
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockCurrentPackageMissing);
        }

        if (!TryCanonicalVersion(request.RetainedPreviousVersion, out var previous))
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockRollbackUnavailable);
        }

        if (!TryCanonicalVersion(request.TargetVersion, out var target))
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockInvalidTargetVersion);
        }

        if (!string.Equals(previous, target, StringComparison.Ordinal))
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockRollbackTargetMismatch);
        }

        var packageBoundary = RequireVerifiedProtectedPackage(request);
        if (packageBoundary is not null)
        {
            return packageBoundary;
        }

        return Allow(ByeDpiPackageLifecycleDecisionCode.AllowRollback, target);
    }

    private static ByeDpiPackageLifecycleDecision EvaluateUninstall(
        ByeDpiPackageLifecycleRequest request)
    {
        if (!TryCanonicalVersion(request.CurrentVersion, out _))
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockCurrentPackageMissing);
        }

        if (HasVersion(request.TargetVersion))
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockUnexpectedTargetVersion);
        }

        return Allow(ByeDpiPackageLifecycleDecisionCode.AllowUninstall, null);
    }

    private static ByeDpiPackageLifecycleDecision? RequireVerifiedProtectedPackage(
        ByeDpiPackageLifecycleRequest request)
    {
        if (!request.PackageVerified)
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockPackageNotVerified);
        }

        if (!request.ProtectedStaging)
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockUnprotectedStaging);
        }

        if (!request.SameVolumeStaging)
        {
            return Block(ByeDpiPackageLifecycleDecisionCode.BlockCrossVolumeStaging);
        }

        return null;
    }

    private static bool TryCanonicalVersion(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 32
            || value[0] == '+'
            || value.Any(static c => c is not (>= '0' and <= '9') and not '.'))
        {
            return false;
        }

        var parts = value.Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        var numbers = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part.Length == 0
                || (part.Length > 1 && part[0] == '0')
                || !int.TryParse(part, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out numbers[index])
                || numbers[index] < 0)
            {
                return false;
            }
        }

        canonical = string.Join('.', numbers);
        return true;
    }

    private static int CompareVersions(string left, string right)
    {
        var leftParts = left.Split('.').Select(static value => int.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var rightParts = right.Split('.').Select(static value => int.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var count = Math.Max(leftParts.Length, rightParts.Length);

        for (var index = 0; index < count; index++)
        {
            var leftValue = index < leftParts.Length ? leftParts[index] : 0;
            var rightValue = index < rightParts.Length ? rightParts[index] : 0;
            var comparison = leftValue.CompareTo(rightValue);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static bool HasVersion(string? value) => !string.IsNullOrWhiteSpace(value);

    private static ByeDpiPackageLifecycleDecision Allow(
        ByeDpiPackageLifecycleDecisionCode code,
        string? target) => new(true, code, target);

    private static ByeDpiPackageLifecycleDecision Block(
        ByeDpiPackageLifecycleDecisionCode code) => new(false, code, null);
}

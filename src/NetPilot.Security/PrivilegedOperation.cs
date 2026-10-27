namespace NetPilot.Security;

// Privileged operations are explicit capabilities, not arbitrary command strings.
public enum PrivilegedOperation
{
    EngineInstall,
    EngineStart,
    EngineStop,
    EngineRemove,
    EngineRepair,
    UpdateApply,
    BackupRestore
}

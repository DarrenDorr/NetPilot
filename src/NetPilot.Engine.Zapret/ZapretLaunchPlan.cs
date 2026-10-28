namespace NetPilot.Engine.Zapret;

public sealed record ZapretRuntimeVariables(
    string GameFilterTcp,
    string GameFilterUdp);

public sealed record ZapretLaunchPlan(
    string StrategyId,
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> ArgumentList);

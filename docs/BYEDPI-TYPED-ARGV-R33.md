# ByeDPI typed argv + service-owned lifecycle R33

Status: **IMPLEMENTED / R32 RUNTIME PASS / HOSTED CI PASS / PRODUCTION ADMISSION BLOCK**

## Purpose

R33 connects the reviewed ByeDPI Windows candidate shape to NetPilot's verified launch chain without introducing raw command-line, generic string-value, user-controlled executable, path, URL, environment or script surfaces.

R33 does **not** admit ByeDPI for production and does not expose a user-facing `EngineStart` path.

## R32 prerequisite — PASS

The exact reviewed ByeDPI v0.17.3 Windows candidate passed the manual R32 loopback runtime lab in Actions run `40904363655`.

Confirmed evidence:

- candidate SHA-256: `866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd`;
- listener only on `127.0.0.1` in both executions;
- SOCKS5 method negotiation returned `05 00` in both executions;
- established non-loopback TCP connections: `0`;
- forced-stop cleanup: PASS after both executions;
- no WinDivert;
- no driver installation;
- no Windows service installation by ByeDPI;
- no system proxy mutation;
- final result: `BYEDPI_WINDOWS_LOOPBACK_R32_PASS`.

R32 PASS authorizes only the next controlled integration step. It is not production admission.

## Typed short argv boundary

The existing long-option token kinds remain unchanged. ByeDPI short forms are represented only by dedicated trusted kinds:

- `ShortFlag` -> one exact one-letter option such as `-N` or `-U`;
- `ShortFixedPositiveUInt` -> exact one-letter option plus a trusted positive unsigned decimal value;
- `ShortFixedNonNegativeUInt` -> exact one-letter option plus a trusted non-negative unsigned decimal value;
- `ShortLoopbackIpv4` -> exact one-letter option plus the fixed literal `127.0.0.1`.

There is still no token kind that can carry a generic hostname, IP address, URL, path, script, config text, environment value or arbitrary CLI fragment.

Short option names must be exactly two characters: `-` followed by one ASCII letter. Forms such as `-ab`, `-1`, `-i=value`, `--i` or shell punctuation are rejected.

The Windows typed compiler emits separate `ProcessStartInfo.ArgumentList` entries. The exact R33 service-owned ByeDPI profile is:

```text
-i 127.0.0.1 -p <trusted-port> -N -U -c 8 -x 0
```

The port is a typed positive integer and is accepted only in the bounded range `1024..65535` by the service-owned controller.

## Single audited process-creation boundary

CI initially detected that the first R33 implementation introduced a second `Process.Start` location. That implementation was rejected and removed.

NetPilot retains exactly one audited native process-creation point:

`NetPilot.Engine.Zapret.Windows/SystemZapretProcessLauncher.cs`

The launcher now supports two typed inputs:

1. the existing `ZapretLaunchPlan`;
2. `WindowsCompiledEngineArgumentPlan` produced by the verified immutable-package / protected-ACL / typed-argv chain.

Both overloads converge on the same private method containing the repository's single `Process.Start` call.

The R33 service-owned controller contains no `Process.Start` and accepts no executable/path/CLI strings.

## Service-owned ByeDPI lifecycle

`WindowsByeDpiServiceOwnedProcessController` provides a bounded one-process lifecycle:

- `GetStatusAsync`;
- `StartAsync`;
- `StopAsync`;
- `DisposeAsync`.

Before launch it requires:

- backend = `ByeDpi`;
- platform = Windows;
- architecture = x64;
- exact typed argument declaration matching the R32 loopback profile;
- exact compiled argv matching that declaration;
- a live verified pre-launch handoff.

The controller rejects a second concurrent start and uses bounded forced-stop cleanup.

## Production fail-closed composition

Production `NetPilot.Service` registers:

`NoAdmittedWindowsVerifiedEngineProcessLauncher.Instance`

Therefore the presence of the lifecycle/controller and audited system launcher does **not** enable native execution in the installed product.

The existing no-admission guarantees remain in force:

- `OperationPolicy.CreateNoAdmittedEnginePolicy()`;
- no production `EngineStart` capability;
- no production ByeDPI argument-plan catalog entry;
- no user-provided raw command/path/URL/arguments;
- no WinDivert admission;
- no system proxy/routing integration.

`BuiltInEngineArgumentPlanCatalog` remains intentionally empty.

## Enrollment policy consistency

R31.1 intentionally changed the client-side enrollment helper: elevation itself is not the authentication boundary. A known `NotElevated` or `Elevated` token state may proceed to the authoritative LocalSystem SID/session/nonce checks, while `Unknown` must fail closed.

The security policy checker was updated to enforce this current rule instead of the obsolete R26.3 literal-only `NotElevated` check.

## Hosted CI evidence

R33 checkpoint `dbb241f091b8d64ccfe00495b09f3372bebad0d6` passed hosted CI run `40915349875`:

- security policy check: PASS;
- runtime admission composition check: PASS;
- Linux restore/build: PASS;
- Core/IPC tests: PASS;
- Zapret parser tests: PASS;
- Windows security-test project build: PASS;
- Windows GUI host build: PASS;
- UAC lab client build: PASS;
- Windows unit/local-pipe security tests: PASS;
- verified Inno Setup installer build: PASS;
- installer artifact sanity check: PASS.

The ByeDPI compile/static workflows for the same checkpoint also completed without a failure result.

## Current effect

```text
R32_BYEDPI_LOOPBACK_RUNTIME = PASS
BYEDPI_TYPED_SHORT_ARGV = IMPLEMENTED
BYEDPI_SERVICE_OWNED_LIFECYCLE = IMPLEMENTED
SINGLE_AUDITED_PROCESS_START = ENFORCED
PRODUCTION_ARGUMENT_PLAN = NONE
PRODUCTION_ENGINE_START = BLOCKED
PRODUCTION_ADMISSION = BLOCK
WIN_DIVERT = NOT_ADMITTED
```

## Next gate

R34 should exercise the new service-owned lifecycle in an isolated synthetic local functional lab:

1. exact verified ByeDPI candidate/package identity;
2. typed internal loopback profile only;
3. launch through the single audited process boundary;
4. synthetic local SOCKS/HTTP traffic only;
5. controlled stop/restart/crash cleanup;
6. no system proxy mutation;
7. no WinDivert;
8. no production admission.

Only after separate R34 evidence and later production-admission review may the project consider wiring a user-facing runtime path.

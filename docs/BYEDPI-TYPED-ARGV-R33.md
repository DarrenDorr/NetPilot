# ByeDPI typed argv bridge R33

Status: **PREPARED / NON-EXECUTING / PRODUCTION ADMISSION BLOCK**

## Purpose

R33 prepares the existing verified Windows process-launch chain for the short option syntax used by ByeDPI without introducing a raw command-line or generic string-value surface.

R33 is deliberately non-executing. It does not add a production argument-plan catalog entry, does not call `Process.Start`, does not install ByeDPI and does not change system proxy/routing.

## Security boundary

The original long-option token kinds remain unchanged. New short forms are represented only by dedicated trusted kinds:

- `ShortFlag` -> one exact one-letter option such as `-N` or `-U`;
- `ShortFixedPositiveUInt` -> exact one-letter option plus a trusted positive unsigned decimal value;
- `ShortFixedNonNegativeUInt` -> exact one-letter option plus a trusted non-negative unsigned decimal value;
- `ShortLoopbackIpv4` -> exact one-letter option plus the fixed literal `127.0.0.1`.

There is still no token kind that can carry a generic hostname, IP address, URL, path, script, config text, environment value or arbitrary CLI fragment.

Short option names must be exactly two characters: `-` followed by one ASCII letter. Forms such as `-ab`, `-1`, `-i=value`, `--i` or shell punctuation are rejected.

## Compiler output

The Windows typed argument compiler may now emit separate `ArgumentList` entries required by short-option CLIs. For the planned ByeDPI loopback profile the representable shape is equivalent to:

```text
-i 127.0.0.1 -p <trusted-positive-port> -N -U -c <trusted-positive-count> -x <trusted-non-negative-value>
```

The angle-bracket values above are typed numeric sources, not user strings.

The non-starting `WindowsVerifiedEngineProcessStartInfoFactory` keeps a second structural gate and accepts only:

- existing long `--...` tokens from the trusted compiler;
- exact one-letter short options;
- unsigned decimal value tokens;
- exact `127.0.0.1` loopback literal.

It still uses `ProcessStartInfo.ArgumentList`, clears inherited environment, and does not expose `Process.Start`.

## Production effect

`BuiltInEngineArgumentPlanCatalog` remains intentionally empty.

Therefore R33 by itself has the following effect:

```text
BYEDPI_TYPED_SHORT_ARGV = PREPARED
PROCESS_START = NOT_ADDED
PRODUCTION_ARGUMENT_PLAN = NONE
PRODUCTION_ADMISSION = BLOCK
PRODUCTION_INTEGRATION = BLOCK
```

## Dependency on R32

R33 must not be promoted into the main engine integration path until the manual R32 ByeDPI Windows loopback runtime lab produces reviewed PASS evidence for the exact candidate binary.

After R32 PASS, the next R33 work is to bind an exact ByeDPI immutable package identity and a fixed internal loopback profile to the service-owned lifecycle while preserving the global engine lease.

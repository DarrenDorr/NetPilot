#!/usr/bin/env bash
set -euo pipefail

admission_dir='security/native-admission'
app_windows_program='src/NetPilot.App.Windows/Program.cs'

fail() {
  echo "ОШИБКА RUNTIME ADMISSION POLICY: $1"
  exit 1
}

if [[ ! -d "$admission_dir" ]]; then
  fail 'каталог native admission отсутствует.'
fi

if [[ ! -f "$app_windows_program" ]]; then
  fail 'Windows GUI composition root отсутствует.'
fi

admitted_files="$(grep -RIlE \
  '^admission_status:[[:space:]]*ADMIT[[:space:]]*$' \
  "$admission_dir" \
  --include='*.yaml' \
  --include='*.yml' \
  2>/dev/null || true)"

if [[ -n "$admitted_files" ]]; then
  echo 'Native admission содержит ADMIT: deny-all composition guard не принимает решение за candidate-specific admission wiring.'
  exit 0
fi

# Пока ADMIT отсутствует, GUI обязан оставаться на полностью пустом profile runtime.
no_admitted_runtime_count="$(grep -cF 'new NoAdmittedEngineProfileRuntime()' "$app_windows_program" || true)"
if [[ "$no_admitted_runtime_count" -ne 1 ]]; then
  fail 'без ADMIT GUI обязан создавать ровно один NoAdmittedEngineProfileRuntime.'
fi

# R28.7: capability declaration не является admission. Production composition должна
# явно передавать deny-all policy, а не полагаться на default/optional поведение.
deny_all_policy_count="$(grep -cF 'DenyAllEngineRuntimeAdmissionPolicy.Instance' "$app_windows_program" || true)"
if [[ "$deny_all_policy_count" -ne 1 ]]; then
  fail 'без ADMIT GUI обязан явно использовать DenyAllEngineRuntimeAdmissionPolicy.Instance.'
fi

if ! grep -qF 'new EngineProfileStrategyRuntimeAdapter(' "$app_windows_program"; then
  fail 'GUI обязан проходить через engine-neutral EngineProfileStrategyRuntimeAdapter.'
fi

# Legacy Zapret strategy runtime остаётся только тестируемой compatibility boundary и
# не может возвращаться в production GUI composition.
if grep -qF 'new WindowsZapretStrategyRuntime(' "$app_windows_program"; then
  fail 'legacy WindowsZapretStrategyRuntime запрещён в GUI composition.'
fi

# Пока ADMIT отсутствует, GUI composition не должна даже получать trusted backend
# declaration: это исключает случайное превращение capability metadata в runtime grant.
if grep -qF 'BuiltInEngineBackendCapabilityCatalog' "$app_windows_program"; then
  fail 'trusted backend capability catalog запрещён в GUI composition до отдельного ADMIT.'
fi

if grep -qF 'IEngineBackendCapabilityProvider' "$app_windows_program"; then
  fail 'trusted backend capability provider запрещён в GUI composition до отдельного ADMIT.'
fi

echo 'R28.8 runtime admission composition policy пройдена: native ADMIT отсутствует, GUI остаётся deny-all.'

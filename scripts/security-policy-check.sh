#!/usr/bin/env bash
set -euo pipefail

fail=0

check_pattern() {
  local description="$1"
  local pattern="$2"

  if grep -RniE \
    --include='*.cs' \
    --include='*.csproj' \
    --include='*.props' \
    --include='*.targets' \
    --exclude-dir=bin \
    --exclude-dir=obj \
    "$pattern" src Directory.Packages.props Directory.Build.props 2>/dev/null; then
    echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: ${description}"
    fail=1
  fi
}

# NetPilot Core не должен иметь скрытую связь с аккаунтами ChatGPT/OpenAI.
check_pattern \
  "обнаружена запрещённая OpenAI/ChatGPT-интеграция в исполняемом коде" \
  'api\.openai\.com|auth\.openai\.com|chatgpt\.com|chat\.openai\.com|OPENAI_API_KEY|Azure\.AI\.OpenAI|OpenAIClient'

# Привилегированная часть не должна выполнять произвольный shell-код.
check_pattern \
  "обнаружен запрещённый запуск shell" \
  'cmd\.exe|powershell\.exe|pwsh(\.exe)?|bash[[:space:]]+-c|sh[[:space:]]+-c'

check_pattern \
  "UseShellExecute=true запрещён; процессы должны запускаться напрямую" \
  'UseShellExecute[[:space:]]*=[[:space:]]*true'

check_pattern \
  "строковое ProcessStartInfo.Arguments запрещено; используйте ArgumentList" \
  '\.Arguments[[:space:]]*='

# Общий Avalonia/App слой должен оставаться платформенно-нейтральным.
# Windows IPC разрешён только в платформенных адаптерах и host-проектах.
app_ipc_matches="$(grep -RniE \
  --include='*.cs' \
  --include='*.csproj' \
  --exclude-dir=bin \
  --exclude-dir=obj \
  'NetPilot\.Ipc|NetPilot\.Ipc\.csproj' \
  src/NetPilot.App 2>/dev/null || true)"

if [[ -n "$app_ipc_matches" ]]; then
  printf '%s\n' "$app_ipc_matches"
  echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: NetPilot.App не должен напрямую зависеть от Windows IPC."
  fail=1
fi

# Реальный Process.Start разрешён только в одной отдельно проверяемой точке.
allowed_process_launcher='src/NetPilot.Engine.Zapret.Windows/SystemZapretProcessLauncher.cs'
process_pattern='Process[[:space:]]*\.Start[[:space:]]*\('
process_matches="$(grep -RniE --include='*.cs' --exclude-dir=bin --exclude-dir=obj "$process_pattern" src 2>/dev/null || true)"
unexpected_process_matches="$(printf '%s\n' "$process_matches" | grep -v "^${allowed_process_launcher}:" || true)"

if [[ -n "$unexpected_process_matches" ]]; then
  printf '%s\n' "$unexpected_process_matches"
  echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: Process.Start разрешён только в audited SystemZapretProcessLauncher."
  fail=1
fi

if [[ ! -f "$allowed_process_launcher" ]]; then
  echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: audited SystemZapretProcessLauncher отсутствует."
  fail=1
else
  allowed_process_count="$(grep -cE "$process_pattern" "$allowed_process_launcher" || true)"
  if [[ "$allowed_process_count" -ne 1 ]]; then
    echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: audited launcher должен содержать ровно одну точку Process.Start."
    fail=1
  fi
fi

# Privileged updater не может напрямую доверять upstream latest.
# Service обязан ставить transport за admission-wrapper, а не регистрировать
# GithubZapretReleaseClient как IZapretReleaseSource напрямую.
service_program='src/NetPilot.Service/Program.cs'
admission_dir='security/native-admission'

if [[ ! -f "$service_program" ]]; then
  echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: Program.cs службы NetPilot отсутствует."
  fail=1
else
  if grep -qE 'AddSingleton<IZapretReleaseSource,[[:space:]]*GithubZapretReleaseClient>' "$service_program"; then
    echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: GitHub transport нельзя регистрировать напрямую как доверенный release source."
    fail=1
  fi

  if ! grep -qF 'new AdmittedZapretReleaseSource(' "$service_program"; then
    echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: privileged updater обязан проходить через AdmittedZapretReleaseSource."
    fail=1
  fi
fi

# Native admission должен реально влиять на runtime, а не существовать только как документация.
# Пока нет ни одного component set со статусом ADMIT, службе запрещено выдавать capabilities
# установки и запуска движка. EngineStop остаётся доступным через специальный fail-closed профиль,
# чтобы можно было остановить уже существующий legacy-процесс.
if [[ ! -d "$admission_dir" ]]; then
  echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: каталог native admission отсутствует."
  fail=1
else
  admitted_files="$(grep -RIlE '^admission_status:[[:space:]]*ADMIT[[:space:]]*$' "$admission_dir" --include='*.yaml' --include='*.yml' 2>/dev/null || true)"

  if [[ -z "$admitted_files" ]]; then
    if [[ ! -f "$service_program" ]]; then
      echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: Program.cs службы NetPilot отсутствует."
      fail=1
    else
      if ! grep -qF 'OperationPolicy.CreateNoAdmittedEnginePolicy()' "$service_program"; then
        echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: без ADMIT служба обязана использовать fail-closed OperationPolicy."
        fail=1
      fi

      if ! grep -qF 'NoAdmittedZapretReleaseCatalog.Instance' "$service_program"; then
        echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: без ADMIT updater обязан использовать пустой admission catalog."
        fail=1
      fi

      direct_engine_capabilities="$(grep -nE 'PrivilegedOperation\.(EngineInstall|EngineStart)' "$service_program" || true)"
      if [[ -n "$direct_engine_capabilities" ]]; then
        printf '%s\n' "$direct_engine_capabilities"
        echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: EngineInstall/EngineStart запрещены, пока native admission не содержит ADMIT."
        fail=1
      fi
    fi
  fi
fi

if [[ "$fail" -ne 0 ]]; then
  echo "Проверка политики безопасности NetPilot завершилась ошибкой."
  exit 1
fi

echo "Проверка политики безопасности NetPilot пройдена."

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

# Реальный Process.Start разрешён только в двух отдельно проверяемых launchers:
# privileged Zapret и непривилегированный ByeDPI user-mode.
allowed_process_launchers=(
  'src/NetPilot.Engine.Zapret.Windows/SystemZapretProcessLauncher.cs'
  'src/NetPilot.Platform.Windows/AuditedByeDpiUserModeProcessLauncher.cs'
)
process_pattern='Process[[:space:]]*\.Start[[:space:]]*\('
process_matches="$(grep -RniE --include='*.cs' --exclude-dir=bin --exclude-dir=obj "$process_pattern" src 2>/dev/null || true)"
unexpected_process_matches="$process_matches"

for launcher in "${allowed_process_launchers[@]}"; do
  unexpected_process_matches="$(printf '%s\n' "$unexpected_process_matches" | grep -v "^${launcher}:" || true)"
done

if [[ -n "$unexpected_process_matches" ]]; then
  printf '%s\n' "$unexpected_process_matches"
  echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: Process.Start обнаружен вне audited launchers."
  fail=1
fi

for launcher in "${allowed_process_launchers[@]}"; do
  if [[ ! -f "$launcher" ]]; then
    echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: audited launcher отсутствует: $launcher"
    fail=1
    continue
  fi

  allowed_process_count="$(grep -ciE "$process_pattern" "$launcher" || true)"
  if [[ "$allowed_process_count" -ne 1 ]]; then
    echo "ОШИБКА ПОЛИТИКИ БЕЗОПАСНОСТИ: audited launcher должен содержать ровно одну точку Process.Start: $launcher"
    fail=1
  fi
done

if [[ "$fail" -ne 0 ]]; then
  echo "Проверка политики безопасности NetPilot завершилась ошибкой."
  exit 1
fi

echo "Проверка политики безопасности NetPilot пройдена."

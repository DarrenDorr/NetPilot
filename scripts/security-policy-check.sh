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

# До отдельного этапа process runner никакой модуль не должен реально стартовать процессы.
check_pattern \
  "Process.Start пока запрещён; сначала требуется отдельный audited runner" \
  'Process[[:space:]]*\.Start[[:space:]]*\('

if [[ "$fail" -ne 0 ]]; then
  echo "Проверка политики безопасности NetPilot завершилась ошибкой."
  exit 1
fi

echo "Проверка политики безопасности NetPilot пройдена."

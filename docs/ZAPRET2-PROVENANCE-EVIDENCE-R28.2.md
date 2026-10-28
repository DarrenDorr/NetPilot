# R28.2 — evidence source/provenance для zapret2 patch 0001

Дата проверки: 28.10.2026.

Статус этого gate: **PASS только для source anchor и применимости patch 0001**. Это не `ADMIT`, не разрешение запуска и не разрешение интеграции native engine.

## GitHub Actions

- workflow: `Native Candidate Provenance`;
- run: `40708142913`;
- head NetPilot: `859c53e1fdd2e3f7ba92e84f82207f84c992ffe7`;
- conclusion: `success`;
- обычный CI на той же голове: run `40708142910`, `success`;
- CodeQL на той же голове: run `40708143025`, `success`.

## Проверенный upstream anchor

- repository: `bol-van/zapret2`;
- head: `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`;
- parent: `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`;
- tree: `5c2a77786cc7089145abe2ca99bcc53a8ed7de15`;
- commit message: `nfqws2: fix UAF`;
- delta parent → head: только `nfq2/desync.c`.

## Artifact

- artifact id: `12824806940`;
- name: `zapret2-r28-source-provenance`;
- GitHub artifact digest / проверенный SHA-256 ZIP: `700962c2acb0d9873836bb371a7a6d51459c0951d9796bd5065fd6678787b7b6`.

Artifact был скачан после завершения workflow. Проверенный состав:

- `zapret2-source-anchor.txt`;
- `zapret2-uaf-fix.patch`;
- `netpilot-0001-explicit-conntrack-feed-result.patch`;
- `SHA256SUMS.txt`.

Проверенные SHA-256:

- source anchor: `dfd059a545ef2d7cbe9e322ab6d5a993a75ab45452cf964c24d3f47e9274e369`;
- exact upstream UAF patch: `a6cc3426bd38bf4c38d6c87edec64b3436386a95a2d5cd7d9d935d5d960df38e`;
- NetPilot patch 0001: `af811313b37efc14f157bdc1744e9a74ea3c009b03fc1d518d9ab327a35facff`.

Внутренний `SHA256SUMS.txt` соответствует файлам artifact.

## Что доказано

1. Мы работаем не с текущим `master`, а с точным post-v1.0.6 UAF-fix tree.
2. UAF-fix является прямым дочерним изменением exact release commit.
3. Patch 0001 синтаксически корректен и применим к exact tree через `git apply --check`.
4. Patch 0001 затрагивает только allowlist файлов:
   - `nfq2/conntrack.c`;
   - `nfq2/conntrack.h`;
   - `nfq2/desync.c`;
   - `nfq2/lua.c`.
5. Patch 0001 пока является behavior-preserving подготовительным refactor и не вводит resource limit.

## Что НЕ доказано

- что patched source компилируется на всех целевых платформах;
- что binary безопасен;
- что WinDivert/Cygwin/Lua dependencies допустимы;
- что runtime behavior эквивалентен во всех случаях;
- что resource exhaustion уже bounded;
- что движок можно устанавливать или запускать из NetPilot.

Следующий gate: patch 0002 `RESOURCE_EXHAUSTED + aggregate conntrack budget`, затем patch 0003 `delayed queue byte budget`, controlled build и native inventory.

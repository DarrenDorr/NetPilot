# R28.5 — резервный Windows backend без собственного kernel driver

Дата: 29.10.2026.

Статус: **DESIGN ONLY / RUNTIME НЕ РАЗРЕШЁН**.

## Цель

Определить, может ли hardened ByeDPI служить безопасным резервом для Windows v1.0, если WinDivert blocker затянется, не выдавая ограниченный proxy backend за полноценную system-wide transparent замену.

## Exact ByeDPI boundary

Проверяемая база:

- ByeDPI v0.17.3;
- commit `7efde1b1296eaaa187b70e951894dde17527489c`;
- NetPilot hardened patchset `0001–0007`;
- listener policy: loopback only.

Exact v0.17.3 source показывает:

- локальный SOCKS listener;
- UDP association может быть разрешён/запрещён (`--no-udp`);
- `--transparent` присутствует только под `#ifdef __linux__` и не является Windows mode.

Следовательно, Windows ByeDPI является user-mode proxy boundary, а не встроенным system-wide transparent packet diversion backend.

## Что потенциально покрывает ByeDPI на Windows

После отдельного `LAB_APPROVED` и runtime evidence ByeDPI может рассматриваться для приложений, которые явно используют NetPilot loopback SOCKS proxy:

- обычный HTTP/HTTPS browser traffic;
- web/YouTube traffic, если конкретный client/browser использует proxy path;
- TCP connections приложений, которые поддерживают SOCKS/proxy configuration;
- UDP только для клиентов, реально использующих SOCKS5 UDP association.

Это не является обещанием совместимости до controlled runtime evidence.

## Что ByeDPI сам по себе НЕ покрывает

Без отдельного routing/TUN component нельзя считать покрытыми:

- произвольный system-wide TCP traffic Windows;
- приложения, игнорирующие SOCKS/system proxy;
- Discord voice/media UDP, если клиент отправляет UDP напрямую;
- system-wide QUIC/HTTP3 interception;
- прозрачный Discord + YouTube combined profile на уровне всей ОС.

Наличие UDP association внутри SOCKS proxy не означает автоматический захват direct UDP от приложения.

## Почему не добавляем скрытый redirector

NetPilot не должен компенсировать это:

- DLL injection;
- process hooking;
- undocumented Winsock interception;
- arbitrary firewall redirect rules;
- неподтверждённым сторонним TUN/redirector binary;
- packet interception component без отдельного admission.

Если позже потребуется Windows TUN/transparent routing, этот компонент получает собственные provenance/license/lifecycle/update/security gates.

## Release strategy

### Полный Windows profile set

Для профилей `standard`, `discord`, `youtube`, `discord-youtube` с ожидаемым system-wide поведением предпочтительным backend остаётся zapret2 через безопасно admitted packet-diversion boundary.

### Degraded user-mode fallback

Если WinDivert blocker не закрыт к release candidate, можно отдельно рассматривать ограниченный user-mode mode на ByeDPI, но только при выполнении всех условий:

1. UI явно показывает, что это proxy mode, а не полный system-wide mode;
2. profile capabilities объявляются честно;
3. Discord UDP/voice не помечается как поддержанный без доказательства;
4. browser/client proxy configuration выполняется обратимо;
5. stop/crash/uninstall полностью восстанавливают исходные proxy settings;
6. runtime проходит отдельный loopback-only lab;
7. user can disable/restore proxy without admin recovery procedure.

Такой fallback не должен молча заменять полноценный `discord-youtube` profile.

## Архитектурный вывод

Сейчас стратегия Windows остаётся двухконтурной:

- **primary full backend:** hardened zapret2 + безопасный admitted Windows packet-diversion component;
- **secondary user-mode backend:** hardened ByeDPI loopback-only proxy с ограниченными capability flags.

DPIBreak остаётся reserve component и не решает WinDivert kernel boundary на Windows.

## Admission effect

На текущем этапе:

- ByeDPI Windows runtime: `BLOCK`;
- loopback lab: `DRAFT_NOT_APPROVED`;
- Windows transparent mode in ByeDPI: `NOT_AVAILABLE`;
- separate TUN/routing component admitted: `false`;
- Discord UDP/voice capability: `NOT_PROVEN`;
- production activation: `false`;
- release effect: `NONE`.

Этот документ описывает fallback architecture и не разрешает candidate execution.

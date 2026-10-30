# Используемые сторонние проекты

NetPilot — независимый проект. Он не является официальным продуктом Flowseal, bol-van, hufrea/ByeDPI, WinDivert, Inno Setup или их разработчиков.

Этот файл служит кратким указателем. При распространении конкретной сборки приоритет имеют оригинальные тексты лицензий соответствующих сторонних компонентов и точный release manifest.

## Inno Setup

- Проект: Inno Setup
- Назначение в NetPilot: создание Windows-установщика
- Закреплённая версия сборочного инструмента: 7.1.1 x64
- Исходный код: https://github.com/jrsoftware/issrc
- Официальный сайт: https://jrsoftware.org/isinfo.php
- Лицензия: оригинальная лицензия Inno Setup; её текст и условия публикуются авторами проекта

Inno Setup является сборочной зависимостью. Его лицензия не меняет MIT-лицензию собственного кода NetPilot.

## hufrea / ByeDPI

- Репозиторий: https://github.com/hufrea/byedpi
- Рассматриваемая версия: v0.17.3
- Рассматриваемый upstream commit: `7efde1b1296eaaa187b70e951894dde17527489c`
- Назначение: кандидат отдельного loopback explicit-proxy engine для NetPilot
- Лицензия: MIT License
- Copyright: Copyright (c) 2024 hufrea
- Локальная копия оригинального текста лицензии для release-подготовки: `third_party/licenses/ByeDPI-MIT.txt`

На этапе R41.1 ByeDPI **не входит в production installer и не имеет production ADMIT**. Наличие attribution и текста лицензии в репозитории не означает разрешение на production native execution или распространение бинарного файла в составе NetPilot.

Если ByeDPI когда-либо будет включён в распространяемую сборку, вместе с соответствующим бинарным/исходным компонентом должен распространяться применимый оригинальный MIT copyright/license notice. Точный fork/patchset, версия и hash должны быть зафиксированы в release manifest и admission evidence.

## Flowseal / zapret-discord-youtube

- Репозиторий: https://github.com/Flowseal/zapret-discord-youtube
- Назначение: официальный upstream-набор стратегий и Windows-материалов Zapret, с которым интегрируется NetPilot

NetPilot не должен скрытно изменять официальные upstream-стратегии. Пользовательские изменения создаются как отдельные структуры NetPilot.

## Flowseal / tg-ws-proxy

- Репозиторий: https://github.com/Flowseal/tg-ws-proxy
- Назначение: планируемый отдельный Telegram WebSocket/MTProto engine

Telegram engine не включён в текущий production installer.

## bol-van / zapret

- Репозиторий: https://github.com/bol-van/zapret
- Назначение: базовый проект Zapret и справочная документация движка

## WinDivert

- Проект: WinDivert
- Репозиторий/сайт: https://github.com/basil00/WinDivert
- Назначение: Windows packet interception driver, используемый upstream Zapret

WinDivert является отдельным сторонним компонентом со своей лицензией. Перед публичным релизом NetPilot проводится отдельная сверка лицензий всех реально распространяемых бинарных файлов. ByeDPI admission сам по себе не разрешает WinDivert или transparent routing.

## Release manifest

Canonical release metadata для текущей ветки разработки хранится в `release/release-manifest.yaml`. Он фиксирует repository/support/update URL, закреплённый build-tool identity и статус сторонних native-компонентов. Исполняемые build scripts дополнительно проверяются на согласованность с ним.

## Правило перед релизом

Перед каждым публичным релизом NetPilot необходимо:

1. определить точные версии реально включаемых сторонних компонентов;
2. сохранить и распространять оригинальные тексты их лицензий там, где этого требуют условия распространения;
3. проверить совместимость лицензий с моделью распространения NetPilot;
4. сверить `release/release-manifest.yaml`, build scripts, installer metadata и этот файл;
5. обновить этот файл при изменении фактически распространяемого component set;
6. не полагаться только на краткое описание лицензии в интерфейсе.

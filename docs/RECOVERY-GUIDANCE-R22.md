# R22 — единая классификация сбоя и рекомендации

## Статус

R22 устраняет дублирование логики между Automation и GUI.

До R22 автоматический подбор профиля уже имел fail-closed policy R19, а интерфейс отдельно расшифровывал диагностические коды. При дальнейшем развитии эти две таблицы могли разойтись: интерфейс мог рекомендовать смену профиля для причины, которую Automation считает неподходящей, или наоборот.

R22 вводит единый engine-neutral классификатор в Core.

## ResourceHealthFailureClassifier

Классификатор получает только готовый `ResourceHealth` и возвращает один из классов:

- `Healthy`;
- `RecoverableNetworkPath`;
- `DnsFailure`;
- `ServiceResponse`;
- `SecurityOrConfiguration`;
- `Unclassified`.

Он не выполняет сетевых, файловых или привилегированных операций.

## RecoverableNetworkPath

Только следующие diagnostic codes входят в явный allowlist:

- `TcpConnectFailed`;
- `TlsHandshakeFailed`;
- `Timeout`.

Только этот класс может дать Automation право проверять альтернативные сетевые профили.

## DnsFailure

`DnsResolutionFailed` классифицируется отдельно.

NetPilot сообщает пользователю о проблеме DNS, но не пытается автоматически лечить её сменой DPI-профиля и не меняет DNS-настройки сам.

## ServiceResponse

В этот класс входят:

- корректные `HttpXYZ`, где `XYZ` строго от 100 до 599;
- `TooManyRedirects`;
- `RedirectWithoutLocation`.

Если сервис уже дал HTTP/redirect-результат, одного этого недостаточно, чтобы автоматически менять сетевой профиль.

Строки вроде `Http999` не считаются HTTP-кодом и fail-closed переходят в `Unclassified`.

## SecurityOrConfiguration

В этот класс входят:

- `EndpointRejected`;
- `NoHealthEndpoints`.

Automation не пытается обходить собственные ограничения безопасности через смену профиля.

## Unclassified

Любой:

- неизвестный будущий diagnostic code;
- пустой diagnostic code при неуспешном результате;
- `Unknown`;
- `Checking`;
- неподдерживаемый формат

классифицируется как `Unclassified`.

Это fail-closed default: появление нового диагностического кода не расширяет автоматически возможности Automation.

## Aggregate classification

Для нескольких сервисов действует приоритет:

1. `SecurityOrConfiguration`;
2. `DnsFailure`;
3. `ServiceResponse`;
4. `Unclassified`;
5. `RecoverableNetworkPath`.

То есть наличие одного более опасного/неподходящего класса подавляет автоматическое вмешательство даже если другой сервис одновременно показывает TLS/TCP-сбой.

## Automation

`StrategyRecoveryPolicy` больше не содержит собственной таблицы строковых diagnostic codes.

Он использует `ResourceHealthFailureClassifier.ClassifyAggregate()` и сохраняет прежние результаты R19:

- network path → альтернативы разрешены;
- DNS → `RecoverySuppressedDnsFailure`;
- service response → `RecoverySuppressedHttpResponse`;
- security/configuration → `RecoverySuppressedSecurityOrConfiguration`;
- unknown → `RecoverySuppressedUnclassifiedFailure`.

## GUI

Главная карточка состояния использует тот же classifier.

Пользователь получает рекомендации вида:

- DNS: проверить подключение и DNS-настройки, автоматическая смена профиля не выполняется;
- service response: сервис отвечает, но вернул ошибку/ограничение; профиль автоматически не меняется;
- security/configuration: вмешательство запрещено policy;
- recoverable network path + admitted engine: можно запустить подбор разрешённого профиля;
- recoverable network path без admitted engine: проблема похожа на сетевой путь, но непроверенный native package запускаться не будет;
- unclassified: сохранить текущую конфигурацию и не вмешиваться автоматически.

## Что R22 не делает

R22 не:

- меняет DNS;
- меняет proxy;
- запускает native engine;
- расширяет список admitted компонентов;
- добавляет endpoint;
- делает дополнительные HTTP-запросы;
- читает содержимое трафика;
- сохраняет новые пользовательские данные;
- отправляет телеметрию.

Это только единая классификация уже полученного безопасного результата.

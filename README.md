# pets — мониторинг вакансий из ATS в Telegram

Этап 1: обход явно заданного списка бордов Greenhouse, детект изменений, отправка в Telegram,
добавление компаний по имени через бота. Реестр «всех компаний» — следующий этап.

## Структура

```
src/
  Pets.Core/                 контракты и вся содержательная логика, без зависимостей от ATS
    Abstractions/            IVacancySource, IVacancySink, IWatchlistProvider, IStateStore, IOutbox
    Model/                   Vacancy, FilterSpec, Watchlist, SourceFetchResult
    Pipeline/                PollingOrchestrator, ChangeDetector, VacancyMatcher, VacancyHasher
    Services/                WatchService — сценарий «добавить компанию по имени»
  Pets.Storage/              SQLite: состояние + outbox в одной БД (нужна общая транзакция)
  Pets.Sources.Greenhouse/   плагин ATS #1: клиент Job Board API, маппер, резолвер слагов
  Pets.Sinks.Telegram/       клиент Bot API, форматтер сообщений, слушатель команд
  Pets.Host/                 DI-сборка, воркеры, конфигурация
tests/Pets.Core.Tests/       юнит-тесты фильтра и детектора изменений
```

## Как работает один цикл

```
watchlist (горячая перезагрузка)
  → IVacancySource.FetchAsync            один борд целиком, IsComplete обязателен
  → VacancyMatcher                       фильтр по заголовку/локации/отделу/дате
  → ChangeDetector                       New / Updated / Closed по контент-хешу
  → IStateStore.CommitAsync              состояние + outbox ОДНОЙ транзакцией
  → OutboxDispatcher → TelegramSink      отдельный процесс, ретраи, backoff
```

Три предохранителя, встроенные в логику:

- **`Closed` только при полном фетче.** Таймаут или частичный ответ не «закрывают» борд.
- **Контент-хеш вместо `updated_at`.** Greenhouse дёргает `updated_at` от косметических правок.
- **Засев (seed).** Первый проход новой записи (и проход после смены фильтра) пишет состояние
  молча — иначе добавление компании вылилось бы в сотню сообщений сразу.

## Запуск

```bash
dotnet build Pets.sln
dotnet test Pets.sln
dotnet run --project src/Pets.Host
```

По умолчанию `Polling:DryRun = true` — уведомления только логируются. Это осознанный дефолт:
сначала посмотрите в логах, сколько бы улетело, потом выключайте.

## Ручная настройка

### Telegram (единственное, что реально нужно руками)

1. `@BotFather` → `/newbot` → получить токен.
2. Если бот в группе — `/setprivacy` → **Disable**.
3. Узнать `chat_id`: написать боту `/start`, затем
   `curl https://api.telegram.org/bot<TOKEN>/getUpdates` → `message.chat.id`.
   Для канала/группы — добавить бота администратором, id будет вида `-100...`.
4. Прописать секреты (в `appsettings.json` токен не кладём):

```bash
dotnet user-secrets --project src/Pets.Host set "Telegram:BotToken" "123456789:AA..."
dotnet user-secrets --project src/Pets.Host set "Telegram:AdminChatIds:0" "<ваш chat_id>"
```

В проде — переменные окружения: `Telegram__BotToken`, `Telegram__AdminChatIds__0`.

5. Указать чат доставки в `watchlist.json` → `DefaultDelivery.ChatId`.

> `AdminChatIds` пустой = команды бота не принимаются ни от кого. Так и задумано: бота может
> найти любой, и без белого списка чужой человек правил бы ваш watchlist.

### Greenhouse

Кредов **не требуется** — Job Board API публичный, все GET открыты. Нужен только `board_token`,
и его находит бот сам по названию компании.

## Команды бота

```
/watch Finom                       найти и добавить компанию
/watch https://x.com/careers       если по имени не нашлось — разобрать карьерную страницу
/list                              что отслеживается
/remove Finom                      снять с мониторинга
```

## Проверено на реальных бордах (04.08.2026)

| Компания | Слаг | Результат |
|---|---|---|
| Nebius | `nebius` | 200, **343 вакансии**, ответ ~237 КБ |
| JetBrains | `jetbrains` | 200, **92 вакансии**, ответ ~82 КБ |
| Finom | — | **404** на `finom` и на вариантах (`finomhq`, `getfinom`, `finom-1`, `finomeu`) |

Про Finom: борда на Greenhouse не нашлось, их карьерная страница отдаёт 403 на автоматический
запрос (защита от ботов), так что ATS определить не удалось. Запись оставлена в `watchlist.json`
выключенной. Это ровно сценарий 4 из плана: компания живёт не на Greenhouse и подключится, когда
появится нужный провайдер. Если знаете их борд — включите запись и поправьте `Board`.

Про объём: 237 КБ на каждый обход Nebius при интервале 10 минут — это ~34 МБ в сутки на одну
компанию. Для трёх бордов неважно, но при переходе к реестру это первое, что придётся считать
(и первый аргумент за проверку ETag).

## Что осознанно не сделано на этапе 1

- **Реестр бордов** (Common Crawl → каталог → тиринг) — отдельный этап.
- **Polly / circuit breaker** — сейчас только таймаут и собственный rate limiter в оркестраторе.
- **`DailyMessageCap`** — поле в настройках есть, применение не реализовано.
- **Дайджесты и персональные фильтры на компанию** через бота — только глобальный фильтр и
  фильтр в JSON.
- **Проверка ETag / If-None-Match** — не подтверждено, поддерживает ли их Job Board API.

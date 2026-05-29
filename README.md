# Fias

REST-сервис над Государственным Адресным Реестром (ГАР) ФНС РФ. Скачивает и применяет
полные / дельта-выгрузки, отдаёт нечёткий поиск, иерархическую навигацию,
адресные строки и справочники через HTTP API.

## Архитектура

Решение разнесено на пять проектов по принципу Clean Architecture; зависимости
направлены строго от Domain наружу.

```
Fias.Domain          POCO-сущности схемы fias.* (без внешних зависимостей)
Fias.Application     Use-case'ы, DTO, контракты (IFiasDbContext, IFiasUpdateJob, …)
Fias.Infrastructure  FiasDbContext (EF Core), pg_trgm-репозиторий, Hangfire-enqueue
Fias.Api             Контроллеры, API-key auth, rate-limit, Swagger, Hangfire Dashboard
Fias.Service.Updater Worker Service: загрузка ZIP с ФНС, COPY-импорт, Hangfire-сервер
```

Updater работает с PostgreSQL напрямую через `Npgsql` + `COPY ... FROM STDIN (BINARY)`
для bulk-импорта. Api использует EF Core (read-only) и провайдер-специфичный
`pg_trgm` запрятан за `IAddressSearchRepository`.

## Стек

- .NET 8, ASP.NET Core, Worker Service
- PostgreSQL 16 + расширение `pg_trgm`
- EF Core 8 + Npgsql.EntityFrameworkCore.PostgreSQL
- Hangfire (PostgreSQL storage) — фоновые задачи и Dashboard
- Swashbuckle (Swagger UI)
- Docker / Docker Compose

## Быстрый старт

```bash
docker compose up -d --build
```

Поднимаются три контейнера: `postgres`, `fias.api` (порт 8080), `fias.service.updater`.
Updater при старте применяет схему `fias.*` и регистрирует recurring-job'ы Hangfire.

- Swagger UI:        http://localhost:8080/swagger
- Hangfire Dashboard: http://localhost:8080/hangfire?token=dev-root-token
- API health:        http://localhost:8080/api/v1/health
- API version:       http://localhost:8080/api/v1/version

### Залить данные

1. **Через Hangfire Dashboard** — открыть Dashboard, в Recurring Jobs нажать
   *Trigger now* на `full-data-import`. Воркер скачает `gar_xml.zip` с ФНС
   и распарсит его (несколько часов на полный дамп).

2. **Через API** (роль Admin):

   ```bash
   curl -X POST http://localhost:8080/api/v1/admin/imports/full \
        -H "X-API-Key: dev-admin-key" -H "Content-Type: application/json" \
        -d '{}'
   ```

   Параметр `{"localZipPath":"/fias/import/gar_xml.zip"}` подтянет
   локальный файл вместо скачивания.

3. **Положить файл в volume** — `./fias-import/gar_xml.zip` смонтирован в
   `/fias/import/` внутри воркера. Если задача запущена без `localZipPath`,
   оркестратор сам подхватит файл из этой папки.

После full-import дельты накатываются автоматически по расписанию `Cron.Daily(3)`.

## API v1

Все endpoints — `application/json`. Заголовок `X-API-Key` обязателен везде,
кроме `/version` и `/health`. Rate-limit: 60 req/min без ключа, 600 — с ключом.

### Адреса
| Метод | Эндпоинт | Назначение |
|---|---|---|
| GET  | `/api/v1/addresses/{objectId}` | Адресная строка и иерархия по OBJECTID |
| GET  | `/api/v1/addresses/by-guid/{guid}` | То же по OBJECTGUID |
| GET  | `/api/v1/addresses/search` | Нечёткий поиск (`q`, `limit`, `threshold`, `level`, `parentId`) |
| GET  | `/api/v1/addresses/{objectId}/children` | Дочерние элементы (`level`, `name`, пагинация) |
| GET  | `/api/v1/addresses/{objectId}/parents` | Путь к корню |
| POST | `/api/v1/addresses/batch` | Пачка OBJECTID (до 1000) |
| POST | `/api/v1/addresses/parse` | Разбор свободной адресной строки |
| POST | `/api/v1/addresses/parse-batch` | До 1000 свободных строк |

### Иерархия
| Метод | Эндпоинт | Назначение |
|---|---|---|
| GET | `/api/v1/regions` | Все субъекты РФ |
| GET | `/api/v1/streets/{objectId}/houses` | Дома на улице |
| GET | `/api/v1/houses/{objectId}/apartments` | Помещения в доме |
| GET | `/api/v1/apartments/{objectId}/rooms` | Комнаты в помещении |

### Справочники
| Метод | Эндпоинт | Назначение |
|---|---|---|
| GET | `/api/v1/levels` | OBJECT_LEVELS |
| GET | `/api/v1/types/address-objects` | Типы адресообразующих (фильтр `level`) |
| GET | `/api/v1/types/houses` | Типы зданий |
| GET | `/api/v1/types/apartments` | Типы помещений |
| GET | `/api/v1/types/rooms` | Типы комнат |

### Сервисные
| Метод | Эндпоинт | Авторизация | Назначение |
|---|---|---|---|
| GET | `/api/v1/health` | anonymous | Liveness |
| GET | `/api/v1/version` | anonymous | Версия выгрузки, дата применения |
| GET | `/api/v1/stats` | Public | Кол-во записей по таблицам |

### Админка (роль Admin)
| Метод | Эндпоинт | Назначение |
|---|---|---|
| POST | `/api/v1/admin/imports/full` | Запустить full-import (опционально `localZipPath`) |
| POST | `/api/v1/admin/imports/delta` | Запустить delta вне расписания |

## Конфигурация

Параметры в `appsettings.json` обоих проектов; в Docker переопределяются через
`environment:` в `compose.yaml`.

### Fias.Api
```jsonc
{
  "ConnectionStrings": { "Default": "Host=...;Database=fias;Username=...;Password=..." },
  "ApiKeys": {
    "Keys": [
      { "Key": "dev-public-key", "Role": "Public", "Owner": "dev" },
      { "Key": "dev-admin-key",  "Role": "Admin",  "Owner": "dev" }
    ]
  },
  "Hangfire": {
    "RootToken": "dev-root-token"     // токен доступа к /hangfire
  }
}
```

### Fias.Service.Updater
```jsonc
{
  "ConnectionStrings": { "Default": "..." },
  "Fias": {
    "FnsServiceUrl":          "https://fias.nalog.ru/WebServices/Public",
    "ActualDownloadsBaseUrl": "https://fias.nalog.ru/Public/Downloads/Actual",
    "ImportDirectory":        "/fias/import",
    "FullArchiveFileName":    "gar_xml.zip",
    "DeltaArchiveFileName":   "gar_delta_xml.zip",
    "CopyBatchSize":          5000,
    "Regions":                [ 74 ]   // двузначные коды; [] — все регионы
  }
}
```

## Аутентификация

### API
Все защищённые эндпоинты требуют заголовок:
```
X-API-Key: <ключ из appsettings.json:ApiKeys.Keys>
```
Роль определяется по полю `Role` ключа: `Public` для обычных интеграторов,
`Admin` для запуска импорта.

### Hangfire Dashboard
Дашборд защищён отдельным root-токеном (браузер не шлёт `X-API-Key`).
Первый визит — с query-параметром:
```
http://<host>/hangfire?token=<Hangfire:RootToken>
```
После этого фильтр выписывает HttpOnly cookie `hf_token` на 7 дней.

## Структура БД

Updater создаёт схему `fias.*` с 13 таблицами (адресообразующие, здания,
помещения, комнаты, иерархии, справочники типов, параметры) + служебная
`fias.import_state` с текущей версией выгрузки. Подробности — в
`Fias.Service.Updater/Services/Schema/Migrator.cs`.

Индексы:
- B-tree на `objectid` всех основных таблиц.
- B-tree на `parentobjid` иерархий.
- **GIN(name gin_trgm_ops)** на `addressobjects` (фильтр `isactive=1 AND isactual=1`)
  — для нечёткого поиска.

## Известные ограничения

- ParseService — baseline (токенизация + similarity). Для production-grade
  нужен полноценный NLP-парсер (DaData и аналоги).
- Batch и search строят адресные строки последовательно (N+1 запросов на каждый
  результат). Для top-N сценариев скорости хватает.
- ADDHOUSE_TYPES (доп. типы строений) не импортируется — в адресной строке
  дома идут с числовыми кодами для `ADDTYPE1/2`.
- Все запросы по умолчанию работают с «активными на сегодня» записями
  (`ISACTUAL=1 AND ISACTIVE=1`); исторические срезы не поддержаны.
- API-keys в `appsettings.json` — пригодно для dev; в production выносить
  в secrets-store / KMS.

## Разработка

Локальная сборка:
```bash
dotnet build Fias.sln
```

Запуск без Docker (нужен локальный Postgres на 5432 с БД `fias`):
```bash
dotnet run --project Fias.Api
dotnet run --project Fias.Service.Updater
```

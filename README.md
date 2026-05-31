# Fias

REST-сервис над Государственным Адресным Реестром (ГАР) ФНС РФ. Скачивает и применяет
полные / дельта-выгрузки, отдаёт нечёткий гибридный поиск, иерархическую навигацию,
адресные строки и справочники через HTTP API. По смыслу — self-hosted аналог DaData
поверх собственной копии ГАР.

## Архитектура

Решение разнесено по принципу Clean Architecture; зависимости направлены строго от
Domain наружу. Семь проектов:

```
Fias.Domain          POCO-сущности сырой схемы fias.* (без внешних зависимостей)
Fias.Application     Use-case'ы, DTO, нормализация запроса, контракты (интерфейсы)
Fias.Infrastructure  Dapper-репозиторий поверх NpgsqlDataSource, Hangfire-enqueue
Fias.Api             Контроллеры, API-key auth, rate-limit, Swagger, Hangfire Dashboard
Fias.Service.Updater Worker Service: загрузка ZIP с ФНС, COPY-импорт, сборка search.*, Hangfire-сервер
Fias.Eval            Харнесс качества поиска (recall@k / MRR по бакетам) — офлайн-утилита
Fias.Tests           Юнит-тесты нормализатора + интеграционные тесты поиска
```

Чтение **полностью на Dapper** (raw SQL) поверх единого `NpgsqlDataSource`; EF Core в
ядре чтения не используется. Поиск идёт не по сырым `fias.*`, а по **денормализованной
проекции `search.*`**, которую Updater пересобирает после каждого импорта (см. ниже).

Updater работает с PostgreSQL напрямую через `Npgsql` + `COPY ... FROM STDIN (BINARY)`
для bulk-импорта сырых данных.

## Стек

- .NET 8, ASP.NET Core, Worker Service
- PostgreSQL 16 + расширение `pg_trgm`, полнотекстовый поиск (`tsvector`/`tsquery`, конфиг `russian`)
- **Dapper** + Npgsql (read-path), `COPY (BINARY)` (write-path)
- Hangfire (PostgreSQL storage) — фоновые задачи и Dashboard
- Swashbuckle (Swagger UI)
- Docker / Docker Compose

## Быстрый старт

```bash
cp .env.example .env        # секреты и порты
docker compose up -d --build
```

Поднимаются три контейнера: `postgres`, `fias.api` (порт `${API_PORT:-8080}`),
`fias.service.updater`. Updater при старте применяет схему `fias.*` и регистрирует
recurring-job'ы Hangfire.

- Swagger UI:         http://localhost:8080/swagger
- Hangfire Dashboard: http://localhost:8080/hangfire?token=dev-root-token
- Liveness:           http://localhost:8080/health/live
- Readiness:          http://localhost:8080/health/ready
- Версия выгрузки:    http://localhost:8080/api/v1/version

### Залить данные

1. **Через Hangfire Dashboard** — в Recurring Jobs нажать *Trigger now* на
   `full-data-import`. Воркер скачает `gar_xml.zip` с ФНС и распарсит его
   (несколько часов на полный дамп).

2. **Через API** (роль Admin):

   ```bash
   curl -X POST http://localhost:8080/api/v1/admin/imports/full \
        -H "X-API-Key: dev-admin-key" -H "Content-Type: application/json" \
        -d '{}'
   ```

   Параметр `{"localZipPath":"/fias/import/gar_xml.zip"}` подтянет локальный файл
   вместо скачивания.

3. **Положить файл в volume** — `./fias-import/gar_xml.zip` смонтирован в `/fias/import/`
   внутри воркера; оркестратор сам подхватит его, если задача запущена без `localZipPath`.

После импорта сырых данных Updater **автоматически пересобирает поисковую проекцию
`search.*`** (`SearchProjectionBuilder`), и только тогда readiness-проба API становится
зелёной. Дельты накатываются по расписанию `Cron.Daily(3)`.

## API v1

Все endpoints — `application/json` (snake_case, null-поля опускаются). Заголовок
`X-API-Key` обязателен везде, кроме `/version`, `/health`, `/health/live`,
`/health/ready`. Rate-limit: 60 req/min без ключа, 600 — с ключом.

### Адреса (`/api/v1/addresses`)
| Метод | Эндпоинт | Назначение |
|---|---|---|
| GET | `/{objectId}` | Адресная строка и иерархия по OBJECTID |
| GET | `/by-guid/{guid}` | То же по OBJECTGUID |
| GET | `/search?query=&limit=` | Нечёткий гибридный поиск (компактная выдача + структурный блок `data`) |
| GET | `/search/full?query=&limit=` | Поиск с полной структурой ГАР по каждому объекту |
| GET | `/{objectId}/children` | Дочерние элементы (`level`, `name`, `page`, `page_size`) |
| GET | `/{objectId}/parents` | Путь к корню (breadcrumbs) |

### Подсказки и стандартизация
| Метод | Эндпоинт | Назначение |
|---|---|---|
| GET  | `/api/v1/suggest/address?query=&limit=` | Саджест в формате, близком к DaData |
| GET  | `/api/v1/suggest/address/{fiasId}` | Резолв одного объекта по FIAS GUID |
| POST | `/api/v1/clean/address` | Стандартизация строки (тело `{"query":"…"}`): лучший разбор + `qc`/`confidence` |

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
| GET | `/api/v1/version` | anonymous | Версия выгрузки, дата применения |
| GET | `/api/v1/health` | anonymous | Простой liveness |
| GET | `/health/live` | anonymous | Liveness-проба (процесс жив) |
| GET | `/health/ready` | anonymous | Readiness (БД + проекция `search.*` готовы) |
| GET | `/api/v1/stats` | Public | Кол-во записей по таблицам |

### Админка (роль Admin) — `/api/v1/admin/imports`
| Метод | Эндпоинт | Назначение |
|---|---|---|
| POST | `/full` | Запустить full-import (опционально `localZipPath`) |
| POST | `/delta` | Запустить delta вне расписания |

## Поиск

Свободная строка проходит нормализацию (`AddressNormalizer`) и уходит в гибридный
репозиторий: **полнотекстовый поиск (FTS) + триграммы `pg_trgm`**, объединённые через
**Reciprocal Rank Fusion (RRF)**, с поуровневым сужением по дереву. Понимает сокращения
(`нск`, `ул`, `д 5 к1`), опечатки, обратный порядок «улица город» и сокращённые имена
улиц по инициалу (`Б.Хмельницкого` → `Богдана Хмельницкого`).

Подробный разбор пайплайна — в [docs/search.md](docs/search.md). Эталонный SQL —
[docs/sql/hybrid_search.sql](docs/sql/hybrid_search.sql).

### Оценка качества (Fias.Eval)

Харнесс генерирует «золотой набор» прямо из проекции `search.*` (эталон = `object_guid`),
гоняет его через боевой `IAddressSearchService` и печатает `recall@k` / `MRR` по бакетам
(`clean`, `reorder`, `typo`, `abbrev`, `house`, `nocity`, `type`).

```bash
# локально (нужен Postgres с наполненной проекцией)
dotnet run --project Fias.Eval -- --region-count 5 --out eval-misses.csv

# в сети docker compose (профиль eval)
docker compose run --rm fias.eval --region-count 5 --out /out/eval-misses.csv

# ad-hoc разбор одного запроса
dotnet run --project Fias.Eval -- --query "Б.Хмельницкого 2"
```

## Конфигурация

Параметры в `appsettings.json` обоих сервисов; в Docker переопределяются через
`environment:` в `docker-compose.yml` (значения берутся из `.env`).

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
  "Hangfire": { "RootToken": "dev-root-token" }   // токен доступа к /hangfire
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
Защищённые эндпоинты требуют заголовок `X-API-Key: <ключ из ApiKeys.Keys>`. Роль
определяется полем `Role`: `Public` для интеграторов, `Admin` для запуска импорта.

### Hangfire Dashboard
Дашборд защищён отдельным root-токеном (браузер не шлёт `X-API-Key`). Первый визит — с
query-параметром `?token=<Hangfire:RootToken>`; затем фильтр выписывает HttpOnly cookie
`hf_token` на 7 дней.

## Структура БД

Две схемы, разделение «источник правды / поисковый слой»:

### `fias.*` — сырой ГАР (system of record)
Updater создаёт ~13 таблиц (адресообразующие объекты, здания, помещения, комнаты,
адм./мун. иерархии, справочники типов, параметры) + служебная `fias.import_state` с
текущей версией выгрузки. Эти таблицы — точная копия ГАР; **их не удаляют и не правят
вручную**. Подробности схемы — в `Fias.Service.Updater/Services/Schema/Migrator.cs`.

### `search.*` — денормализованная проекция для поиска
Строится `SearchProjectionBuilder` из `fias.*` после импорта. Три таблицы:
`address_objects`, `houses`, `address_object_types`. В каждой строке уже лежат полный
`path`, `full_name`, структурный сплит (`region`/`area`/`city`/`settlement`/`street`),
реквизиты (индекс/ОКАТО/ОКТМО/ИФНС/КЛАДР) и `house_count` — поиск обходится без JOIN'ов.

Индексы проекции:
- `GIN(name_tsv)` — полнотекстовый поиск (`@@`);
- `GiST(name gist_trgm_ops)` — нечёткий поиск и KNN по триграммам;
- `GIN(house_num gin_trgm_ops)` — номера домов;
- B-tree на `object_id`, `parent_object_id`, `region_object_id`, `object_guid` и
  `path text_pattern_ops` (сужение по поддереву через `LIKE 'prefix.%'`).

Пересборка **zero-downtime**: всё строится в `*_stage`-таблицах, индексы — по уже
наполненным staging, в конце — атомарный swap в одной транзакции (DDL в Postgres
транзакционный).

## Известные ограничения

- Поиск опирается на эвристический разбор строки + двухфазное SQL-сужение; это не
  полноценный NLP-парсер, но качество измеряется регрессионно (`Fias.Eval`).
- Построение полной адресной строки для `search/full` и навигации идёт последовательно
  (N+1 запросов на результат); для top-N выдачи скорости хватает.
- `ADDHOUSE_TYPES` (доп. типы строений) не импортируется — в адресной строке доп. части
  дома идут с числовыми кодами `ADDTYPE1/2`.
- Все запросы работают со «активными на сегодня» записями (`ISACTUAL=1 AND ISACTIVE=1`);
  исторические срезы не поддержаны.
- API-ключи в `appsettings.json` — для dev; в production выносить в secrets-store / KMS.
- Кейс `nocity` (улица+дом без города) имеет пониженный recall — несколько одноимённых
  улиц по разным городам неоднозначны без контейнера.

## Разработка

```bash
dotnet build Fias.sln          # сборка
dotnet test  Fias.sln          # тесты (интеграционные требуют Postgres)
```

Запуск без Docker (нужен локальный Postgres с БД `fias`):
```bash
dotnet run --project Fias.Api
dotnet run --project Fias.Service.Updater
```

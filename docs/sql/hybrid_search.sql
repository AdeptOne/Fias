-- ============================================================================
--  Гибридный поиск по ГАР: схема и индексы (FTS + pg_trgm) — СПРАВОЧНИК ТЕХНИКИ.
--
--  ВАЖНО: живой поиск в приложении идёт по ДЕНОРМАЛИЗОВАННОМУ слою search.*
--  (schema search: address_objects/houses/address_object_types). Его DDL — в
--  Fias.Service.Updater/.../Migrator.cs, а наполнение — в SearchProjectionBuilder.cs
--  (запускается после импорта). Там же GIN(tsvector) и GiST(gist_trgm_ops).
--
--  Этот файл оставлен как пояснение приёмов FTS+тригоамм и как одноразовый скрипт
--  для опытов на сырых fias.* таблицах. Все операции идемпотентны.
-- ============================================================================

CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- ----------------------------------------------------------------------------
--  Справочно: ключевые типы колонок (как в реальной схеме fias.*).
--    objectid   bigint   — числовой ID объекта (НЕ uuid: компактнее, быстрее в индексах/JOIN)
--    objectguid uuid     — глобальный идентификатор ФИАС
--    name       text     — наименование
--    level      integer  — уровень адресообразующего элемента
--    name_tsv   tsvector — полнотекстовый вектор (ниже)
--
--  CREATE TABLE здесь НЕ дублируем (его владелец — Migrator); показываем только
--  добавления, нужные для гибридного поиска.
-- ----------------------------------------------------------------------------

-- 1) FTS-колонка. GENERATED ALWAYS ... STORED: значение вычисляется при вставке/обновлении
--    строки и физически хранится. Для read-only ГАР это оптимально — считается один раз
--    при импорте, поиск только читает. 'russian' даёт стемминг (улица→улиц, лесная→лесн).
ALTER TABLE fias.addressobjects
    ADD COLUMN IF NOT EXISTS name_tsv tsvector
    GENERATED ALWAYS AS (to_tsvector('russian', coalesce(name, ''))) STORED;

-- 2) GIN по tsvector — основной индекс полнотекстового поиска (оператор @@).
CREATE INDEX IF NOT EXISTS ix_addressobjects_name_tsv
    ON fias.addressobjects USING gin (name_tsv)
    WHERE isactive = true AND isactual = true;

-- 3) Триграммный индекс по name — фолбэк на опечатки (операторы % и similarity()).
--
--    GIN (gin_trgm_ops) vs GiST (gist_trgm_ops) — что выбрать для ГАР:
--      • GIN  — быстрее на чтение, точный (не lossy). Минусы: дольше строится и больше
--               на диске, дороже обновлять. Для READ-ONLY базы, которую заливают пакетно
--               и потом только читают, минусы не важны, а выигрыш в скорости поиска — да.
--               => для ГАР выбираем GIN.
--      • GiST — компактнее и быстрее строится, и УМЕЕТ KNN: `ORDER BY name <-> @term`
--               (top-N по близости с поддержкой индекса). Брать стоит, только если ранжируете
--               преимущественно по триграммной дистанции и нужен индексный KNN.
--    Мы комбинируем FTS+триграммы и пере-ранжируем результат в SQL, фильтр-сторона выигрывает
--    от GIN — поэтому GIN.
CREATE INDEX IF NOT EXISTS ix_addressobjects_name_trgm
    ON fias.addressobjects USING gin (name gin_trgm_ops)
    WHERE isactive = true AND isactual = true;

-- 4) Триграммный индекс по номеру дома — для нечёткого поиска домов в поддереве.
CREATE INDEX IF NOT EXISTS ix_houses_housenum_trgm
    ON fias.houses USING gin (housenum gin_trgm_ops)
    WHERE isactive = true AND isactual = true;

-- 5) Префиксный индекс по пути иерархии — обслуживает сужение `path LIKE 'prefix.%'`.
--    text_pattern_ops делает LIKE-префикс индексируемым независимо от локали кластера.
CREATE INDEX IF NOT EXISTS ix_adm_hierarchy_path
    ON fias.adm_hierarchy (path text_pattern_ops)
    WHERE isactive = true;

-- ----------------------------------------------------------------------------
--  После массовой заливки/перестройки индексов обязательно обновить статистику,
--  иначе планировщик может промахнуться с BitmapOr(FTS, trgm).
-- ----------------------------------------------------------------------------
ANALYZE fias.addressobjects;
ANALYZE fias.houses;
ANALYZE fias.adm_hierarchy;

-- ============================================================================
--  Пример итогового гибридного запроса (как его строит репозиторий):
--
--    SET LOCAL pg_trgm.similarity_threshold = 0.3;
--    WITH q AS (SELECT plainto_tsquery('russian', :term) AS tsq)
--    SELECT a.objectid, a.name, a.level,
--           ts_rank(a.name_tsv, q.tsq)  AS fts_rank,
--           similarity(a.name, :term)   AS trgm_sim,
--           ( CASE WHEN a.name_tsv @@ q.tsq THEN 1.0 + ts_rank(a.name_tsv, q.tsq) ELSE 0 END
--             + similarity(a.name, :term) * 0.3
--             + CASE a.level WHEN 4 THEN 0.15 WHEN 5 THEN 0.15 ELSE 0 END ) AS score
--    FROM fias.addressobjects a CROSS JOIN q
--    WHERE a.isactive AND a.isactual
--      AND (a.name_tsv @@ q.tsq OR a.name % :term)   -- FTS ИЛИ триграммы (оба по индексу)
--    ORDER BY score DESC
--    LIMIT :limit;
-- ============================================================================

using Dapper;
using Fias.Application.Search;
using Fias.Application.Services;
using Fias.Infrastructure.Persistence;
using Fias.Service.Updater.Services.Progress;
using Fias.Service.Updater.Services.Schema;
using Fias.Service.Updater.Services.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Fias.Tests.Integration;

/// <summary>
/// Сквозной тест SQL-конвейера: схема (Migrator) → сид сырых fias.* → денормализация
/// (SearchProjectionBuilder) → гибридный поиск (AddressSearchRepository, RRF + двухфазное сужение).
/// Требует Docker (Testcontainers). Категория Integration — фильтруется из юнит-прогона.
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgresCollection.Name)]
public sealed class SearchPipelineTests(PostgresFixture fixture)
{
    private static readonly Guid StreetGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private async Task BuildPipelineAsync()
    {
        var migrator = new Migrator(fixture.Factory, NullLogger<Migrator>.Instance);
        await migrator.EnsureSchemaAsync(default);
        await migrator.TruncateAllAsync(default); // фикстура одна на коллекцию — сид должен быть чистым

        await using (var conn = await fixture.Factory.OpenAsync(default))
        {
            await conn.ExecuteAsync(Seed, new { streetGuid = StreetGuid });
        }

        var projection = new SearchProjectionBuilder(fixture.Factory, NullLogger<SearchProjectionBuilder>.Instance);
        await projection.RebuildAsync(new NullProgress(), default);
    }

    [Fact]
    public async Task Search_By_Street_Name_Finds_Street()
    {
        await BuildPipelineAsync();
        var repo = new AddressSearchRepository(NpgsqlDataSource.Create(fixture.ConnectionString));

        var hits = await repo.SearchAsync(Query("ленина", regionOrCity: "ленина"), default);

        Assert.Contains(hits, h => h.ObjectId == 3 && h.Name == "Ленина");
    }

    [Fact]
    public async Task Search_City_Street_House_Resolves_House_In_Street()
    {
        await BuildPipelineAsync();
        var repo = new AddressSearchRepository(NpgsqlDataSource.Create(fixture.ConnectionString));

        var query = Query("новосибирск ленина 12", regionOrCity: "новосибирск", street: "ленина", house: "12", houseNum: "12");
        var hits = await repo.SearchAsync(query, default);

        Assert.Contains(hits, h => h.ObjectId == 4); // дом 12 на ул. Ленина
    }

    [Fact]
    public async Task Builder_Reads_DateOnly_Columns_And_Builds_Address()
    {
        await BuildPipelineAsync();
        var builder = new AddressBuilderService(
            new SqlConnectionFactory(NpgsqlDataSource.Create(fixture.ConnectionString)));

        // Билдер делает SELECT * по fias.* с date-колонками (updatedate) → проверяет DateOnly-маппинг.
        var dto = await builder.BuildByObjectIdAsync(3, default);

        Assert.NotNull(dto);
        Assert.Contains("Ленина", dto!.FullName);
    }

    [Fact]
    public async Task Duplicate_Street_Name_Ranks_Populous_First()
    {
        await BuildPipelineAsync();
        var repo = new AddressSearchRepository(NpgsqlDataSource.Create(fixture.ConnectionString));

        // Два «Ленина» в одном городе: у объекта 3 есть дом, у 5 — нет. При равном RRF
        // тай-брейк по house_count должен поставить «живую» улицу (3) первой.
        var hits = await repo.SearchAsync(
            Query("новосибирск ленина", regionOrCity: "новосибирск", street: "ленина"), default);

        var streets = hits.Where(h => h.ObjectId is 3 or 5).ToList();
        Assert.Equal(3, streets[0].ObjectId);
    }

    [Fact]
    public async Task GetByGuid_Returns_Object_With_Denormalized_Data()
    {
        await BuildPipelineAsync();
        var repo = new AddressSearchRepository(NpgsqlDataSource.Create(fixture.ConnectionString));

        var hit = await repo.GetByGuidAsync(StreetGuid, default);

        Assert.NotNull(hit);
        Assert.Equal(3, hit!.ObjectId);
        Assert.Equal("Новосибирская", hit.Region);
        Assert.Equal("Новосибирск", hit.City);
    }

    private static ParsedAddressQuery Query(
        string normalized, string? regionOrCity = null, string? street = null, string? house = null, string? houseNum = null) =>
        new()
        {
            Raw = normalized,
            Normalized = normalized,
            Tokens = normalized.Split(' '),
            RegionOrCity = regionOrCity,
            Street = street,
            House = house,
            HouseNum = houseNum,
            Limit = 10,
            SimilarityThreshold = 0.3,
        };

    // Граф: регион(1) → город(2) → улица «Ленина»(3, есть дом) и дубль «Ленина»(5, без домов) → дом(4).
    private const string Seed = """
        INSERT INTO fias.reestr_objects (objectid, levelid, isactive) VALUES
            (1, 1, true), (2, 5, true), (3, 8, true), (4, 10, true), (5, 8, true);

        -- updatedate заполнен намеренно: воспроизводит чтение date-колонки билдером
        -- (Npgsql отдаёт date как DateTime → нужен DateOnly TypeHandler).
        INSERT INTO fias.addressobjects (id, objectid, objectguid, name, typename, level, updatedate, isactual, isactive) VALUES
            (1, 1, NULL,         'Новосибирская', 'обл', 1, DATE '2026-05-22', true, true),
            (2, 2, NULL,         'Новосибирск',   'г',   5, DATE '2026-05-22', true, true),
            (3, 3, @streetGuid,  'Ленина',        'ул',  8, DATE '2026-05-22', true, true),
            (5, 5, NULL,         'Ленина',        'ул',  8, DATE '2026-05-22', true, true);

        INSERT INTO fias.adm_hierarchy (id, objectid, parentobjid, path, isactive) VALUES
            (1, 1, NULL, '1',       true),
            (2, 2, 1,    '1.2',     true),
            (3, 3, 2,    '1.2.3',   true),
            (4, 4, 3,    '1.2.3.4', true),
            (5, 5, 2,    '1.2.5',   true);

        INSERT INTO fias.houses (id, objectid, housenum, isactual, isactive) VALUES
            (4, 4, '12', true, true);
        """;

    private sealed class NullProgress : IProgressSink
    {
        public void WriteLine(string message) { }
        public IJobProgressBar StartProgressBar(string title) => new NullBar();
        private sealed class NullBar : IJobProgressBar { public void SetValue(double percent) { } }
    }
}

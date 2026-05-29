using Dapper;
using Fias.Application.Search;
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

    // Минимальный граф: регион(1) → город(2) → улица(3) → дом(4).
    private const string Seed = """
        INSERT INTO fias.reestr_objects (objectid, levelid, isactive) VALUES
            (1, 1, true), (2, 5, true), (3, 8, true), (4, 10, true);

        INSERT INTO fias.addressobjects (id, objectid, objectguid, name, typename, level, isactual, isactive) VALUES
            (1, 1, NULL,         'Новосибирская', 'обл', 1, true, true),
            (2, 2, NULL,         'Новосибирск',   'г',   5, true, true),
            (3, 3, @streetGuid,  'Ленина',        'ул',  8, true, true);

        INSERT INTO fias.adm_hierarchy (id, objectid, parentobjid, path, isactive) VALUES
            (1, 1, NULL, '1',     true),
            (2, 2, 1,    '1.2',   true),
            (3, 3, 2,    '1.2.3', true),
            (4, 4, 3,    '1.2.3.4', true);

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

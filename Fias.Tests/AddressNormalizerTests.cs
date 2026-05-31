using Fias.Application.Search;
using Xunit;

namespace Fias.Tests;

public class AddressNormalizerTests
{
    private readonly AddressNormalizer _sut = new();

    // ---------- Разбор «регион/город · улица · дом(+корпус)» ----------

    [Theory]
    // запрос                              регион/город        улица          house  num    building
    [InlineData("нск ленина 12 к1",        "новосибирск",      "ленина",      "12к1", "12",  "1")]
    [InlineData("москва тверская 7",       "москва",           "тверская",    "7",    "7",   null)]
    [InlineData("спб невский проспект 1",  "санкт-петербург",  "невский",     "1",    "1",   null)]
    [InlineData("г москва ул тверская д 7 к 2", "москва",      "тверская",    "7к2",  "7",   "2")]
    [InlineData("новосибирск красный проспект 3", "новосибирск", "красный",   "3",    "3",   null)]
    public void Parse_SplitsCityStreetHouse(
        string query, string city, string street, string house, string houseNum, string? building)
    {
        var r = _sut.Parse(query);

        Assert.Equal(city, r.RegionOrCity);
        Assert.Equal(street, r.Street);
        Assert.Equal(house, r.House);
        Assert.Equal(houseNum, r.HouseNum);
        Assert.Equal(building, r.Building);
    }

    [Theory]
    [InlineData("новосибирск")]
    [InlineData("нск")]
    public void Parse_SingleToken_NoStreetNoHouse(string query)
    {
        var r = _sut.Parse(query);

        Assert.Equal("новосибирск", r.RegionOrCity);
        Assert.Null(r.Street);
        Assert.Null(r.House);
    }

    // ---------- Инициал имени улицы: «Б.Хмельницкого» → улица, не город ----------

    [Theory]
    [InlineData("б хмельницкого 2",   "б хмельницкого", "2")]
    [InlineData("Б.Хмельницкого 2",   "б хмельницкого", "2")] // точка → пробел, регистр
    [InlineData("к маркса 10",        "к маркса",       "10")]
    public void Parse_LeadingInitial_StaysInStreet(string query, string street, string houseNum)
    {
        var r = _sut.Parse(query);

        // Одиночная буква — инициал имени, а НЕ контейнер: город не вычленяется.
        Assert.Null(r.RegionOrCity);
        Assert.Equal(street, r.Street);
        Assert.Equal(houseNum, r.HouseNum);
    }

    [Fact]
    public void Parse_InitialWithCity_KeepsCityAndStreet()
    {
        var r = _sut.Parse("москва б хмельницкого 2");

        Assert.Equal("москва", r.RegionOrCity);
        Assert.Equal("б хмельницкого", r.Street);
        Assert.Equal("2", r.HouseNum);
    }

    // ---------- Разбор номера дома: база + корпус/строение ----------

    [Theory]
    [InlineData("ленина 12",      "12",   null)]
    [InlineData("ленина 12а",     "12а",  null)]
    [InlineData("ленина 3/1",     "3/1",  null)]
    [InlineData("ленина 12к1",    "12",   "1")]
    [InlineData("ленина д 5",     "5",    null)]
    [InlineData("ленина д 5 к 1", "5",    "1")]
    [InlineData("ленина 5 стр 2", "5",    "2")]
    public void Parse_HouseNumberForms(string query, string expectedNum, string? expectedBuilding)
    {
        var r = _sut.Parse(query);

        Assert.Equal(expectedNum, r.HouseNum);
        Assert.Equal(expectedBuilding, r.Building);
    }

    [Fact]
    public void Parse_NoDigits_NoHouse()
    {
        var r = _sut.Parse("москва тверская");

        Assert.Null(r.House);
        Assert.Null(r.HouseNum);
        Assert.Null(r.Building);
    }

    // ---------- Очистка/нормализация строки ----------

    [Theory]
    [InlineData("  Москва,  Тверская  ул. ", "москва тверская улица")] // регистр, пунктуация, схлоп, ул→улица
    [InlineData("Орёл", "орел")]                                       // ё → е
    [InlineData("САНКТ-ПЕТЕРБУРГ", "санкт-петербург")]                 // дефис сохраняется
    public void Parse_Normalizes(string query, string expectedNormalized)
    {
        var r = _sut.Parse(query);

        Assert.Equal(expectedNormalized, r.Normalized);
    }

    [Fact]
    public void Parse_ExpandsCitySlang_IntoRegionOrCity()
    {
        var r = _sut.Parse("спб");

        Assert.Equal("санкт-петербург", r.RegionOrCity);
        Assert.Equal("санкт-петербург", r.Normalized);
    }

    [Fact]
    public void Parse_DropsTypeMarkers_FromNames()
    {
        // «ул» и «г» — типовые маркеры: в Street/RegionOrCity попадают только имена.
        var r = _sut.Parse("г москва ул ленина");

        Assert.Equal("москва", r.RegionOrCity);
        Assert.Equal("ленина", r.Street);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",.;")]
    public void Parse_EmptyOrPunctuationOnly_HasNoContent(string query)
    {
        var r = _sut.Parse(query);

        Assert.Equal(string.Empty, r.Normalized);
        Assert.Null(r.RegionOrCity);
        Assert.Null(r.Street);
        Assert.Null(r.House);
    }
}

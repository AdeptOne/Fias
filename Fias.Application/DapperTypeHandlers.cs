using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using Dapper;

namespace Fias.Application;

/// <summary>
/// Глобальная настройка Dapper. Регистрируется автоматически при загрузке сборки Application
/// (ModuleInitializer), поэтому действует и в API, и в тестах без явного вызова.
///
/// Зачем: Npgsql читает колонки типа <c>date</c> как <see cref="DateTime"/>, а доменные сущности
/// (AddressObject, House, FiasParam, …) используют <see cref="DateOnly"/>. EF приводил типы сам;
/// после перехода на Dapper нужен явный TypeHandler, иначе маппинг падает с
/// "Invalid cast from System.DateTime to System.DateOnly".
/// </summary>
internal static class DapperTypeHandlers
{
    [ModuleInitializer]
    internal static void Register()
    {
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            string s => DateOnly.Parse(s, CultureInfo.InvariantCulture),
            _ => DateOnly.FromDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture)),
        };

        // Npgsql принимает DateOnly как параметр типа date.
        public override void SetValue(IDbDataParameter parameter, DateOnly value) => parameter.Value = value;
    }
}

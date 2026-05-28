using System.Globalization;
using System.Xml;

namespace Fias.Service.Updater.Services.Importing;

/// <summary>
/// Утилиты для чтения типизированных значений из атрибутов XML-элементов ФИАС.
/// Все «отсутствующие» значения превращаются в null — Npgsql пишет их как NULL.
/// </summary>
internal static class XmlHelpers
{
    public static string? GetString(XmlReader r, string name)
    {
        var v = r.GetAttribute(name);
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public static int? GetInt(XmlReader r, string name)
    {
        var v = r.GetAttribute(name);
        return string.IsNullOrEmpty(v) ? null : int.Parse(v, CultureInfo.InvariantCulture);
    }

    public static long? GetLong(XmlReader r, string name)
    {
        var v = r.GetAttribute(name);
        return string.IsNullOrEmpty(v) ? null : long.Parse(v, CultureInfo.InvariantCulture);
    }

    public static short? GetShort(XmlReader r, string name)
    {
        var v = r.GetAttribute(name);
        return string.IsNullOrEmpty(v) ? null : short.Parse(v, CultureInfo.InvariantCulture);
    }

    public static DateOnly? GetDate(XmlReader r, string name)
    {
        var v = r.GetAttribute(name);
        return string.IsNullOrEmpty(v) ? null : DateOnly.Parse(v, CultureInfo.InvariantCulture);
    }

    public static Guid? GetGuid(XmlReader r, string name)
    {
        var v = r.GetAttribute(name);
        return string.IsNullOrEmpty(v) ? null : Guid.Parse(v);
    }
}

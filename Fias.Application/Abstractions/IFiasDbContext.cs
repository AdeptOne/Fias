using Fias.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fias.Application.Abstractions;

/// <summary>
/// Абстракция БД для use case'ов. Реализуется в Infrastructure (FiasDbContext).
/// Намеренно отдаём DbSet'ы — для CRUD/чтения это даёт LINQ-выражения без репозиторного boilerplate.
/// Специфичные SQL-операции (например, pg_trgm similarity) живут в отдельных репозиториях.
/// </summary>
public interface IFiasDbContext
{
    DbSet<ReestrObject> ReestrObjects { get; }
    DbSet<AddressObject> AddressObjects { get; }
    DbSet<House> Houses { get; }
    DbSet<Apartment> Apartments { get; }
    DbSet<Room> Rooms { get; }
    DbSet<AdmHierarchy> AdmHierarchy { get; }
    DbSet<MunHierarchy> MunHierarchy { get; }
    DbSet<AddressObjectType> AddressObjectTypes { get; }
    DbSet<HouseType> HouseTypes { get; }
    DbSet<ApartmentType> ApartmentTypes { get; }
    DbSet<RoomType> RoomTypes { get; }
    DbSet<ObjectLevel> ObjectLevels { get; }
    DbSet<FiasParam> Params { get; }
}

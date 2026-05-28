using Fias.Application.Abstractions;
using Fias.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fias.Infrastructure.Persistence;

public class FiasDbContext(DbContextOptions<FiasDbContext> options)
    : DbContext(options), IFiasDbContext
{
    public DbSet<ReestrObject> ReestrObjects => Set<ReestrObject>();
    public DbSet<AddressObject> AddressObjects => Set<AddressObject>();
    public DbSet<House> Houses => Set<House>();
    public DbSet<Apartment> Apartments => Set<Apartment>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<AdmHierarchy> AdmHierarchy => Set<AdmHierarchy>();
    public DbSet<MunHierarchy> MunHierarchy => Set<MunHierarchy>();
    public DbSet<AddressObjectType> AddressObjectTypes => Set<AddressObjectType>();
    public DbSet<HouseType> HouseTypes => Set<HouseType>();
    public DbSet<ApartmentType> ApartmentTypes => Set<ApartmentType>();
    public DbSet<RoomType> RoomTypes => Set<RoomType>();
    public DbSet<ObjectLevel> ObjectLevels => Set<ObjectLevel>();
    public DbSet<FiasParam> Params => Set<FiasParam>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Схема fias.* создаётся миграцией Updater'а — здесь только маппинг для чтения.
        b.HasDefaultSchema("fias");

        b.Entity<ReestrObject>(e =>
        {
            e.ToTable("reestr_objects");
            e.HasKey(x => x.ObjectId);
            e.Property(x => x.ObjectId).ValueGeneratedNever();
        });

        b.Entity<AddressObject>(e =>
        {
            e.ToTable("addressobjects");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.ObjectId);
        });

        b.Entity<House>(e =>
        {
            e.ToTable("houses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.ObjectId);
        });

        b.Entity<Apartment>(e =>
        {
            e.ToTable("apartments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.ObjectId);
        });

        b.Entity<Room>(e =>
        {
            e.ToTable("rooms");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.ObjectId);
        });

        b.Entity<AdmHierarchy>(e =>
        {
            e.ToTable("adm_hierarchy");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.ObjectId);
        });

        b.Entity<MunHierarchy>(e =>
        {
            e.ToTable("mun_hierarchy");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.ObjectId);
        });

        b.Entity<AddressObjectType>(e =>
        {
            e.ToTable("addressobject_types");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        b.Entity<HouseType>(e =>
        {
            e.ToTable("house_types");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        b.Entity<ApartmentType>(e =>
        {
            e.ToTable("apartment_types");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        b.Entity<RoomType>(e =>
        {
            e.ToTable("room_types");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        b.Entity<ObjectLevel>(e =>
        {
            e.ToTable("object_levels");
            e.HasKey(x => x.Level);
            e.Property(x => x.Level).ValueGeneratedNever();
        });

        b.Entity<FiasParam>(e =>
        {
            e.ToTable("params");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.ObjectId, x.TypeId });
        });
    }
}

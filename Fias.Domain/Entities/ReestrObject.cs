namespace Fias.Domain.Entities;

public class ReestrObject
{
    public long ObjectId { get; set; }
    public Guid? ObjectGuid { get; set; }
    public int? LevelId { get; set; }
    public DateOnly? UpdateDate { get; set; }
    public DateOnly? CreateDate { get; set; }
    public short? IsActive { get; set; }
}

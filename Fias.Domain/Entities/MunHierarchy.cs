namespace Fias.Domain.Entities;

public class MunHierarchy
{
    public long Id { get; set; }
    public long ObjectId { get; set; }
    public long? ParentObjId { get; set; }
    public string? Path { get; set; }
    public DateOnly? EndDate { get; set; }
    public short? IsActive { get; set; }
}

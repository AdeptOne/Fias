namespace Fias.Domain.Entities;

public class AddressObject
{
    public long Id { get; set; }
    public long ObjectId { get; set; }
    public Guid? ObjectGuid { get; set; }
    public string? Name { get; set; }
    public string? TypeName { get; set; }
    public int? Level { get; set; }
    public int? OperTypeId { get; set; }
    public long? PrevId { get; set; }
    public long? NextId { get; set; }
    public DateOnly? UpdateDate { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool? IsActual { get; set; }
    public bool? IsActive { get; set; }
}

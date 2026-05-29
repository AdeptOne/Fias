namespace Fias.Domain.Entities;

public class Apartment
{
    public long Id { get; set; }
    public long ObjectId { get; set; }
    public Guid? ObjectGuid { get; set; }
    public string? Number { get; set; }
    public int? ApartType { get; set; }
    public int? OperTypeId { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool? IsActual { get; set; }
    public bool? IsActive { get; set; }
}

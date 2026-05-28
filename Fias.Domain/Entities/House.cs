namespace Fias.Domain.Entities;

public class House
{
    public long Id { get; set; }
    public long ObjectId { get; set; }
    public Guid? ObjectGuid { get; set; }
    public string? HouseNum { get; set; }
    public string? AddNum1 { get; set; }
    public string? AddNum2 { get; set; }
    public int? HouseType { get; set; }
    public int? AddType1 { get; set; }
    public int? AddType2 { get; set; }
    public DateOnly? EndDate { get; set; }
    public short? IsActual { get; set; }
    public short? IsActive { get; set; }
}

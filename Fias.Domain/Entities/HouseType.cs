namespace Fias.Domain.Entities;

public class HouseType
{
    public int Id { get; set; }
    public string? ShortName { get; set; }
    public string? Name { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool? IsActive { get; set; }
}

namespace Fias.Domain.Entities;

public class ObjectLevel
{
    public int Level { get; set; }
    public string? Name { get; set; }
    public string? ShortName { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool? IsActive { get; set; }
}

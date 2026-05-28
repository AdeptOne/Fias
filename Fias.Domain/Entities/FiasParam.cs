namespace Fias.Domain.Entities;

public class FiasParam
{
    public long Id { get; set; }
    public long ObjectId { get; set; }
    public int? TypeId { get; set; }
    public string? Value { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}

namespace InventoryService.Models;

public class InventoryItemEntity
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Stock { get; set; }
}

namespace OrderService.Models;

public class OrderEntity
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

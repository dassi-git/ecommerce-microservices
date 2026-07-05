namespace Shared.Contracts;

public record OrderCreatedEvent(int OrderId, int ProductId, int Quantity, DateTime CreatedAt);
public record InventoryReservedEvent(int OrderId, int ProductId, int Quantity, DateTime ReservedAt);
public record InventoryReservationFailedEvent(int OrderId, int ProductId, int Quantity, string Reason, DateTime FailedAt);
public record OrderStatusChangedEvent(int OrderId, string Status, DateTime ChangedAt);

namespace SkillTokenDemo.Core;

/// <summary>
/// A ready-made test for the prompt comparison: a task that has existing code it should reuse.
/// The known symbols are the names that exist in that code, so the analyser can tell reuse from invention.
/// </summary>
public static class ComparisonExample
{
    public const string Prompt =
        "Add a POST /orders/{id}/cancel endpoint that calls the existing cancel logic. " +
        "Shipped orders must not be cancellable. Do not hard-delete anything. " +
        "Include one test for the shipped-order case.";

    /// <summary>A prose-only prompt, for skills that change how text is written (e.g. caveman).</summary>
    public const string TextPrompt =
        "Explain what a closure is in JavaScript, when it is useful, and give one short example.";

    public const string Context = """
        public enum OrderStatus { Open, Shipped, Cancelled }

        public enum CancelOutcome { Cancelled, NotFound, NotAllowed }

        public sealed class Order
        {
            public Guid Id { get; init; }
            public OrderStatus Status { get; private set; }

            public bool CanTransitionTo(OrderStatus next) =>
                Status == OrderStatus.Open && next == OrderStatus.Cancelled;

            public void Transition(OrderStatus next) => Status = next;
        }

        public sealed class OrderService(OrdersDbContext db)
        {
            public Task<Order?> GetAsync(Guid id, CancellationToken ct) =>
                db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

            // Returns NotFound, NotAllowed (order is not open) or Cancelled.
            public async Task<CancelOutcome> CancelAsync(Guid id, CancellationToken ct)
            {
                var order = await GetAsync(id, ct);
                if (order is null) return CancelOutcome.NotFound;
                if (!order.CanTransitionTo(OrderStatus.Cancelled)) return CancelOutcome.NotAllowed;
                order.Transition(OrderStatus.Cancelled);
                await db.SaveChangesAsync(ct);
                return CancelOutcome.Cancelled;
            }
        }
        """;

    /// <summary>Names already defined in <see cref="Context"/>.</summary>
    public static readonly string[] KnownSymbols =
        ["OrderStatus", "CancelOutcome", "Order", "OrderService", "OrdersDbContext", "GetAsync", "CancelAsync", "CanTransitionTo", "Transition"];
}

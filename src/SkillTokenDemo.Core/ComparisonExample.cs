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

    /// <summary>
    /// A deliberately vague version of <see cref="Prompt"/> for grill-me: the decisions are left open, so
    /// there is something to ask about. The fact sheet (<see cref="Answers"/>) holds the real answers.
    /// </summary>
    public const string VaguePrompt = "Customers need to be able to cancel their orders. Add that to the API.";

    /// <summary>A prose-only prompt, for skills that change how text is written (e.g. caveman).</summary>
    public const string TextPrompt =
        "Our team is choosing between threads and async/await for a .NET service that calls three slow HTTP APIs " +
        "per request and then writes the results to SQL Server. Explain how threads and async/await differ, " +
        "what each one costs in memory and CPU, how they behave under high load, the common pitfalls " +
        "(deadlocks with .Result, thread-pool starvation, race conditions), and which one you would recommend for " +
        "this service and why. Finish with a short C# example of the recommended approach.";

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

    /// <summary>
    /// The simulated user's fact sheet for the cancel-order task, keyed by <see cref="ClarifyingTopics"/> topic.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Answers = new Dictionary<string, string>
    {
        ["existing"] = "Yes. OrderService.CancelAsync already exists and the Order state machine only allows cancelling open orders. Reuse it.",
        ["shipped"] = "A shipped order must not be cancelled: return 409 Conflict.",
        ["reason"] = "No, we do not need to store a cancel reason.",
        ["no-hard-delete"] = "Never delete rows. Cancelling only changes the status to Cancelled.",
        ["tests"] = "Yes, add one test for the shipped-order case."
    };

    public const string NoMoreConstraints = "No other constraints. Go ahead and implement it now.";

    /// <summary>Answers every fact-sheet topic the question raises; nudges the model to implement otherwise.</summary>
    public static string AnswerFor(string question)
    {
        var parts = ClarifyingTopics.Covered(question).Where(Answers.ContainsKey).Select(t => Answers[t]).ToList();
        return parts.Count == 0 ? NoMoreConstraints : string.Join(" ", parts);
    }

    /// <summary>Names already defined in <see cref="Context"/>.</summary>
    public static readonly string[] KnownSymbols =
        ["OrderStatus", "CancelOutcome", "Order", "OrderService", "OrdersDbContext", "GetAsync", "CancelAsync", "CanTransitionTo", "Transition"];
}

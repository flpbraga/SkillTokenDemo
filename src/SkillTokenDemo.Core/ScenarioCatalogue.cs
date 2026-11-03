namespace SkillTokenDemo.Core;

/// <summary>
/// Hand-written conversations for one task: "add an endpoint to cancel an order".
/// They are illustrative: the shape of each workflow is taken from the skills' public READMEs,
/// the wording is invented for the demo.
/// </summary>
public static class ScenarioCatalogue
{
    public const string TaskPrompt =
        "Add an endpoint to cancel an order in our ASP.NET Core Orders API. " +
        "Shipped orders must not be cancellable, and we should not hard-delete anything.";

    public static IReadOnlyList<Scenario> All() =>
    [
        Baseline(),
        GrillMe(),
        Caveman(),
        Ponytail(),
        Superpowers()
    ];

    private static Scenario Baseline() => new(
        new SkillProfile("Normal", "(none)", "No skill. Chatty answer, assumptions, then a correction round.", 0),
        [
            new(Speaker.User, "Prompt", TaskPrompt),
            new(Speaker.Assistant, "Verbose answer", """
                Great question! Cancelling an order is a common requirement, so let me walk you through a robust
                approach. I will introduce a dedicated service abstraction, a request DTO and a validator so the
                endpoint stays thin and testable. Here is the full implementation:

                ```csharp
                public interface IOrderCancellationService
                {
                    Task<CancellationResult> CancelAsync(Guid orderId, CancelOrderRequest request, CancellationToken ct);
                }

                public sealed record CancelOrderRequest(string Reason);

                public sealed class CancelOrderRequestValidator : AbstractValidator<CancelOrderRequest>
                {
                    public CancelOrderRequestValidator()
                    {
                        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
                    }
                }

                public sealed class OrderCancellationService(OrdersDbContext db) : IOrderCancellationService
                {
                    public async Task<CancellationResult> CancelAsync(Guid orderId, CancelOrderRequest request, CancellationToken ct)
                    {
                        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
                        if (order is null)
                        {
                            return CancellationResult.NotFound;
                        }

                        if (order.Status == OrderStatus.Shipped)
                        {
                            return CancellationResult.AlreadyShipped;
                        }

                        order.Status = OrderStatus.Cancelled;
                        order.CancelReason = request.Reason;
                        await db.SaveChangesAsync(ct);
                        return CancellationResult.Cancelled;
                    }
                }

                app.MapPost("/orders/{id:guid}/cancel", async (Guid id, CancelOrderRequest request,
                    IOrderCancellationService service, CancellationToken ct) =>
                {
                    var result = await service.CancelAsync(id, request, ct);
                    return result switch
                    {
                        CancellationResult.NotFound => Results.NotFound(),
                        CancellationResult.AlreadyShipped => Results.Conflict("Shipped orders cannot be cancelled."),
                        _ => Results.NoContent()
                    };
                });
                ```

                Remember to register the service and the validator in Program.cs and add a migration for the new
                CancelReason column. I hope this helps! Let me know if you would like me to add unit tests as well.
                """),
            new(Speaker.User, "Correction", "We already have an OrderService with a Cancel rule, and Status is a state machine. Please redo it."),
            new(Speaker.Assistant, "Verbose rework", """
                Apologies for the confusion, and thank you for the extra context! Since an OrderService already
                exists, the cleanest approach is to extend it rather than add a parallel service. Here is the
                revised version, which delegates to the existing state machine:

                ```csharp
                app.MapPost("/orders/{id:guid}/cancel", async (Guid id, OrderService orders, CancellationToken ct) =>
                {
                    var order = await orders.GetAsync(id, ct);
                    if (order is null)
                    {
                        return Results.NotFound();
                    }

                    if (!order.CanTransitionTo(OrderStatus.Cancelled))
                    {
                        return Results.Conflict("Shipped orders cannot be cancelled.");
                    }

                    await orders.CancelAsync(order, ct);
                    return Results.NoContent();
                });
                ```

                You can now delete the service, DTO and validator from my first answer. Please also double-check
                that the migration I suggested earlier is removed. Let me know if anything else is needed!
                """)
        ],
        "Cheap first prompt, expensive rework: the wrong assumptions are paid for twice.");

    private static Scenario GrillMe() => new(
        new SkillProfile("grill-me", "/grill-me",
            "Interviews you one question at a time before acting. Spends tokens early to avoid rework.", 200),
        [
            new(Speaker.User, "Prompt", TaskPrompt),
            new(Speaker.Assistant, "Question 1", "Does an OrderService or an order state machine already exist that I should extend? My recommendation: yes, reuse it."),
            new(Speaker.User, "Answer 1", "Yes, OrderService with a state machine."),
            new(Speaker.Assistant, "Question 2", "Should a cancel reason be stored? My recommendation: no, YAGNI until someone asks."),
            new(Speaker.User, "Answer 2", "No reason needed."),
            new(Speaker.Assistant, "Question 3", "Cancelling a shipped order: 409 Conflict or 400 Bad Request? My recommendation: 409."),
            new(Speaker.User, "Answer 3", "409."),
            new(Speaker.Assistant, "Implementation", """
                Shared understanding reached. Implementation:

                ```csharp
                app.MapPost("/orders/{id:guid}/cancel", async (Guid id, OrderService orders, CancellationToken ct) =>
                {
                    var order = await orders.GetAsync(id, ct);
                    if (order is null)
                    {
                        return Results.NotFound();
                    }

                    if (!order.CanTransitionTo(OrderStatus.Cancelled))
                    {
                        return Results.Conflict("Shipped orders cannot be cancelled.");
                    }

                    await orders.CancelAsync(order, ct);
                    return Results.NoContent();
                });
                ```

                It reuses the existing state machine, so no migration is needed.
                """)
        ],
        "More turns, but each is tiny and the correction round disappears. Pays off when the prompt is vague.");

    private static Scenario Caveman() => new(
        new SkillProfile("caveman", "/caveman",
            "Terse prose, code untouched. Cuts output words only; the rules cost ~1,160 input tokens per request (per its README).", 1160),
        [
            new(Speaker.User, "Prompt", TaskPrompt),
            new(Speaker.Assistant, "Terse answer", """
                Add service, DTO, validator, endpoint.

                ```csharp
                public interface IOrderCancellationService
                {
                    Task<CancellationResult> CancelAsync(Guid orderId, CancelOrderRequest request, CancellationToken ct);
                }

                public sealed record CancelOrderRequest(string Reason);

                public sealed class CancelOrderRequestValidator : AbstractValidator<CancelOrderRequest>
                {
                    public CancelOrderRequestValidator()
                    {
                        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
                    }
                }

                public sealed class OrderCancellationService(OrdersDbContext db) : IOrderCancellationService
                {
                    public async Task<CancellationResult> CancelAsync(Guid orderId, CancelOrderRequest request, CancellationToken ct)
                    {
                        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
                        if (order is null)
                        {
                            return CancellationResult.NotFound;
                        }

                        if (order.Status == OrderStatus.Shipped)
                        {
                            return CancellationResult.AlreadyShipped;
                        }

                        order.Status = OrderStatus.Cancelled;
                        order.CancelReason = request.Reason;
                        await db.SaveChangesAsync(ct);
                        return CancellationResult.Cancelled;
                    }
                }

                app.MapPost("/orders/{id:guid}/cancel", async (Guid id, CancelOrderRequest request,
                    IOrderCancellationService service, CancellationToken ct) =>
                {
                    var result = await service.CancelAsync(id, request, ct);
                    return result switch
                    {
                        CancellationResult.NotFound => Results.NotFound(),
                        CancellationResult.AlreadyShipped => Results.Conflict("Shipped orders cannot be cancelled."),
                        _ => Results.NoContent()
                    };
                });
                ```

                Register service and validator. Add migration for CancelReason.
                """),
            new(Speaker.User, "Correction", "We already have an OrderService with a Cancel rule, and Status is a state machine. Please redo it."),
            new(Speaker.Assistant, "Terse rework", """
                Reuse OrderService. Drop service, DTO, validator, migration.

                ```csharp
                app.MapPost("/orders/{id:guid}/cancel", async (Guid id, OrderService orders, CancellationToken ct) =>
                {
                    var order = await orders.GetAsync(id, ct);
                    if (order is null)
                    {
                        return Results.NotFound();
                    }

                    if (!order.CanTransitionTo(OrderStatus.Cancelled))
                    {
                        return Results.Conflict("Shipped orders cannot be cancelled.");
                    }

                    await orders.CancelAsync(order, ct);
                    return Results.NoContent();
                });
                ```
                """)
        ],
        "Prose shrinks, code does not, and the rework round remains. A real but modest saving on code-heavy work.");

    private static Scenario Ponytail() => new(
        new SkillProfile("ponytail", "/ponytail",
            "Reads the code first, then writes the least code that works. Fewer lines, fewer rework rounds.", 900),
        [
            new(Speaker.User, "Prompt", TaskPrompt),
            new(Speaker.Assistant, "Minimal change", """
                OrderService already has CancelAsync and the state machine blocks shipped orders, so only the route is missing.

                ```csharp
                app.MapPost("/orders/{id:guid}/cancel", async (Guid id, OrderService orders, CancellationToken ct) =>
                    await orders.CancelAsync(id, ct) switch
                    {
                        CancelOutcome.NotFound => Results.NotFound(),
                        CancelOutcome.NotAllowed => Results.Conflict("Shipped orders cannot be cancelled."),
                        _ => Results.NoContent()
                    });
                ```

                One test for the shipped-order branch:

                ```csharp
                [Fact]
                public async Task Cancelling_a_shipped_order_returns_conflict() =>
                    Assert.Equal(409, await Post("/orders/" + ShippedId + "/cancel"));
                ```
                """)
        ],
        "Biggest saving here: less code written and no correction round. Needs a codebase to read.");

    private static Scenario Superpowers() => new(
        new SkillProfile("superpowers", "/superpowers",
            "Brainstorm, written plan, TDD, review. Highest token cost, aimed at quality on large work.", 3500),
        [
            new(Speaker.User, "Prompt", TaskPrompt),
            new(Speaker.Assistant, "Brainstorming", """
                Using brainstorming to shape this. It looks bounded: one endpoint over existing code.
                Is there an existing OrderService or state machine to build on?
                """),
            new(Speaker.User, "Answer", "Yes, OrderService with a state machine."),
            new(Speaker.Assistant, "Short design", """
                Design: add POST /orders/{id}/cancel, delegate to OrderService.CancelAsync, map NotFound to 404,
                NotAllowed to 409, success to 204. No schema change. Tests: shipped order returns 409, open order
                returns 204, unknown order returns 404. Approve?
                """),
            new(Speaker.User, "Approval", "Approved."),
            new(Speaker.Assistant, "Plan", """
                Plan:
                1. Write three failing endpoint tests.
                2. Add the route and map outcomes to status codes.
                3. Run the suite and confirm green before claiming done.
                """),
            new(Speaker.User, "Go", "Go ahead."),
            new(Speaker.Assistant, "TDD implementation", """
                Red: three tests added and failing.

                ```csharp
                [Fact]
                public async Task Cancelling_a_shipped_order_returns_conflict() =>
                    Assert.Equal(409, await Post("/orders/" + ShippedId + "/cancel"));

                [Fact]
                public async Task Cancelling_an_open_order_returns_no_content() =>
                    Assert.Equal(204, await Post("/orders/" + OpenId + "/cancel"));

                [Fact]
                public async Task Cancelling_an_unknown_order_returns_not_found() =>
                    Assert.Equal(404, await Post("/orders/" + Guid.NewGuid() + "/cancel"));
                ```

                Green: route added.

                ```csharp
                app.MapPost("/orders/{id:guid}/cancel", async (Guid id, OrderService orders, CancellationToken ct) =>
                    await orders.CancelAsync(id, ct) switch
                    {
                        CancelOutcome.NotFound => Results.NotFound(),
                        CancelOutcome.NotAllowed => Results.Conflict("Shipped orders cannot be cancelled."),
                        _ => Results.NoContent()
                    });
                ```

                Verification: 3 new tests pass, 148 of 148 total pass.
                """)
        ],
        "Most expensive, but the only flow that proves the behaviour. Worth it for risky or large changes.");
}

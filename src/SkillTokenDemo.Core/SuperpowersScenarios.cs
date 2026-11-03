using System.Text.RegularExpressions;

namespace SkillTokenDemo.Core;

/// <summary>
/// Fixed demo content for exercising three parts of the superpowers process in isolation: brainstorming
/// (clarify then present a design, no code), writing-plans (a plan with tests before implementation) and
/// code-review (catching a real, planted defect). Each uses its own short system prompt, not the catch-all
/// "superpowers" entry in <see cref="SkillInstructions"/>, so the model sees only that one phase's job.
/// </summary>
public static class SuperpowersScenarios
{
    public const string BrainstormSystem =
        "You are a coding assistant following a structured process. Before acting on a plan, classify the work " +
        "as a spike, a bounded change or an architectural change, then interview the user one question at a time, " +
        "walking the decision tree in dependency order, until you reach a shared understanding. Then present a " +
        "short design in a few sentences and stop. Never write implementation code at this stage.";

    public const string WritingPlansSystem =
        "You are a coding assistant following a structured process. You are given an approved design and the " +
        "existing code. Write an implementation plan broken into small, numbered tasks (\"Task 1\", \"Task 2\", " +
        "...). Every task that changes behaviour must list a step to write a failing test BEFORE the step that " +
        "implements it (red, then green). Name the exact files and function signatures involved. Do not write the " +
        "actual implementation, only the plan.";

    public const string CodeReviewSystem =
        "You are a coding assistant following a structured process. Review the given code for correctness bugs " +
        "only. Report each finding as the file/function, the concrete failure scenario (what input or state " +
        "triggers it and what goes wrong), and the fix. Be terse: no praise, no style nits, no restating the code.";

    /// <summary>The design a brainstorming session would have produced, as the input to the writing-plans test.</summary>
    public const string ApprovedDesign =
        "Design: add POST /orders/{id}/cancel, delegating to the existing OrderService.CancelAsync. Map " +
        "NotFound to 404, NotAllowed to 409, Cancelled to 204. No schema change. Add one test for the " +
        "shipped-order case.";

    /// <summary>
    /// <see cref="ComparisonExample.Context"/> with the shipped-order guard removed — cancelling a shipped
    /// order silently succeeds. The planted defect a code review should catch.
    /// </summary>
    public const string BuggyContext = """
        public enum OrderStatus { Open, Shipped, Cancelled }

        public enum CancelOutcome { Cancelled, NotFound, NotAllowed }

        public sealed class Order
        {
            public Guid Id { get; init; }
            public OrderStatus Status { get; private set; }

            public void Transition(OrderStatus next) => Status = next;
        }

        public sealed class OrderService(OrdersDbContext db)
        {
            public Task<Order?> GetAsync(Guid id, CancellationToken ct) =>
                db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

            public async Task<CancelOutcome> CancelAsync(Guid id, CancellationToken ct)
            {
                var order = await GetAsync(id, ct);
                if (order is null) return CancelOutcome.NotFound;
                order.Transition(OrderStatus.Cancelled);
                await db.SaveChangesAsync(ct);
                return CancelOutcome.Cancelled;
            }
        }
        """;

    // A review checks code against what it is meant to do, so the requirement travels with the code; without
    // it, cancelling a shipped order does not look like a bug at all.
    public const string CodeReviewPrompt =
        "Requirement: only open orders can be cancelled. Cancelling a shipped or already-cancelled order must " +
        "return CancelOutcome.NotAllowed and leave the order unchanged.\n\n" +
        "Review this code against the requirement for bugs before we ship it.";

    private static readonly Regex MentionsGuard = new(@"(?i)CanTransitionTo", RegexOptions.Compiled);
    private static readonly Regex MentionsShipped = new(@"(?i)shipped", RegexOptions.Compiled);
    private static readonly Regex MentionsMissingCheck =
        new(@"(?i)(missing|no|without|doesn'?t|does not|never|lacks?)\s+(\w+\s+){0,3}(check|validat|guard)", RegexOptions.Compiled);

    private static readonly Regex MentionsStatusRule =
        new(@"(?i)NotAllowed|already[- ]cancel", RegexOptions.Compiled);

    // A fix in code that guards on the status: "Status != OrderStatus.Open", "Status == OrderStatus.Shipped", ...
    private static readonly Regex StatusGuardInCode =
        new(@"Status\s*(?:!=|==|is\s+not|is)\s*OrderStatus\.(?:Open|Shipped)", RegexOptions.Compiled);

    // One line that ties "shipped" to the cancel rule: "a Shipped order is cancelled", "NotAllowed for Shipped".
    // Order matters: "...to Cancelled, ... remain Shipped" (a save failure) mentions both but is not the finding.
    private static readonly Regex ShippedFinding =
        new(@"(?i)shipped.*\bcancel|NotAllowed.*shipped|shipped.*NotAllowed", RegexOptions.Compiled);

    private static readonly Regex EnumLine = new(@"(?i)\benum\b|\{\s*Open", RegexOptions.Compiled);

    // Asking for a check at all ("add a check", "missing validation"), negated or not.
    private static readonly Regex AsksForCheck = new(@"(?i)\b(check|guard|validat\w*)\b", RegexOptions.Compiled);

    private static bool NamesShippedCase(string reply) =>
        StatusGuardInCode.IsMatch(reply) ||
        reply.Split('\n').Any(line => ShippedFinding.IsMatch(line) && !EnumLine.IsMatch(line));

    /// <summary>True if the reply calls out the planted defect: nothing stops a shipped order from being cancelled.</summary>
    public static bool FoundPlantedDefect(string reply) => GradeReview(reply) == ReviewGrade.Found;

    /// <summary>
    /// Found: names the shipped-order case or the missing CanTransitionTo guard. Partial: sees that a status
    /// check is missing but only for some other case (e.g. already cancelled), so its fix still lets a shipped
    /// order be cancelled. Missed: neither.
    /// </summary>
    public static ReviewGrade GradeReview(string reply) =>
        MentionsGuard.IsMatch(reply) || NamesShippedCase(reply) ||
        (MentionsShipped.IsMatch(reply) && MentionsMissingCheck.IsMatch(reply))
            ? ReviewGrade.Found
            : AsksForCheck.IsMatch(reply) && MentionsStatusRule.IsMatch(reply)
                ? ReviewGrade.Partial
                : ReviewGrade.Missed;
}

public enum ReviewGrade { Missed, Partial, Found }

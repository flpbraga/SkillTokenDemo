using SkillTokenDemo.Core;

namespace SkillTokenDemo.Tests;

public class PlanAnalyserTests
{
    [Fact]
    public void Counts_tasks_and_test_first_tasks()
    {
        var plan = """
            ### Task 1: Add the route
            - Write the failing test for the shipped-order case.
            - Run it, confirm it fails.
            - Implement the endpoint.
            - Run it, confirm it passes.

            ### Task 2: Wire it up
            - Implement the DI registration.
            - No test needed for this one.
            """;

        var m = PlanAnalyser.Analyse(plan);

        Assert.Equal(2, m.TaskCount);
        Assert.Equal(1, m.TasksWithTestFirst);
    }

    [Fact]
    public void A_reply_with_no_tasks_has_zero_counts()
    {
        var m = PlanAnalyser.Analyse("Sure, here is the plan in prose, no structure.");

        Assert.Equal(0, m.TaskCount);
        Assert.Equal(0, m.TasksWithTestFirst);
    }

    [Fact]
    public void A_task_that_only_implements_is_not_test_first()
    {
        var plan = "### Task 1: Add the route\n```csharp\napp.MapPost(\"/x\", () => 1);\n```";

        Assert.Equal(0, PlanAnalyser.Analyse(plan).TasksWithTestFirst);
    }
}

public class DefectCheckTests
{
    [Theory]
    [InlineData("The code never calls CanTransitionTo, so a shipped order can still be cancelled.", true)]
    [InlineData("There is no check that the order is still open before cancelling a shipped order.", true)]
    [InlineData("The formatting on line 12 is inconsistent.", false)]
    public void Detects_whether_the_reply_calls_out_the_planted_defect(string reply, bool expected) =>
        Assert.Equal(expected, SuperpowersScenarios.FoundPlantedDefect(reply));

    [Theory]
    [InlineData("The code never calls CanTransitionTo, so a shipped order can still be cancelled.", ReviewGrade.Found)]
    [InlineData("CancelAsync does not check if the order is already cancelled; it should return CancelOutcome.NotAllowed.", ReviewGrade.Partial)]
    [InlineData("GetAsync can return null when the order does not exist.", ReviewGrade.Missed)]
    // Real replies from the local model: right finding, worded without "missing check".
    [InlineData("- `OrderService` should only handle `Open` orders and should return `NotAllowed` for `Shipped` or `Cancelled` orders.", ReviewGrade.Found)]
    [InlineData("- **Input**: An existing `Order` with a `Shipped` status is cancelled.", ReviewGrade.Found)]
    [InlineData("```csharp\nif (order is null || order.Status != OrderStatus.Open) return CancelOutcome.NotAllowed;\n```", ReviewGrade.Found)]
    [InlineData("- **Failure Scenario:** The function does not handle the case where an order is already cancelled.\n- **Fix:** Add a check to ensure the order is not already cancelled.", ReviewGrade.Partial)]
    // Restating the enum, or a different failure that only mentions the statuses, is not a finding.
    [InlineData("The enum OrderStatus { Open, Shipped, Cancelled } is well defined.", ReviewGrade.Missed)]
    [InlineData("After transitioning the order to `Cancelled`, if SaveChangesAsync fails the order will remain in the `Shipped` status.", ReviewGrade.Missed)]
    public void Grades_how_much_of_the_planted_defect_the_review_caught(string reply, ReviewGrade expected) =>
        Assert.Equal(expected, SuperpowersScenarios.GradeReview(reply));
}

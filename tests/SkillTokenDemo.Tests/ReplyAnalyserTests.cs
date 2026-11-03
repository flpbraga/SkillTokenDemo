using SkillTokenDemo.Core;

namespace SkillTokenDemo.Tests;

public class ReplyAnalyserTests
{
    private static readonly string[] Known = ["OrderService", "Order", "OrderStatus", "CancelAsync", "CanTransitionTo"];

    [Fact]
    public void Reused_symbols_are_found_by_whole_word()
    {
        var m = ReplyAnalyser.Analyse("Call orders.CancelAsync(id) after checking order.CanTransitionTo(x).", Known);

        Assert.Contains("CancelAsync", m.ReusedSymbols);
        Assert.Contains("CanTransitionTo", m.ReusedSymbols);
        Assert.DoesNotContain("OrderStatus", m.ReusedSymbols);
    }

    [Fact]
    public void New_types_exclude_known_names()
    {
        var reply = """
            ```csharp
            public sealed class OrderCancellationService { }
            public interface IOrderCancellationService { }
            public sealed class OrderService { }
            ```
            """;

        var m = ReplyAnalyser.Analyse(reply, Known);

        Assert.Equal(["OrderCancellationService", "IOrderCancellationService"], m.NewTypes);
    }

    [Fact]
    public void Prose_mentioning_a_class_is_not_a_new_type()
    {
        var m = ReplyAnalyser.Analyse("This is a class and it contains the logic.", Known);

        Assert.Empty(m.NewTypes);
    }

    [Fact]
    public void Code_lines_count_only_non_blank_lines_inside_fences()
    {
        var reply = "Intro line\n```csharp\nvar a = 1;\n\nvar b = 2;\n```\nOutro";

        Assert.Equal(2, ReplyAnalyser.Analyse(reply, Known).CodeLines);
    }

    [Fact]
    public void Questions_are_counted_outside_code_fences()
    {
        var reply = "Does OrderService exist yet?\n```csharp\nvar x = a ? b : c;\n```\nShould shipped orders return 409?";

        var m = ReplyAnalyser.Analyse(reply, Known);

        Assert.Equal(2, m.Questions);
        Assert.True(m.HasCode);
    }

    [Fact]
    public void A_question_only_reply_has_no_code()
    {
        var m = ReplyAnalyser.Analyse("Does OrderService exist yet?\nShould shipped orders return 409?", Known);

        Assert.Equal(2, m.Questions);
        Assert.False(m.HasCode);
    }

    [Fact]
    public void Clarifying_topics_are_covered_only_by_questions()
    {
        var reply = "Does OrderService already have CancelAsync?\nShould a shipped order return 409 or 400?\nWe will implement it now.";

        var covered = ClarifyingTopics.Covered(reply);

        Assert.Contains("existing", covered);
        Assert.Contains("shipped", covered);
        Assert.DoesNotContain("tests", covered);
    }

    [Theory]
    [InlineData("[Fact]\npublic void T() { }", true)]
    [InlineData("Assert.Equal(1, x);", true)]
    [InlineData("No tests here.", false)]
    public void Detects_tests(string reply, bool expected) =>
        Assert.Equal(expected, ReplyAnalyser.Analyse(reply, Known).HasTest);
}

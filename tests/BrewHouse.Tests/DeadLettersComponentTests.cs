using Bunit;
using BrewHouse.Components.Pages;
using BrewHouse.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace BrewHouse.Tests;

public class DeadLettersComponentTests : BunitContext
{
    private static DeadLetterItem Item(Guid id, string message = "Handler blew up") => new(
        id,
        "BrewHouse.Services.ProcessBidMessage, BrewHouse",
        "System.InvalidOperationException",
        message,
        new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero),
        Replayable: false);

    private Mock<IDeadLetterAdmin> Admin(DeadLetterPage page)
    {
        var admin = new Mock<IDeadLetterAdmin>();
        admin.Setup(a => a.ListAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(page);
        Services.AddSingleton(admin.Object);
        return admin;
    }

    [Fact]
    public void Empty_Store_Says_Nothing_Is_Waiting()
    {
        Admin(new DeadLetterPage(0, []));

        var cut = Render<DeadLetters>();

        cut.WaitForAssertion(() => Assert.Contains("No dead letters", cut.Find("[data-testid=dead-letter-empty]").TextContent));
    }

    [Fact]
    public void Lists_Each_Dead_Letter_With_Its_Short_Type_Names_And_Count()
    {
        var id = Guid.NewGuid();
        Admin(new DeadLetterPage(1, [Item(id, "Cannot process bid: coffee lot 99 does not exist.")]));

        var cut = Render<DeadLetters>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("1 dead letter(s)", cut.Find("[data-testid=dead-letter-count]").TextContent);
            var row = cut.Find($"[data-testid=dead-letter-{id}]");
            Assert.Contains("ProcessBidMessage", row.TextContent);
            Assert.Contains("InvalidOperationException", row.TextContent);
            Assert.Contains("coffee lot 99 does not exist", row.TextContent);
        });
    }

    [Fact]
    public void Replay_Click_Calls_The_Service_And_Reloads()
    {
        var id = Guid.NewGuid();
        var admin = Admin(new DeadLetterPage(1, [Item(id)]));
        var cut = Render<DeadLetters>();
        cut.WaitForAssertion(() => cut.Find($"[data-testid=replay-{id}]"));

        cut.Find($"[data-testid=replay-{id}]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Marked for replay", cut.Find("[data-testid=dead-letter-notice]").TextContent));
        admin.Verify(a => a.ReplayAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        admin.Verify(a => a.ListAsync(1, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public void Discard_Needs_A_Second_Click_Before_Anything_Is_Removed()
    {
        var id = Guid.NewGuid();
        var admin = Admin(new DeadLetterPage(1, [Item(id)]));
        var cut = Render<DeadLetters>();
        cut.WaitForAssertion(() => cut.Find($"[data-testid=discard-{id}]"));

        cut.Find($"[data-testid=discard-{id}]").Click();

        admin.Verify(a => a.DiscardAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        cut.Find($"[data-testid=confirm-discard-{id}]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Discarded", cut.Find("[data-testid=dead-letter-notice]").TextContent));
        admin.Verify(a => a.DiscardAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void A_Failing_Store_Shows_An_Error_Not_A_Crash()
    {
        var admin = new Mock<IDeadLetterAdmin>();
        admin.Setup(a => a.ListAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unreachable"));
        Services.AddSingleton(admin.Object);

        var cut = Render<DeadLetters>();

        cut.WaitForAssertion(() => Assert.Contains("database unreachable", cut.Find("[data-testid=dead-letter-error]").TextContent));
    }
}

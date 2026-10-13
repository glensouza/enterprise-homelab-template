namespace BrewHouse.Services;

/// <summary>One persisted Wolverine dead letter, flattened for display.</summary>
public sealed record DeadLetterItem(
    Guid Id,
    string MessageType,
    string ExceptionType,
    string ExceptionMessage,
    DateTimeOffset SentAt,
    bool Replayable);

public sealed record DeadLetterPage(int TotalCount, IReadOnlyList<DeadLetterItem> Items);

/// <summary>
/// Read/replay/discard access to Wolverine's persisted dead letters - messages whose handler kept failing
/// and were parked in <c>wolverine.wolverine_dead_letters</c> (docs/01 ADR 125). Mutating calls are
/// destructive or re-run business logic, so the only caller is the admin-gated dead-letter page.
/// </summary>
public interface IDeadLetterAdmin
{
    /// <param name="pageNumber">1-based.</param>
    Task<DeadLetterPage> ListAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Marks the dead letter replayable; Wolverine's durability agent then re-sends it.</summary>
    Task ReplayAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Permanently removes the dead letter.</summary>
    Task DiscardAsync(Guid id, CancellationToken cancellationToken = default);
}

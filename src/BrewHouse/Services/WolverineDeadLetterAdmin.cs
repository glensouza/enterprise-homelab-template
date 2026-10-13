using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;
using Wolverine.Runtime;

namespace BrewHouse.Services;

/// <inheritdoc cref="IDeadLetterAdmin" />
public sealed class WolverineDeadLetterAdmin(IWolverineRuntime runtime) : IDeadLetterAdmin
{
    private IDeadLetters DeadLetters => runtime.Storage.DeadLetters;

    public async Task<DeadLetterPage> ListAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = new DeadLetterEnvelopeQuery
        {
            // Wolverine counts pages from 0 internally; callers (the page) count from 1.
            PageNumber = Math.Max(pageNumber, 1) - 1,
            PageSize = Math.Clamp(pageSize, 1, 200),
        };
        var results = await DeadLetters.QueryAsync(query, cancellationToken);
        var items = results.Envelopes
            .Select(e => new DeadLetterItem(e.Id, e.MessageType, e.ExceptionType, e.ExceptionMessage, e.SentAt, e.Replayable))
            .ToList();
        return new DeadLetterPage(results.TotalCount, items);
    }

    public Task ReplayAsync(Guid id, CancellationToken cancellationToken = default)
        => DeadLetters.ReplayAsync(new DeadLetterEnvelopeQuery([id]), cancellationToken);

    public Task DiscardAsync(Guid id, CancellationToken cancellationToken = default)
        => DeadLetters.DiscardAsync(new DeadLetterEnvelopeQuery([id]), cancellationToken);
}

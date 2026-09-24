# 12. How a Bid Actually Flows

Clicking **Bid** in BrewHouse doesn't get a direct reply. It publishes a durable message
that's handled asynchronously and broadcast back to *every* open browser — not just the
one that clicked — through the shared Garnet SignalR backplane.

See `bid-flow.drawio` for the source diagram (open in [draw.io](https://app.diagrams.net/)
or the VS Code draw.io extension).

## The flow

1. **Browser A clicks "Bid $X"** over its already-open SignalR connection to a Blazor
   circuit on one of the web nodes (e.g. `blazor-web-04`).
2. **The circuit publishes `ProcessBidMessage`** via Wolverine (`IMessageBus.PublishAsync`)
   to the `bids` queue on RabbitMQ. This is durable and asynchronous — the envelope is
   stored before the publish call returns, and nothing waits on a reply. Fire-and-forget
   from the browser's perspective (ADR 79).
3. **RabbitMQ delivers the envelope** to whichever web node's Wolverine listener picks it
   up next — seconds later, not synchronously with step 2. It does not have to be the
   same node that published it.
4. **`ProcessBidHandler` writes the new bid** to Postgres (`brewhouse_db`,
   `CoffeeLots.CurrentBid`) via EF Core.
5. **The handler broadcasts `BidPlaced`** through Garnet, the shared SignalR backplane
   (ADR 04/08) — this is what actually fans a single bid out to every connected browser
   regardless of which web node they're attached to.
6. **Garnet pushes the update to every open circuit**, including Browser A's own — the
   browser that clicked gets its UI updated the exact same way every other viewer does,
   not through a direct response to its click.

## Why this is worth drawing

The click (step 1-2) and the write (step 3-4) are two different requests, not one. Step 2
hands the bid to RabbitMQ and returns immediately; step 6 is what actually updates every
screen. This decoupling is also why bids showed up as isolated single-span traces in
Grafana/Tempo until `docs/01` ADR 105 registered Wolverine's own `ActivitySource` — without
it, the publish (step 2) and the handling (step 3-4) had no visible link between them at
all, let alone a nested one.

**Confirmed live** (`docs/01` ADR 105): after registering `.AddSource("Wolverine")`, a real
bid's `send` span and its `ProcessBidMessage` handler span landed **1.6 seconds apart** in
the same trace — direct proof this hop is genuinely asynchronous, not simulated for this
diagram.

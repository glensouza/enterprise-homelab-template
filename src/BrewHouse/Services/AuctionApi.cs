using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using BrewHouse.Data;
using Scalar.AspNetCore;
using Wolverine;

namespace BrewHouse.Services;

/// <summary>
/// Versioned auction HTTP API in the same process as the Blazor UI. A bid published here is the
/// same <see cref="ProcessBidMessage"/> the button publishes. ADR 134.
/// </summary>
public static class AuctionApi
{
    public static IServiceCollection AddAuctionVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = false;
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddApiExplorer(options =>
        {
            // One OpenAPI document per version, with the URL segment filled in
            // (/api/v1/auctions in v1.json, /api/v2/auctions in v2.json).
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        })
        .AddOpenApi();
        return services;
    }

    public static WebApplication MapAuctionDocs(this WebApplication app)
    {
        app.MapOpenApi().WithDocumentPerVersion();
        app.MapScalarApiReference(options =>
        {
            options.AddDocument("v1", "Auctions v1", "/openapi/v1.json");
            options.AddDocument("v2", "Auctions v2", "/openapi/v2.json");
        });
        return app;
    }

    public static WebApplication MapAuctionApi(this WebApplication app)
    {
        var versionSet = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .HasApiVersion(new ApiVersion(2, 0))
            .ReportApiVersions()
            .Build();

        var v1 = app.MapGroup("/api/v{version:apiVersion}/auctions")
            .WithApiVersionSet(versionSet)
            .MapToApiVersion(new ApiVersion(1, 0))
            .WithTags("Auctions");

        v1.MapGet("/", async (ICoffeeLotReader reader, CancellationToken cancellationToken) =>
            Results.Ok(await reader.GetLotsAsync(cancellationToken)));

        v1.MapGet("/{id:int}", async (int id, ICoffeeLotReader reader, CancellationToken cancellationToken) =>
        {
            var lot = await FindAsync(reader, id, cancellationToken);
            return lot is null ? Results.NotFound() : Results.Ok(lot);
        });

        v1.MapPost("/{id:int}/bids", async (
            int id,
            PlaceBidRequest request,
            ICoffeeLotReader reader,
            IMessageBus bus,
            CancellationToken cancellationToken) =>
        {
            var lot = await FindAsync(reader, id, cancellationToken);
            if (lot is null)
                return Results.NotFound();
            if (!BidRules.BeatsStandingBid(lot.CurrentBid, request.Amount))
                return Results.Conflict(new { message = "Bid does not beat the standing bid." });

            await bus.PublishAsync(new ProcessBidMessage { CoffeeLotId = id, BidAmount = request.Amount });
            return Results.Accepted();
        });

        // One deliberately broken field name, so v1 and v2 can be served at the same time.
        var v2 = app.MapGroup("/api/v{version:apiVersion}/auctions")
            .WithApiVersionSet(versionSet)
            .MapToApiVersion(new ApiVersion(2, 0))
            .WithTags("Auctions");

        v2.MapGet("/", async (ICoffeeLotReader reader, CancellationToken cancellationToken) =>
        {
            var lots = await reader.GetLotsAsync(cancellationToken);
            return Results.Ok(lots.Select(lot => new AuctionV2
            {
                Id = lot.Id,
                Origin = lot.Origin,
                Bid = lot.CurrentBid,
            }));
        });

        return app;
    }

    private static async Task<CoffeeLotSnapshot?> FindAsync(ICoffeeLotReader reader, int id, CancellationToken cancellationToken)
    {
        var lots = await reader.GetLotsAsync(cancellationToken);
        return lots.FirstOrDefault(lot => lot.Id == id);
    }
}

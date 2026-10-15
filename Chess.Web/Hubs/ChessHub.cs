using Chess.Application;
using Chess.Domain;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace Chess.Web.Hubs;

public sealed class ChessHub(IMediator mediator, IOnlineMatchStore matchStore, ILogger<ChessHub> logger) : Hub
{
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        matchStore.Disconnect(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<GameSnapshot> CreateGame()
    {
        try
        {
            logger.LogInformation("Creating an online chess match for connection {ConnectionId}.", Context.ConnectionId);
            var snapshot = await mediator.Send(new CreateOnlineGame(Context.ConnectionId));
            logger.LogInformation("Created online chess match {GameId}.", snapshot.GameId);
            await Groups.AddToGroupAsync(Context.ConnectionId, snapshot.GameId);
            await Clients.Group(snapshot.GameId).SendAsync("GameUpdated", snapshot);
            logger.LogInformation("Published initial state for online chess match {GameId}.", snapshot.GameId);
            return snapshot;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to create an online chess match.");
            throw;
        }
    }

    public async Task<GameSnapshot> JoinGame(string gameId)
    {
        var snapshot = await mediator.Send(new JoinOnlineGame(gameId, Context.ConnectionId));
        await Groups.AddToGroupAsync(Context.ConnectionId, snapshot.GameId);
        await Clients.Group(snapshot.GameId).SendAsync("GameUpdated", snapshot);
        return snapshot;
    }

    public Task<GameSnapshot> GetGame(string gameId) => mediator.Send(new GetOnlineGame(gameId, Context.ConnectionId));

    public async Task<GameSnapshot> Move(string gameId, string move)
    {
        var snapshot = await mediator.Send(new MakeOnlineMove(gameId, Context.ConnectionId, ParseMove(move)));
        await Broadcast(snapshot);
        return snapshot;
    }

    public async Task<GameSnapshot> OfferDraw(string gameId)
    {
        var snapshot = await mediator.Send(new OfferDraw(gameId, Context.ConnectionId));
        await Broadcast(snapshot);
        return snapshot;
    }

    public async Task<GameSnapshot> RespondToDraw(string gameId, bool accept)
    {
        var snapshot = await mediator.Send(new RespondToDraw(gameId, Context.ConnectionId, accept));
        await Broadcast(snapshot);
        return snapshot;
    }

    public async Task<GameSnapshot> ClaimDraw(string gameId, bool threefold)
    {
        var snapshot = await mediator.Send(new ClaimDraw(gameId, Context.ConnectionId, threefold));
        await Broadcast(snapshot);
        return snapshot;
    }

    public async Task<GameSnapshot> Resign(string gameId)
    {
        var snapshot = await mediator.Send(new ResignGame(gameId, Context.ConnectionId));
        await Broadcast(snapshot);
        return snapshot;
    }

    private Task Broadcast(GameSnapshot snapshot) => Clients.Group(snapshot.GameId).SendAsync("GameUpdated", snapshot);

    private static ChessMove ParseMove(string uci)
    {
        if (uci.Length is not (4 or 5)) throw new HubException("Move must be in UCI form, such as e2e4.");
        PieceType? promotion = uci.Length == 5 ? char.ToLowerInvariant(uci[4]) switch
        {
            'q' => PieceType.Queen, 'r' => PieceType.Rook, 'b' => PieceType.Bishop, 'n' => PieceType.Knight,
            _ => throw new HubException("Invalid promotion piece.")
        } : null;
        try { return new ChessMove(Square.Parse(uci[..2]), Square.Parse(uci[2..4]), promotion); }
        catch (FormatException exception) { throw new HubException(exception.Message); }
    }
}
using Chess.Domain;
using MediatR;

namespace Chess.Application;

public sealed record BoardPieceView(string Square, string Color, string Type);
public sealed record CapturedPieceView(string Color, string Type);
public sealed record MoveView(int Number, string White, string? Black);

public sealed record GameSnapshot(
    string GameId,
    string SideToMove,
    string Status,
    string? YourColor,
    IReadOnlyList<BoardPieceView> Pieces,
    IReadOnlyList<string> LegalMoves,
    IReadOnlyList<MoveView> History,
    IReadOnlyList<CapturedPieceView> CapturedByWhite,
    IReadOnlyList<CapturedPieceView> CapturedByBlack,
    string? DrawOfferBy,
    bool WhiteJoined,
    bool BlackJoined,
    bool ThreefoldClaimAvailable,
    bool FiftyMoveClaimAvailable,
    string? ResignedBy);

public sealed class OnlineMatch
{
    public OnlineMatch(string id, ChessGame game, string whiteConnectionId)
    {
        Id = id;
        Game = game;
        WhiteConnectionId = whiteConnectionId;
    }

    public string Id { get; }
    public ChessGame Game { get; }
    public string WhiteConnectionId { get; set; }
    public string? BlackConnectionId { get; set; }
    public PieceColor? DrawOfferBy { get; set; }
    public PieceColor? ResignedBy { get; set; }
    public object Gate { get; } = new();

    public PieceColor RequirePlayer(string connectionId)
    {
        if (connectionId == WhiteConnectionId) return PieceColor.White;
        if (connectionId == BlackConnectionId) return PieceColor.Black;
        throw new InvalidOperationException("This connection is not a player in the game.");
    }
}

public interface IOnlineMatchStore
{
    OnlineMatch Create(string whiteConnectionId);
    OnlineMatch Get(string gameId);
    void Disconnect(string connectionId);
}

public sealed record CreateOnlineGame(string ConnectionId) : IRequest<GameSnapshot>;
public sealed record JoinOnlineGame(string GameId, string ConnectionId) : IRequest<GameSnapshot>;
public sealed record GetOnlineGame(string GameId, string ConnectionId) : IRequest<GameSnapshot>;
public sealed record MakeOnlineMove(string GameId, string ConnectionId, ChessMove Move) : IRequest<GameSnapshot>;
public sealed record OfferDraw(string GameId, string ConnectionId) : IRequest<GameSnapshot>;
public sealed record RespondToDraw(string GameId, string ConnectionId, bool Accept) : IRequest<GameSnapshot>;
public sealed record ClaimDraw(string GameId, string ConnectionId, bool Threefold) : IRequest<GameSnapshot>;
public sealed record ResignGame(string GameId, string ConnectionId) : IRequest<GameSnapshot>;

internal static class SnapshotFactory
{
    public static GameSnapshot Create(OnlineMatch match, string? connectionId)
    {
        var game = match.Game;
        var pieces = new List<BoardPieceView>(32);
        for (var rank = 0; rank < 8; rank++)
        for (var file = 0; file < 8; file++)
        {
            var square = new Square(file, rank);
            if (game.GetPiece(square) is { } piece) pieces.Add(new BoardPieceView(square.ToString(), piece.Color.ToString(), piece.Type.ToString()));
        }

        var plies = game.History.Select(record => record.Notation).ToArray();
        var history = new List<MoveView>((plies.Length + 1) / 2);
        for (var index = 0; index < plies.Length; index += 2)
            history.Add(new MoveView(index / 2 + 1, plies[index], index + 1 < plies.Length ? plies[index + 1] : null));

        return new GameSnapshot(
            match.Id,
            game.SideToMove.ToString(),
            game.Status.ToString(),
            connectionId == match.WhiteConnectionId ? "White" : connectionId == match.BlackConnectionId ? "Black" : null,
            pieces,
            game.LegalMoves().Select(move => move.ToString()).ToArray(),
            history,
            game.History.Where(record => record.MovingPiece.Color == PieceColor.White && record.CapturedPiece is not null)
                .Select(record => new CapturedPieceView(record.CapturedPiece!.Value.Color.ToString(), record.CapturedPiece.Value.Type.ToString())).ToArray(),
            game.History.Where(record => record.MovingPiece.Color == PieceColor.Black && record.CapturedPiece is not null)
                .Select(record => new CapturedPieceView(record.CapturedPiece!.Value.Color.ToString(), record.CapturedPiece.Value.Type.ToString())).ToArray(),
            match.DrawOfferBy?.ToString(),
            !string.IsNullOrEmpty(match.WhiteConnectionId),
            match.BlackConnectionId is not null,
            game.IsThreefoldRepetition,
            game.IsFiftyMoveClaimAvailable,
            match.ResignedBy?.ToString());
    }
}

public sealed class CreateOnlineGameHandler(IOnlineMatchStore store) : IRequestHandler<CreateOnlineGame, GameSnapshot>
{
    public Task<GameSnapshot> Handle(CreateOnlineGame request, CancellationToken cancellationToken)
    {
        var match = store.Create(request.ConnectionId);
        return Task.FromResult(SnapshotFactory.Create(match, request.ConnectionId));
    }
}

public sealed class JoinOnlineGameHandler(IOnlineMatchStore store) : IRequestHandler<JoinOnlineGame, GameSnapshot>
{
    public Task<GameSnapshot> Handle(JoinOnlineGame request, CancellationToken cancellationToken)
    {
        var match = store.Get(request.GameId);
        lock (match.Gate)
        {
            if (string.IsNullOrEmpty(match.WhiteConnectionId))
                match.WhiteConnectionId = request.ConnectionId;
            else if (request.ConnectionId != match.WhiteConnectionId)
            {
                if (match.BlackConnectionId is not null && match.BlackConnectionId != request.ConnectionId)
                    throw new InvalidOperationException("This game already has two players.");
                match.BlackConnectionId = request.ConnectionId;
            }
            return Task.FromResult(SnapshotFactory.Create(match, request.ConnectionId));
        }
    }
}

public sealed class GetOnlineGameHandler(IOnlineMatchStore store) : IRequestHandler<GetOnlineGame, GameSnapshot>
{
    public Task<GameSnapshot> Handle(GetOnlineGame request, CancellationToken cancellationToken)
    {
        var match = store.Get(request.GameId);
        lock (match.Gate) return Task.FromResult(SnapshotFactory.Create(match, request.ConnectionId));
    }
}

public sealed class MakeOnlineMoveHandler(IOnlineMatchStore store) : IRequestHandler<MakeOnlineMove, GameSnapshot>
{
    public Task<GameSnapshot> Handle(MakeOnlineMove request, CancellationToken cancellationToken)
    {
        var match = store.Get(request.GameId);
        lock (match.Gate)
        {
            var player = match.RequirePlayer(request.ConnectionId);
            if (match.Game.SideToMove != player) throw new InvalidOperationException("It is not your turn.");
            match.Game.MakeMove(request.Move);
            match.DrawOfferBy = null;
            return Task.FromResult(SnapshotFactory.Create(match, request.ConnectionId));
        }
    }
}

public sealed class OfferDrawHandler(IOnlineMatchStore store) : IRequestHandler<OfferDraw, GameSnapshot>
{
    public Task<GameSnapshot> Handle(OfferDraw request, CancellationToken cancellationToken)
    {
        var match = store.Get(request.GameId);
        lock (match.Gate)
        {
            var player = match.RequirePlayer(request.ConnectionId);
            if (match.Game.Status is not (GameStatus.InProgress or GameStatus.Check)) throw new InvalidOperationException("The game is already over.");
            match.DrawOfferBy = player;
            return Task.FromResult(SnapshotFactory.Create(match, request.ConnectionId));
        }
    }
}

public sealed class RespondToDrawHandler(IOnlineMatchStore store) : IRequestHandler<RespondToDraw, GameSnapshot>
{
    public Task<GameSnapshot> Handle(RespondToDraw request, CancellationToken cancellationToken)
    {
        var match = store.Get(request.GameId);
        lock (match.Gate)
        {
            var player = match.RequirePlayer(request.ConnectionId);
            if (match.DrawOfferBy is null || match.DrawOfferBy == player) throw new InvalidOperationException("There is no draw offer from your opponent.");
            if (request.Accept) match.Game.AgreeDraw();
            match.DrawOfferBy = null;
            return Task.FromResult(SnapshotFactory.Create(match, request.ConnectionId));
        }
    }
}

public sealed class ClaimDrawHandler(IOnlineMatchStore store) : IRequestHandler<ClaimDraw, GameSnapshot>
{
    public Task<GameSnapshot> Handle(ClaimDraw request, CancellationToken cancellationToken)
    {
        var match = store.Get(request.GameId);
        lock (match.Gate)
        {
            match.RequirePlayer(request.ConnectionId);
            if (request.Threefold) match.Game.ClaimThreefoldRepetition();
            else match.Game.ClaimFiftyMoveRule();
            return Task.FromResult(SnapshotFactory.Create(match, request.ConnectionId));
        }
    }
}

public sealed class ResignGameHandler(IOnlineMatchStore store) : IRequestHandler<ResignGame, GameSnapshot>
{
    public Task<GameSnapshot> Handle(ResignGame request, CancellationToken cancellationToken)
    {
        var match = store.Get(request.GameId);
        lock (match.Gate)
        {
            var resigningPlayer = match.RequirePlayer(request.ConnectionId);
            match.Game.Resign();
            match.ResignedBy = resigningPlayer;
            return Task.FromResult(SnapshotFactory.Create(match, request.ConnectionId));
        }
    }
}
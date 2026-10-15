using Chess.Application;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chess.Web.Services;

public sealed class OnlineGameClient(NavigationManager navigationManager) : IAsyncDisposable
{
    private HubConnection? _connection;
    private string? _gameId;
    private string? _playerColor;

    public event Action<GameSnapshot>? GameUpdated;
    public event Action<bool>? ConnectionChanged;
    public event Action<string>? ErrorOccurred;

    public GameSnapshot? Snapshot { get; private set; }
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task ConnectAsync(string? gameId = null)
    {
        EnsureConnection();
        if (!IsConnected) await _connection!.StartAsync();
        ConnectionChanged?.Invoke(true);
        if (!string.IsNullOrWhiteSpace(gameId)) await JoinGameAsync(gameId);
    }

    public Task<GameSnapshot> CreateGameAsync() => InvokeAndApply("CreateGame");

    public async Task<GameSnapshot> JoinGameAsync(string gameId)
    {
        var snapshot = await _connection!.InvokeAsync<GameSnapshot>("JoinGame", gameId);
        _gameId = snapshot.GameId;
        return Apply(snapshot);
    }

    public Task<GameSnapshot> MoveAsync(string move) => InvokeAndApply("Move", GameId, move);
    public Task<GameSnapshot> OfferDrawAsync() => InvokeAndApply("OfferDraw", GameId);
    public Task<GameSnapshot> RespondToDrawAsync(bool accept) => InvokeAndApply("RespondToDraw", GameId, accept);
    public Task<GameSnapshot> ClaimDrawAsync(bool threefold) => InvokeAndApply("ClaimDraw", GameId, threefold);
    public Task<GameSnapshot> ResignAsync() => InvokeAndApply("Resign", GameId);

    private string GameId => _gameId ?? throw new InvalidOperationException("Join or create a game first.");

    private async Task<GameSnapshot> InvokeAndApply(string method, params object?[] arguments)
    {
        if (!IsConnected) throw new InvalidOperationException("The game server is disconnected.");
        var invocation = arguments.Length switch
        {
            0 => _connection!.InvokeAsync<GameSnapshot>(method),
            1 => _connection!.InvokeAsync<GameSnapshot>(method, arguments[0]),
            2 => _connection!.InvokeAsync<GameSnapshot>(method, arguments[0], arguments[1]),
            _ => throw new ArgumentOutOfRangeException(nameof(arguments), "Unsupported hub method argument count.")
        };
        return Apply(await invocation);
    }

    private void EnsureConnection()
    {
        if (_connection is not null) return;
        _connection = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/gamehub"))
            .WithAutomaticReconnect()
            .Build();
        _connection.On<GameSnapshot>("GameUpdated", snapshot => Apply(snapshot));
        _connection.Reconnecting += exception =>
        {
            ConnectionChanged?.Invoke(false);
            if (exception is not null) ErrorOccurred?.Invoke("Connection interrupted. Reconnecting…");
            return Task.CompletedTask;
        };
        _connection.Reconnected += async _ =>
        {
            try
            {
                ConnectionChanged?.Invoke(true);
                if (_gameId is not null) await JoinGameAsync(_gameId);
            }
            catch (Exception exception) { ErrorOccurred?.Invoke(exception.Message); }
        };
        _connection.Closed += exception =>
        {
            ConnectionChanged?.Invoke(false);
            if (exception is not null) ErrorOccurred?.Invoke(exception.Message);
            return Task.CompletedTask;
        };
    }

    private GameSnapshot Apply(GameSnapshot snapshot)
    {
        _playerColor ??= snapshot.YourColor;
        Snapshot = snapshot with { YourColor = _playerColor };
        GameUpdated?.Invoke(Snapshot);
        return Snapshot;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
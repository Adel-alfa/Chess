using System.Collections.Concurrent;
using System.Security.Cryptography;
using Chess.Application;
using Chess.Domain;

namespace Chess.Infrastructure;

public sealed class InMemoryOnlineMatchStore : IOnlineMatchStore
{
    private readonly ConcurrentDictionary<string, OnlineMatch> _matches = new(StringComparer.OrdinalIgnoreCase);

    public OnlineMatch Create(string whiteConnectionId)
    {
        while (true)
        {
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
            var match = new OnlineMatch(id, ChessGame.NewGame(), whiteConnectionId);
            if (_matches.TryAdd(id, match)) return match;
        }
    }

    public OnlineMatch Get(string gameId) => _matches.TryGetValue(gameId, out var match)
        ? match
        : throw new KeyNotFoundException("Game not found. Check the game ID and try again.");

    public void Disconnect(string connectionId)
    {
        foreach (var match in _matches.Values)
        lock (match.Gate)
        {
            if (match.WhiteConnectionId == connectionId) match.WhiteConnectionId = "";
            if (match.BlackConnectionId == connectionId) match.BlackConnectionId = null;
        }
    }
}
using System.Text;

namespace Chess.Domain;

public sealed class ChessGame
{
    private readonly Piece?[,] _board = new Piece?[8, 8];
    private readonly List<MoveRecord> _history = [];
    private readonly Dictionary<string, int> _positions = new(StringComparer.Ordinal);
    private bool _whiteKingSide = true;
    private bool _whiteQueenSide = true;
    private bool _blackKingSide = true;
    private bool _blackQueenSide = true;
    private Square? _enPassantTarget;
    private int _halfMoveClock;

    private ChessGame() { }

    public static ChessGame NewGame()
    {
        var game = new ChessGame();
        PieceType[] backRank = [PieceType.Rook, PieceType.Knight, PieceType.Bishop, PieceType.Queen, PieceType.King, PieceType.Bishop, PieceType.Knight, PieceType.Rook];
        for (var file = 0; file < 8; file++)
        {
            game._board[file, 0] = new Piece(PieceColor.White, backRank[file]);
            game._board[file, 1] = new Piece(PieceColor.White, PieceType.Pawn);
            game._board[file, 6] = new Piece(PieceColor.Black, PieceType.Pawn);
            game._board[file, 7] = new Piece(PieceColor.Black, backRank[file]);
        }

        game._positions[game.PositionKey()] = 1;
        return game;
    }

    public static ChessGame FromFen(string fen)
    {
        var fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 6) throw new FormatException("FEN must contain six fields.");
        var game = new ChessGame();
        var ranks = fields[0].Split('/');
        if (ranks.Length != 8) throw new FormatException("FEN piece placement must contain eight ranks.");
        for (var fenRank = 0; fenRank < 8; fenRank++)
        {
            var file = 0;
            foreach (var symbol in ranks[fenRank])
            {
                if (char.IsDigit(symbol)) file += symbol - '0';
                else
                {
                    if (file >= 8) throw new FormatException("FEN rank contains too many squares.");
                    var type = char.ToLowerInvariant(symbol) switch
                    {
                        'k' => PieceType.King, 'q' => PieceType.Queen, 'r' => PieceType.Rook,
                        'b' => PieceType.Bishop, 'n' => PieceType.Knight, 'p' => PieceType.Pawn,
                        _ => throw new FormatException($"Invalid FEN piece: {symbol}")
                    };
                    game._board[file++, 7 - fenRank] = new Piece(char.IsUpper(symbol) ? PieceColor.White : PieceColor.Black, type);
                }
            }
            if (file != 8) throw new FormatException("FEN rank does not describe eight squares.");
        }
        if (fields[1] is not ("w" or "b")) throw new FormatException("FEN side to move must be 'w' or 'b'.");
        game.SideToMove = fields[1] == "w" ? PieceColor.White : PieceColor.Black;
        game._whiteKingSide = fields[2].Contains('K');
        game._whiteQueenSide = fields[2].Contains('Q');
        game._blackKingSide = fields[2].Contains('k');
        game._blackQueenSide = fields[2].Contains('q');
        game._enPassantTarget = fields[3] == "-" ? null : Square.Parse(fields[3]);
        if (!int.TryParse(fields[4], out game._halfMoveClock) || game._halfMoveClock < 0)
            throw new FormatException("FEN halfmove clock must be a nonnegative integer.");
        if (!int.TryParse(fields[5], out var fullMoveNumber) || fullMoveNumber < 1)
            throw new FormatException("FEN fullmove number must be a positive integer.");
        if (game._board.Cast<Piece?>().Count(piece => piece == new Piece(PieceColor.White, PieceType.King)) != 1
            || game._board.Cast<Piece?>().Count(piece => piece == new Piece(PieceColor.Black, PieceType.King)) != 1)
            throw new FormatException("FEN must contain exactly one king of each color.");
        game._positions[game.PositionKey()] = 1;
        game.UpdateStatus();
        return game;
    }

    public PieceColor SideToMove { get; private set; } = PieceColor.White;
    public GameStatus Status { get; private set; } = GameStatus.InProgress;
    public IReadOnlyList<MoveRecord> History => _history;
    public Piece? GetPiece(Square square) => square.IsValid ? _board[square.File, square.Rank] : null;
    public IReadOnlyList<ChessMove> LegalMoves(Square? from = null) => AllLegalMoves(from);

    public void MakeMove(ChessMove move)
    {
        if (Status is not (GameStatus.InProgress or GameStatus.Check))
            throw new InvalidOperationException("The game is already over.");

        var legal = AllLegalMoves(move.From).FirstOrDefault(candidate => candidate == move);
        if (legal != move)
            throw new InvalidOperationException("That move is not legal.");

        var moving = _board[move.From.File, move.From.Rank]!.Value;
        var destinationPiece = _board[move.To.File, move.To.Rank];
        var captured = destinationPiece;
        var isEnPassant = moving.Type == PieceType.Pawn && move.From.File != move.To.File && destinationPiece is null;
        if (isEnPassant)
        {
            var capturedRank = move.To.Rank + (moving.Color == PieceColor.White ? -1 : 1);
            captured = _board[move.To.File, capturedRank];
            _board[move.To.File, capturedRank] = null;
        }

        _board[move.From.File, move.From.Rank] = null;
        _board[move.To.File, move.To.Rank] = moving.Type == PieceType.Pawn && move.To.Rank is 0 or 7
            ? new Piece(moving.Color, move.Promotion!.Value)
            : moving;

        if (moving.Type == PieceType.King && Math.Abs(move.To.File - move.From.File) == 2)
        {
            var rookFrom = move.To.File == 6 ? 7 : 0;
            var rookTo = move.To.File == 6 ? 5 : 3;
            _board[rookTo, move.From.Rank] = _board[rookFrom, move.From.Rank];
            _board[rookFrom, move.From.Rank] = null;
        }

        UpdateCastlingRights(moving, move, captured);
        _enPassantTarget = moving.Type == PieceType.Pawn && Math.Abs(move.To.Rank - move.From.Rank) == 2
            ? new Square(move.From.File, (move.From.Rank + move.To.Rank) / 2)
            : null;
        _halfMoveClock = moving.Type == PieceType.Pawn || captured is not null ? 0 : _halfMoveClock + 1;
        _history.Add(new MoveRecord(move, moving, captured, Notation(move, moving, captured)));
        SideToMove = Opponent(SideToMove);

        var key = PositionKey();
        _positions[key] = _positions.GetValueOrDefault(key) + 1;
        UpdateStatus();
    }

    public bool IsThreefoldRepetition => _positions.Values.Any(count => count >= 3);
    public bool IsFiftyMoveClaimAvailable => _halfMoveClock >= 100;

    public void ClaimThreefoldRepetition()
    {
        if (!IsThreefoldRepetition) throw new InvalidOperationException("Threefold repetition is not available.");
        Status = GameStatus.DrawByRepetition;
    }

    public void ClaimFiftyMoveRule()
    {
        if (!IsFiftyMoveClaimAvailable) throw new InvalidOperationException("The fifty-move rule is not available.");
        Status = GameStatus.DrawByFiftyMoveRule;
    }

    public void AgreeDraw() => EndByAgreement();
    public void Resign() => EndByResignation();

    private void EndByAgreement()
    {
        if (Status is GameStatus.Checkmate or GameStatus.Stalemate or GameStatus.Resigned) throw new InvalidOperationException("The game is already over.");
        Status = GameStatus.DrawByAgreement;
    }

    private void EndByResignation()
    {
        if (Status is GameStatus.Checkmate or GameStatus.Stalemate or GameStatus.Resigned) throw new InvalidOperationException("The game is already over.");
        Status = GameStatus.Resigned;
    }

    private IReadOnlyList<ChessMove> AllLegalMoves(Square? from = null)
    {
        if (Status is GameStatus.Checkmate or GameStatus.Stalemate or GameStatus.DrawByAgreement or GameStatus.DrawByRepetition or GameStatus.DrawByFiftyMoveRule or GameStatus.DrawByInsufficientMaterial or GameStatus.Resigned)
            return [];

        var moves = new List<ChessMove>();
        for (var rank = 0; rank < 8; rank++)
        for (var file = 0; file < 8; file++)
        {
            var start = new Square(file, rank);
            if (from is not null && from.Value != start) continue;
            if (_board[file, rank] is not { } piece || piece.Color != SideToMove) continue;
            foreach (var move in PseudoMoves(start, piece))
            {
                ApplyTemporary(move, piece, out var restore);
                var safe = !IsKingInCheck(piece.Color);
                restore();
                if (safe) moves.Add(move);
            }
        }
        return moves;
    }

    private IEnumerable<ChessMove> PseudoMoves(Square from, Piece piece)
    {
        var result = new List<ChessMove>();
        void Add(int file, int rank)
        {
            var to = new Square(file, rank);
            if (!to.IsValid) return;
            var target = _board[file, rank];
            if (target is { Type: PieceType.King }) return;
            if (target is null || target.Value.Color != piece.Color)
            {
                if (piece.Type == PieceType.Pawn && rank is 0 or 7)
                    foreach (var promotion in new[] { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight }) result.Add(new ChessMove(from, to, promotion));
                else result.Add(new ChessMove(from, to));
            }
        }

        if (piece.Type == PieceType.Pawn)
        {
            var direction = piece.Color == PieceColor.White ? 1 : -1;
            var nextRank = from.Rank + direction;
            if (new Square(from.File, nextRank).IsValid && _board[from.File, nextRank] is null)
            {
                Add(from.File, nextRank);
                var homeRank = piece.Color == PieceColor.White ? 1 : 6;
                if (from.Rank == homeRank && _board[from.File, nextRank + direction] is null) Add(from.File, nextRank + direction);
            }
            foreach (var file in new[] { from.File - 1, from.File + 1 })
            {
                var target = new Square(file, nextRank);
                if (target.IsValid && (_board[file, nextRank] is { } victim && victim.Color != piece.Color || _enPassantTarget == target)) Add(file, nextRank);
            }
            return result;
        }

        if (piece.Type == PieceType.Knight)
        {
            foreach (var (df, dr) in new[] { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) }) Add(from.File + df, from.Rank + dr);
            return result;
        }

        if (piece.Type == PieceType.King)
        {
            for (var df = -1; df <= 1; df++)
            for (var dr = -1; dr <= 1; dr++)
                if (df != 0 || dr != 0) Add(from.File + df, from.Rank + dr);

            var homeRank = piece.Color == PieceColor.White ? 0 : 7;
            var kingSide = piece.Color == PieceColor.White ? _whiteKingSide : _blackKingSide;
            var queenSide = piece.Color == PieceColor.White ? _whiteQueenSide : _blackQueenSide;
            if (from == new Square(4, homeRank) && !IsKingInCheck(piece.Color))
            {
                var enemy = Opponent(piece.Color);
                if (kingSide && _board[5, homeRank] is null && _board[6, homeRank] is null && _board[7, homeRank] == new Piece(piece.Color, PieceType.Rook)
                    && !IsSquareAttacked(new Square(5, homeRank), enemy) && !IsSquareAttacked(new Square(6, homeRank), enemy)) Add(6, homeRank);
                if (queenSide && _board[1, homeRank] is null && _board[2, homeRank] is null && _board[3, homeRank] is null && _board[0, homeRank] == new Piece(piece.Color, PieceType.Rook)
                    && !IsSquareAttacked(new Square(3, homeRank), enemy) && !IsSquareAttacked(new Square(2, homeRank), enemy)) Add(2, homeRank);
            }
            return result;
        }

        var directions = piece.Type switch
        {
            PieceType.Bishop => new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) },
            PieceType.Rook => new[] { (1, 0), (-1, 0), (0, 1), (0, -1) },
            _ => new[] { (1, 1), (1, -1), (-1, 1), (-1, -1), (1, 0), (-1, 0), (0, 1), (0, -1) }
        };
        foreach (var (df, dr) in directions)
        {
            var file = from.File + df;
            var rank = from.Rank + dr;
            while (new Square(file, rank).IsValid)
            {
                var target = _board[file, rank];
                if (target is null) Add(file, rank);
                else
                {
                    if (target.Value.Color != piece.Color) Add(file, rank);
                    break;
                }
                file += df;
                rank += dr;
            }
        }
        return result;
    }

    private void ApplyTemporary(ChessMove move, Piece moving, out Action restore)
    {
        var source = _board[move.From.File, move.From.Rank];
        var destination = _board[move.To.File, move.To.Rank];
        var epCaptured = moving.Type == PieceType.Pawn && move.From.File != move.To.File && destination is null
            ? _board[move.To.File, move.To.Rank + (moving.Color == PieceColor.White ? -1 : 1)]
            : null;
        var epRank = move.To.Rank + (moving.Color == PieceColor.White ? -1 : 1);
        Piece? rook = null;
        if (moving.Type == PieceType.King && Math.Abs(move.To.File - move.From.File) == 2)
        {
            var rookFrom = move.To.File == 6 ? 7 : 0;
            var rookTo = move.To.File == 6 ? 5 : 3;
            rook = _board[rookFrom, move.From.Rank];
            _board[rookTo, move.From.Rank] = rook;
            _board[rookFrom, move.From.Rank] = null;
        }
        _board[move.From.File, move.From.Rank] = null;
        _board[move.To.File, move.To.Rank] = moving;
        if (epCaptured is not null) _board[move.To.File, epRank] = null;
        restore = () =>
        {
            _board[move.From.File, move.From.Rank] = source;
            _board[move.To.File, move.To.Rank] = destination;
            if (epCaptured is not null) _board[move.To.File, epRank] = epCaptured;
            if (moving.Type == PieceType.King && Math.Abs(move.To.File - move.From.File) == 2)
            {
                var rookFrom = move.To.File == 6 ? 7 : 0;
                var rookTo = move.To.File == 6 ? 5 : 3;
                _board[rookFrom, move.From.Rank] = rook;
                _board[rookTo, move.From.Rank] = null;
            }
        };
    }

    private bool IsKingInCheck(PieceColor color)
    {
        for (var rank = 0; rank < 8; rank++)
        for (var file = 0; file < 8; file++)
            if (_board[file, rank] == new Piece(color, PieceType.King)) return IsSquareAttacked(new Square(file, rank), Opponent(color));
        return true;
    }

    private bool IsSquareAttacked(Square square, PieceColor byColor)
    {
        for (var rank = 0; rank < 8; rank++)
        for (var file = 0; file < 8; file++)
        {
            if (_board[file, rank] is not { } piece || piece.Color != byColor) continue;
            var df = square.File - file;
            var dr = square.Rank - rank;
            switch (piece.Type)
            {
                case PieceType.Pawn:
                    if (Math.Abs(df) == 1 && dr == (byColor == PieceColor.White ? 1 : -1)) return true;
                    break;
                case PieceType.Knight:
                    if ((Math.Abs(df), Math.Abs(dr)) is (1, 2) or (2, 1)) return true;
                    break;
                case PieceType.King:
                    if (Math.Max(Math.Abs(df), Math.Abs(dr)) == 1) return true;
                    break;
                case PieceType.Bishop:
                case PieceType.Rook:
                case PieceType.Queen:
                    var diagonal = Math.Abs(df) == Math.Abs(dr) && df != 0;
                    var straight = (df == 0) != (dr == 0);
                    if ((piece.Type == PieceType.Bishop && !diagonal) || (piece.Type == PieceType.Rook && !straight) || (piece.Type == PieceType.Queen && !diagonal && !straight)) break;
                    var stepFile = Math.Sign(df);
                    var stepRank = Math.Sign(dr);
                    var pathClear = true;
                    for (int f = file + stepFile, r = rank + stepRank; f != square.File || r != square.Rank; f += stepFile, r += stepRank)
                        if (_board[f, r] is not null) { pathClear = false; break; }
                    if (pathClear) return true;
                    break;
            }
        }
        return false;
    }

    private void UpdateCastlingRights(Piece moving, ChessMove move, Piece? captured)
    {
        if (moving.Type == PieceType.King)
        {
            if (moving.Color == PieceColor.White) _whiteKingSide = _whiteQueenSide = false;
            else _blackKingSide = _blackQueenSide = false;
        }
        if (moving.Type == PieceType.Rook) RemoveRookRight(moving.Color, move.From);
        if (captured is { Type: PieceType.Rook } victim) RemoveRookRight(victim.Color, move.To);
    }

    private void RemoveRookRight(PieceColor color, Square square)
    {
        if (color == PieceColor.White && square == new Square(0, 0)) _whiteQueenSide = false;
        if (color == PieceColor.White && square == new Square(7, 0)) _whiteKingSide = false;
        if (color == PieceColor.Black && square == new Square(0, 7)) _blackQueenSide = false;
        if (color == PieceColor.Black && square == new Square(7, 7)) _blackKingSide = false;
    }

    private void UpdateStatus()
    {
        var check = IsKingInCheck(SideToMove);
        var moves = AllLegalMoves();
        if (moves.Count == 0) Status = check ? GameStatus.Checkmate : GameStatus.Stalemate;
        else if (InsufficientMaterial()) Status = GameStatus.DrawByInsufficientMaterial;
        else if (_positions.Values.Any(count => count >= 5)) Status = GameStatus.DrawByRepetition;
        else if (_halfMoveClock >= 150) Status = GameStatus.DrawByFiftyMoveRule;
        else Status = check ? GameStatus.Check : GameStatus.InProgress;
    }

    private bool InsufficientMaterial()
    {
        var pieces = new List<(Square Square, Piece Piece)>();
        for (var rank = 0; rank < 8; rank++)
        for (var file = 0; file < 8; file++)
            if (_board[file, rank] is { } piece && piece.Type != PieceType.King) pieces.Add((new Square(file, rank), piece));
        if (pieces.Count == 0) return true;
        if (pieces.Any(item => item.Piece.Type is PieceType.Pawn or PieceType.Rook or PieceType.Queen)) return false;
        if (pieces.Count == 1) return true;
        return pieces.All(item => item.Piece.Type == PieceType.Bishop)
            && pieces.Select(item => (item.Square.File + item.Square.Rank) % 2).Distinct().Count() == 1;
    }

    private string PositionKey()
    {
        var builder = new StringBuilder();
        for (var rank = 7; rank >= 0; rank--)
        {
            var empty = 0;
            for (var file = 0; file < 8; file++)
            {
                if (_board[file, rank] is not { } piece) { empty++; continue; }
                if (empty > 0) { builder.Append(empty); empty = 0; }
                var symbol = piece.Type switch { PieceType.King => 'k', PieceType.Queen => 'q', PieceType.Rook => 'r', PieceType.Bishop => 'b', PieceType.Knight => 'n', _ => 'p' };
                builder.Append(piece.Color == PieceColor.White ? char.ToUpperInvariant(symbol) : symbol);
            }
            if (empty > 0) builder.Append(empty);
            builder.Append('/');
        }
        builder.Append(SideToMove == PieceColor.White ? 'w' : 'b').Append(':')
            .Append(_whiteKingSide ? 'K' : '-').Append(_whiteQueenSide ? 'Q' : '-')
            .Append(_blackKingSide ? 'k' : '-').Append(_blackQueenSide ? 'q' : '-').Append(':');
        builder.Append(HasLegalEnPassantCapture() ? _enPassantTarget?.ToString() : "-");
        return builder.ToString();
    }

    private bool HasLegalEnPassantCapture()
    {
        if (_enPassantTarget is not { } target) return false;
        for (var file = target.File - 1; file <= target.File + 1; file += 2)
        {
            var from = new Square(file, target.Rank + (SideToMove == PieceColor.White ? -1 : 1));
            if (!from.IsValid || _board[file, from.Rank] != new Piece(SideToMove, PieceType.Pawn)) continue;
            var move = new ChessMove(from, target);
            ApplyTemporary(move, _board[file, from.Rank]!.Value, out var restore);
            var legal = !IsKingInCheck(SideToMove);
            restore();
            if (legal) return true;
        }
        return false;
    }

    private static PieceColor Opponent(PieceColor color) => color == PieceColor.White ? PieceColor.Black : PieceColor.White;

    private string Notation(ChessMove move, Piece moving, Piece? captured)
    {
        if (moving.Type == PieceType.King && Math.Abs(move.To.File - move.From.File) == 2) return move.To.File == 6 ? "O-O" : "O-O-O";
        var prefix = moving.Type == PieceType.Pawn ? "" : moving.Type switch { PieceType.Knight => "N", PieceType.Bishop => "B", PieceType.Rook => "R", PieceType.Queen => "Q", _ => "K" };
        var capture = captured is not null ? "x" : "";
        var promotion = move.Promotion is null ? "" : $"={move.Promotion.Value.ToString()[..1].ToUpperInvariant()}";
        return $"{prefix}{(captured is not null && moving.Type == PieceType.Pawn ? ((char)('a' + move.From.File)).ToString() : "")}{capture}{move.To}{promotion}";
    }
}
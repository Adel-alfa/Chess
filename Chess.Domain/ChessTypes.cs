namespace Chess.Domain;

public enum PieceColor { White, Black }
public enum PieceType { King, Queen, Rook, Bishop, Knight, Pawn }
public enum GameStatus { InProgress, Check, Checkmate, Stalemate, DrawByAgreement, DrawByRepetition, DrawByFiftyMoveRule, DrawByInsufficientMaterial, Resigned }

public readonly record struct Square(int File, int Rank)
{
    public bool IsValid => File is >= 0 and < 8 && Rank is >= 0 and < 8;

    public static Square Parse(string value)
    {
        if (value.Length != 2 || value[0] is < 'a' or > 'h' || value[1] is < '1' or > '8')
            throw new FormatException($"Invalid square: {value}");

        return new Square(value[0] - 'a', value[1] - '1');
    }

    public override string ToString() => IsValid ? $"{(char)('a' + File)}{Rank + 1}" : "--";
}

public readonly record struct Piece(PieceColor Color, PieceType Type);
public readonly record struct ChessMove(Square From, Square To, PieceType? Promotion = null)
{
    public override string ToString() => $"{From}{To}{(Promotion is null ? "" : Promotion.Value.ToString()[..1].ToLowerInvariant())}";
}

public sealed record MoveRecord(ChessMove Move, Piece MovingPiece, Piece? CapturedPiece, string Notation);
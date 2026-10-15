# Architecture Contract

This document defines the ownership boundaries for Endgame Chess. Changes should preserve these dependencies and keep game invariants in the domain.

## Dependency direction

`Chess.Web` is the composition root and may depend on Application, Infrastructure, and Domain. Infrastructure implements interfaces declared by Application. Application depends on Domain. Domain has no dependency on the other projects or on web, persistence, or transport frameworks.

## Domain ownership

`ChessGame` is the authoritative aggregate. It owns board mutation, legal move generation, turn order, castling rights, en-passant state, promotion, check/checkmate/stalemate, draw conditions, resignation state, and move history. Every move entering from either UI must pass through this aggregate. UI components may render supplied legal moves, but must not calculate or override legality.

Threefold repetition and the fifty-move rule are claims. Fivefold repetition and the seventy-five-move rule end the game automatically. Insufficient material and checkmate/stalemate are resolved by the domain. Position repetition keys include side-to-move, castling rights, and legally available en-passant rights.

## Application and transport

MediatR requests define online match operations. Application handlers enforce player/turn checks and serialize mutations under the match lock. `GameSnapshot` is the transport view; it is derived from domain state and contains legal move coordinates for presentation. SignalR methods in `ChessHub` forward requests and broadcast snapshots; they do not implement chess rules.

Local play owns an in-process `ChessGame` in its Blazor page. Online play uses the shared application handlers and `IOnlineMatchStore`. The online store is currently an in-memory development implementation and is not suitable for durable or multi-instance production deployment.

## UI contract

`ChessBoard` is shared between local and online modes. It displays supplied state, handles input, and emits move intent. The domain remains authoritative. Promotion choice is explicit, and the board may be flipped without changing coordinates or game state.

# LAN Reconnect Notes

## Current Behavior

- During a running match, the host keeps a disconnected client's logical seat.
- The lobby/player list marks the disconnected seat as `Offline`.
- If the same player joins again with the same player name, the host maps the new transport peer back to the reserved logical peer.
- The reconnected client receives the current match snapshot, including turn owner, waiting window, hand state, board state, and battle log.
- The client caches the last room address, player name, selected general, and port for the lifetime of the process.
- An unexpected host disconnect or failed connection opens the global ink-style system dialog with the address, localized reason, and a `重新连接` action.
- An intentional `LeaveSession()` clears the reconnect address and suppresses false disconnect prompts.
- `LanDisconnectNotice` distinguishes connection failure from host loss and records whether the interruption happened during a match.

## Playtest Steps

1. Start a two-player LAN match.
2. Close the client window during the match.
3. On the host, confirm that the player remains listed but changes to `Offline`.
4. On the client, choose `重新连接` in the disconnect dialog.
5. Confirm that the client returns with the same player name and general.
6. Confirm that the client returns to the same seat and can continue if it is their turn or response window.

## Known Limit

- Reconnect currently matches by player name. If the player changes their name, the host treats them as a new connection.
- Reconnect metadata is process-local and is not persisted after the game exits.
- Host migration is not implemented. If the host process exits, the original room cannot continue without that host returning.

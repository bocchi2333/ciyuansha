# LAN Multiplayer Architecture

Current direction:

- `LanMultiplayerManager` is the LAN session autoload.
- `GameManager` remains the gameplay state machine and action entry point.
- Gameplay is server-authoritative: clients send play requests, the host validates and queues actions.

Core files:

- `Scripts/Networking/LanMultiplayerManager.cs`
- `Scripts/Networking/LanDisconnectNotice.cs`
- `Scripts/Networking/LanPlayerInfo.cs`
- `Scripts/Networking/LobbySnapshot.cs`
- `Scripts/Networking/MatchStartInfo.cs`
- `Scripts/Networking/MatchStateSnapshot.cs`
- `Scripts/Networking/NetworkCharacterState.cs`
- `Scripts/Networking/NetworkHandCardState.cs`
- `Scripts/Networking/NetworkPlayCommand.cs`
- `Scripts/Gameplay/Core/GameManager.cs`
- `Scripts/Gameplay/Cards/CardType.cs`
- `Scripts/Gameplay/Cards/CardInstance.cs`
- `Scripts/Gameplay/Generals/GeneralDefinition.cs`
- `Scripts/Gameplay/Generals/GeneralCatalog.cs`
- `Scripts/Gameplay/Generals/GeneralArtRegistry.cs`
- `Scripts/Gameplay/Skills/CharacterSkill.cs`
- `Scripts/Gameplay/Skills/SkillFactory.cs`
- `Scripts/UI/MatchBoardManager.cs`
- `Scripts/UI/HandZonePanel.cs`
- `Scripts/UI/TargetSelectionPanel.cs`
- `Scripts/UI/MatchStatusPanel.cs`
- `Scripts/UI/BattleLogPanel.cs`
- `Scripts/UI/GeneralArtMapperPanel.cs`
- `Scripts/UI/SystemDialogPanel.cs`

Recommended autoloads:

- `/root/GameUserSettings`
- `/root/GameManager`
- `/root/LanMultiplayerManager`

Current LAN flow:

1. Host calls `HostGame(playerName, characterId)`.
2. Client calls `JoinGame(address, playerName, characterId)`.
3. Client registers itself through RPC.
4. Host broadcasts lobby snapshots to every peer.
5. Host starts the match after all players are ready.
6. Host broadcasts `MatchStartInfo`, including turn order and the starting peer.
7. `GameManager` begins a server-authoritative match and rotates turns locally on every peer.
8. `MatchBoardManager` builds visual player boards and registers `PlayerCharacter` nodes by peer ID.
9. Clients submit `NetworkPlayCommand` objects to the host.
10. Host validates the request and converts it into local `GameAction` objects.
11. Host broadcasts `MatchStateSnapshot` so clients refresh HP, hand size, phase, and current turn owner from authority.
12. Local HUD uses hand card instance IDs and target buttons to submit card-based play requests to the host.
13. Only the owning client receives full hand card details in snapshots; opponents only see hand counts.
14. When only one alive `PlayerCharacter` remains, the host ends the match and replicates the winner/result message.
15. The host also replicates a compact battle log so every client can read the same action history.
16. Character nodes can now be initialized from `GeneralDefinition`, which may grant passive skills through the skill factory.
17. General portraits are resolved through `GeneralArtRegistry`, which can override catalog defaults through `Data/general_art_mapping.json`.
18. The host now owns a real draw pile and discard pile, deals opening hands, draws from the deck each turn, and auto-discards down to the current HP hand limit during `DiscardPhase`.
19. `Slash` now uses alive turn order to calculate circular distance, and both the UI and the host validate target legality against attack range before damage is queued.
20. After damage resolves, characters now enter a dying check at `0` HP; the current prototype supports automatic self-rescue first, then automatic rescue attempts from other alive players with `Peach`, before final defeat is declared.
21. Weapon and armor cards can now be equipped onto a character; weapons currently increase attack range, armor currently reduces incoming physical damage, and replaced equipment is moved to the discard pile.
22. Damage targeting now opens a replicated response-window state before resolution, so clients can see which peer is currently expected to answer a response.
23. Presentation-only rule cues are replicated as sequenced `NetworkPresentationEvent` entries in `MatchStateSnapshot.PresentationEvents`, so host and clients play the same card, damage, healing, response, and defeat feedback without changing authoritative rules.

Presentation synchronization:

- The host is the only producer of presentation sequence numbers.
- Snapshots retain the latest 32 cues for packet-loss tolerance.
- Clients deduplicate by `Sequence`; the first snapshot of an already running match establishes a baseline instead of replaying old effects.
- `BattleFxDirector` consumes the replicated cues, while gameplay skills continue consuming `RuleEventContext` locally on the host.
- Presentation fields are additive JSON data and do not change `NetworkPlayCommand` validation.

Transport and snapshot scheduling:

- Client play and response commands use reliable transfer channel `1`; authoritative snapshots remain on channel `0` so large state updates cannot queue ahead of player input.
- Repeated `OnStateSyncRequested` calls are coalesced by `LanMultiplayerManager`; normal gameplay sends at most one final authoritative snapshot per frame and per client.
- Explicit reconnect and initial-match synchronization may still send immediately because those paths must establish a complete baseline before input.
- Debug builds log command send/accept/reject results, while `SubmitPlayCommand` now reports an immediate `RpcId` failure instead of always returning success.
- `Scripts/Test/RunLanSmokeScenario.ps1` can launch one host and 1-3 clients for a single scenario; three-player `chain_fire` and `nullify_arrow_target` are the current multi-client baselines.

Reconnect and interruption handling:

- `LanMultiplayerManager` stores the last client address, player name, general, and port while the process remains open.
- `OnUnexpectedDisconnect` emits `LanDisconnectNotice` after cleanup, while intentional leave clears reconnect metadata and suppresses disconnect callbacks.
- `ReconnectLastSession()` reuses `JoinGame`; the host's existing same-name reservation maps the new transport peer back to the original logical seat.
- `SystemDialogPanel` is global to lobby and match UI. It presents connection failure, host loss, reconnect, and safe-exit actions without changing `NetworkPlayCommand`.
- Offline transport IDs resolve to `0`; connection truth is based on `LanSessionState` plus a live peer, not Godot's placeholder offline peer.

Current gameplay command support:

- `BasicAttack`
- `EndPlayPhase`
- `UsePeach`
- `UseEquipment`

Important assumptions:

- All peers use the same node path for `LanMultiplayerManager`, because Godot RPC matching depends on node path consistency.
- `GameManager.RegisterPeerCharacter(peerId, character)` must be called after scene spawn so the host can resolve gameplay targets by peer ID.
- `DamageAction` still needs explicit responders attached if a target should auto-play `DodgeAction`.
- `PlayPhaseInputBridge` now assumes network play should go through `NetworkPlayCommand`, not direct local action injection.
- `MatchBoardManager` currently creates demo player boards with placeholder stats and auto-dodge behavior for remote players.
- In LAN mode, only the host advances gameplay flow inside `GameManager._Process`; clients follow replicated snapshots.
- The current prototype deck now generates `Slash`, `Dodge`, `Peach`, `Weapon`, and `Armor`.
- Discard phase currently auto-discards oldest hand cards until the active player reaches the HP-based hand limit.
- Attack range currently defaults to `1` on `PlayerCharacter.BaseAttackRange`.
- Weapons currently provide range through `PlayerCharacter.EffectiveAttackRange`.
- `Training Armor` currently reduces incoming physical damage by `1`.
- Response windows are currently informational and replicated through match state; manual out-of-turn UI submission is the next layer to build on top.
- Dying resolution is currently automatic: self-save first, then other alive players try to save in turn order with `Peach`.
- Defeated characters stay in `_turnOrder` for identity, but turn advancement now skips over them.
- `PlayerResponder` now consumes a real `Dodge` hand card before responding to a `DamageAction`.
- `MatchStatusPanel` is a lightweight HUD widget that shows current phase, current turn peer, and final result.
- `MatchStatusPanel` now also shows draw pile and discard pile counts.
- `BattleLogPanel` shows the replicated action history, including draw, Slash, Dodge, Peach, and win messages.
- The general catalog is now seeded with recognized entries from `E:\次元杀\武将卡牌`, and those defaults can still be remapped through `GeneralArtMapperPanel`.
- Sample skills currently include automatic Dodge response and bonus draw at turn start.

Suggested next build steps:

1. Add a dedicated two-process reconnect smoke scenario that kills and restarts the client mid-match.
2. Validate 3-4 player reconnect while another player owns the active response window.
3. Decide whether host migration is required after the first external playtest round.
4. Continue keeping client commands server-authoritative and presentation effects snapshot-driven.

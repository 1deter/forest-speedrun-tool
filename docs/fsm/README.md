# PlayMaker FSMs as text

Exported live with the bridge's `fsm` command (`src/Game/FsmExport.cs`,
v0.24.225) from Slot 1 on 2026-10-03. Each file: variables (with their
values at export time), events, global transitions, then every state with
its transitions (`on EVENT -> state`) and actions with their serialized
fields. A field bound to an FSM variable reads `{name}`; `to GameObject
<obj> fsm <name>` is where a SendEvent goes.

| File | Object | What |
|---|---|---|
| `player-controlFSM.txt` | `player/player_BASE` | the player's actions (`pmControl`): attacks, blocks, swim, climb, jump, held items |
| `player-damageFSM.txt` | same | hits taken (`pmDamage`) |
| `player-staminaFSM.txt`, `player-rotatePlayerFSM.txt`, `player-noiseDetectFSM.txt`, `player-tempBlockManagerFSM.txt` | same | stamina, body rotation, noise, blocking |
| `mutant-*.txt` | `mutant_male_BASE` (pooled; the female's four are identical) | cannibal brain, combat, encounters, sleeping |
| `rabbit-aiBaseFSM.txt`, `lizard-aiBaseFSM.txt` | animals | small-animal AI |
| `megan-*.txt` | `girlMutant(Clone)/girl_base`, transformed (savestate `ruben-megan`, Normal, 2026-10-03) | the boss fight: combat (94 states), motor, alert; read in `knowledge/cards/megan-boss.md` |

**The cannibals' `global_motorFSM`, `global_visionFSM`, `action_searchFSM`
and `action_inTreeFSM` do not exist** - a live, awake cannibal carries only
the four exported here (bridge, 2026-10-03); events sent to the others are
lost, and their work is C# (`mutantSearchFunctions`, `mutantAI`,
`pmSearchReplace`). The combat FSM is a dispatcher: many states only start
a `pmCombatReplace` coroutine - read that code for what a state does
(game-notes *Cannibal AI*). Other spawned-only FSMs - export them in a session that
reaches them (`type PlayMakerFSM all` lists what exists, `fsm <target>
children`).

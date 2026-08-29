# 72 Hours Challange Game by Mırmır

A third-person action / light horror game built from scratch for a 72-hour game jam challenge. You wake up in a dungeon: clear out the goons, get past the twin-blade sentry, bring down the boss, and escape by climbing the hill under a rain of rolling boulders.

**Unity 6000.5.5f1 · URP · New Input System · Windows**

[**▶ Play the demo**](https://onursessiz.github.io) — the build is on my site under *Demo Games*.

![The corridor leading to the boss room — statue, torches, quest panel and ability indicators](Docs/boss-corridor.png)

<p align="center">
  <img src="Docs/boss.png" width="70%" alt="Face to face with the boss in the boss room">
</p>

| | |
|---|---|
| Development time | 16–19 August 2026 (72 hours) |
| Version | v1.0 Alpha |
| Genre | Third-person action, combo combat, short campaign |
| Length | ~10-15 minutes |

---

## Gameplay

The game takes place in a single scene (`Assets/Scenes/SampleScene.unity`) and follows a linear four-stage progression. Each room stays locked until the previous stage is finished.

| # | Stage | Objective |
|---|---|---|
| 1 | **Goon Room** | As you enter, the whistler goon blows his whistle and every sleeping goon in the room wakes at once. Clear them all. |
| 2 | **Ocean Room** | Get past the BlackSwordsman — he never moves from his spot, but his spinning 360° attack launches you into the sea. Falling in the water is an instant kill. |
| 3 | **Boss Room** | A jump scare greets you at the door while the statues in the corridor turn to face you. Take the boss down with combos and the flying kick. |
| 4 | **Rock Trap** | Climb the slope through a rain of falling boulders and reach the top → closing sequence. |

The quest panel in the bottom-right shows the current objective and a live count of the enemies left.

### Controls

| Key | Action |
|---|---|
| `W A S D` | Move (relative to the camera) |
| `Mouse` | Camera |
| `Space` | Jump |
| `Left Click` | Attack — press repeatedly to chain the combo |
| `Space` + `Left Click` | **Flying kick** — attack while airborne; carries you forward and stuns the boss |
| `Right Click` | Dodge roll — you take no damage for its duration (i-frames) |
| `Left Shift` | Dash |

Dash has a 1s cooldown, the dodge roll 1.5s; both have on-screen indicators. Dropped oranges restore health.

---

## Running it

**Ready-made build:** available from [onursessiz.github.io](https://onursessiz.github.io) under *Demo Games* (the build folder is not part of this repository).

**From source:**

```bash
git clone https://github.com/OnurSessiz/72HoursChallangeGameByMrmr.git
```

1. Unity Hub → Add → pick the folder. The editor version must be **6000.5.5f1** (URP 17.5 and Input System 1.19 are tied to it).
2. Open `Assets/Scenes/SampleScene.unity` and hit Play.

To build: `File → Build Profiles → Windows`, with `SampleScene` as the only scene.

---

## Technical overview

### Player — state machine

Player control is written as a state machine rather than a pile of `if` statements. [`PlayerController`](Assets/Scripts/Player/PlayerController.cs) is the brain: it holds the active state, hands the shared references and every tuning value down to the states, and keeps the cooldown timestamps outside the state objects so they survive being re-entered.

| State | Job |
|---|---|
| [`LocomotionState`](Assets/Scripts/Player/LocomotionState.cs) | Default: camera-relative movement, smooth rotation, and where transitions into every other state are triggered |
| [`AttackState`](Assets/Scripts/Player/AttackState.cs) | Multi-step combo; step transitions are driven by Animation Events, with a timeout as a fallback if an event is missing |
| [`AirAttackState`](Assets/Scripts/Player/AirAttackState.cs) | Flying kick: horizontal velocity is not braked, and the damage window stays open for the whole flight |
| [`DashState`](Assets/Scripts/Player/DashState.cs) | Short high-speed burst |
| [`DodgeState`](Assets/Scripts/Player/DodgeState.cs) | Direction-locked roll; i-frames open on Enter and close on Exit |
| [`LaunchedState`](Assets/Scripts/Player/LaunchedState.cs) | Knockback. It has to be its own state: Locomotion calls `MovePosition` every FixedUpdate, which otherwise wiped the impulse on the next physics frame |

Input is broadcast as events through [`PlayerInputReader`](Assets/Scripts/Player/PlayerInputReader.cs); states subscribe in `Enter` and unsubscribe in `Exit`. The camera pivot ([`PlayerLook`](Assets/Scripts/Player/PlayerLook.cs)) is deliberately **not** a child of the character — if it were, the movement direction would drift as the character rotated.

### Combat

Hits fire from an Animation Event on the strike frame; [`PlayerAttack`](Assets/Scripts/Player/PlayerAttack.cs) then runs a cone-limited sphere cast in front of the player. Every `IDamageable` it finds takes damage exactly **once** per combo step, so enemies with several colliders don't get hit multiple times. Combo steps are tuned individually — the final hit is heavier, wider, and comes with hit-stop.

All damage flows through a single contract: [`IDamageable`](Assets/Scripts/Combat/IDamageable.cs). [`Health`](Assets/Scripts/Combat/Health.cs) owns the health of enemies, the boss and breakables; [`PlayerHealth`](Assets/Scripts/Player/PlayerHealth.cs) owns the player's. The boss is also `IStunnable`: a flying kick staggers it, and while stunned it won't walk, turn or attack. Instant-death volumes like the ocean, falling objects and damage-over-time zones are all set up with the same [`DamageSource`](Assets/Scripts/World/DamageSource.cs).

### Progression

[`GameProgress`](Assets/Scripts/Progression/GameProgress.cs) is the single authority — it alone knows which stage is done. Completing an objective raises `StageCompleted`, and the [`StageGate`](Assets/Scripts/Progression/StageGate.cs) waiting on that stage disables its colliders to open the room. Objectives come in two flavours: [`EnemyClearObjective`](Assets/Scripts/Progression/EnemyClearObjective.cs) (when everything on the list is dead) and [`ReachPointObjective`](Assets/Scripts/Progression/ReachPointObjective.cs) (when the player enters a trigger). With `enforceOrder` on, a stage cannot count as complete before the ones ahead of it — a second layer of safety on top of the collider locks.

Once the final stage lands, [`EndingSequence`](Assets/Scripts/Progression/EndingSequence.cs) takes control away from the player, cuts through the closing cameras in order, and ends the game.

### Atmosphere

- [`HirtRoomAmbush`](Assets/Scripts/Enemy/HirtRoomAmbush.cs) — cinematic camera, the whistle, then the whole room waking up together
- [`BossRoomTurn`](Assets/Scripts/Boss/BossRoomTurn.cs) — the jump scare: scare animation, camera shake, player locked in place
- [`StatueWatcher`](Assets/Scripts/Boss/StatueWatcher.cs) — statues that turn toward the player, with smooth tracking, sudden twitching, and a Weeping Angel mode that *only* turns while you're not looking
- [`RollingBallSpawner`](Assets/Scripts/World/RollingBallSpawner.cs) + [`RollingBall`](Assets/Scripts/World/RollingBall.cs) — boulders spawned in the sky that roll down the slope, cleaning themselves up when their lifetime runs out, when they fall off the map, or when they get stuck

### Editor tools

Most of the scene setup runs through menu commands under `Assets/Editor` instead of manual dragging — so the same wiring didn't have to be redone by hand over 72 hours:

```
Tools/Progression/Setup Game Flow      → wires up GameProgress + three gates + three objectives
Tools/Player/Create Health Bar         → player health bar
Tools/Player/Create Ability Buttons    → dash/dodge cooldown indicators
Tools/UI/Create Quest HUD              → quest panel
Tools/Combat/Setup Player Attack       → attack components
Tools/Enemy/Create Enemy Animator      → enemy animator controller
Tools/Boss/Create Boss Animator        → boss animator
Tools/World/Create Rolling Ball Trap   → creates the spawner + trigger volume and links them
Tools/World/Create Orange Pickup       → health pickup + VFX slots
Tools/World/Create Ocean Death Zone    → ocean instant-death volume
```

The full list lives in the `Tools/` menu. What each setup command does is also documented at the top of the runtime script it belongs to.

---

## Project layout

```
Assets/
├─ Scenes/SampleScene.unity     the single game scene
├─ Scripts/
│  ├─ Player/                   state machine, input, attacks, health, camera
│  ├─ Enemy/                    goon chasing, BlackSwordsman, goon-room ambush
│  ├─ Boss/                     boss fight, jump scare, statues
│  ├─ Combat/                   Health, IDamageable
│  ├─ Progression/              GameProgress, gates, objectives, ending
│  ├─ World/                    damage sources, rolling boulders, loot, pickups
│  └─ UI/                       health bars, quest panel, cooldown indicators
├─ Editor/                      scene setup tools (Tools/ menu)
├─ Animations/                  animator controllers
├─ Models/                      character, boss and enemy models with their textures
├─ Prefabs/                     OrangePickup, RollingBall
└─ 3rdPartyAssets/              ready-made packages (see below)
```

In-code documentation is written in Turkish: every script opens with what it does, why it was written that way, and how to set it up in the scene, as XML doc comments.

---

## Third-party assets

The character, boss and enemy models along with the animations were made for this jam. The free Asset Store packages used for the environment and effects:

- **LowPolyDungeons Lite** — dungeon environment
- **KE Statues Lite** — statues
- **RPG Tiny Fantasy Forest PBR** — exterior / island
- **Playground Apocalypse** — environment props
- **Eric VFX Studio – Free Game VFX** — effects
- **fabreffect – Free Slash VFX** — sword trails
- **PolyOne – Free Fruits** — health pickup (orange)

---

## Known limitations

v1.0 Alpha is where it stood when the 72 hours ran out:

- No main menu, settings menu or save system — dying just reloads the scene
- Sound design is unfinished: several scripts (`StatueWatcher`, `BossRoomTurn`, `StageGate`) have clip slots wired up but empty
- Keys are fixed; no rebinding
- Keyboard + mouse only; gamepad bindings were never set up
- Windows is the only build target

---

## Development log

| Day | Work |
|---|---|
| Aug 16 | Project setup, URP |
| Aug 17 | Scripts, first model, level plan, animations, map + textures, boss room, jump scare |
| Aug 18 | Combat mechanics, level design, boss fight fixes, goon room, BlackSwordsman |
| Aug 19 | v1.0 Alpha |

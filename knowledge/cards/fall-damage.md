---
id: fall-damage
title: Fall damage and the slide cancel
aliases: fall damage, fall damage cancel, slide cancel, fall cancel, body slide, slide on bodies, sliding on bodies, cave 6 drop, keycard drop, fall death, fell too long, 3.8 seconds, hard landing, landing stun, landing lag, prevVelocity, fall height
tags: physics, tech, damage, caves
confidence: code
checked: 2026-10-03
sources: game-notes "Deaths" (What a landing hurts from), "Speedrun tech and the endgame gate" (Fall damage and the slide cancel; The game's timers and pauses; the runners' words mapped); sxczurass's Creative Bombless Any% Guide (2025); docs/run-mode.md "Banned moves: detection" (fall damage cancel)
related: player-physics, deaths-and-revives, pausing-and-game-time, movement-tricks
code: FirstPersonCharacter.HandleLanded, FirstPersonCharacter.OnCollisionEnterProxied, FirstPersonCharacter.HandleStartJumping, FirstPersonCharacter.fallDamageTimer, playerHitReactions.doHardfallRoutine, PlayerStats.Hit
---

# Fall damage and the slide cancel

The game does not measure how far you fell. When you land, it looks at
**one stored number: the vertical speed of the most recent collision that
*started*** (`prevVelocity`, written in `OnCollisionEnter`). If that is over
28 m/s you take damage. So if the last *new* contact before you count as
"landed" was a slow one - grazing a steep surface, sliding onto a body,
touching a seam between two colliders - the game judges your landing on
that small number and you take nothing, however fast you were really
falling. That is the slide cancel.

## How runners do it

The known use: the **Cave 6 drop to the keycard** - runners "slide on the
bodies to not get fall damage" (sxczurass's guide). The steep colliders of
the bodies piled at the bottom turn the fall into a slide, so the landing is
judged on the slide's slow contact [runner + code; not reproduced live].

## Why it works

Two pieces of `FirstPersonCharacter`:

**1. `prevVelocity` is written on every new collision, and nowhere else:**

```csharp
// OnCollisionEnterProxied - any collider the player starts touching
prevVelocity   = coll.relativeVelocity.y;
prevVelocityXZ = coll.relativeVelocity;  prevVelocityXZ.y = 0f;
```

**2. Landing (`HandleLanded`, on the frame `Grounded` turns true in
`FixedUpdate`) judges that stored value:**

```csharp
if ((onShellRide || flyingGlider) && prevVelocityXZ.magnitude > 32f) noDamage = true;
if (prevVelocity > 28f && !noDamage && allowFallDamage && jumpingTimer > 0.75f)
{
    if (!jumpLand && !Clock.planecrash)
    {
        int damage = (int)(prevVelocity * 0.9f * (prevVelocity / 27.5f));
        if (jumpingTimer > 3.8f && !flyingGlider) damage = 1000;   // "fell too long"
        // (shell ride: 5 s limit, 17 damage; glider disconnect: 12)
        Stats.Hit(damage, ignoreArmor: true);
        // survived: hard landing - see below
    }
}
```

So a landing does **no damage at all** - not even the 3.8 s death - unless
*all* of these hold:
- `prevVelocity` > 28 m/s (the last collision *enter*'s vertical speed);
- `allowFallDamage` - armed 0.35 s after leaving the ground
  (`Invoke("fallDamageTimer", 0.35f)` in `HandleStartJumping`) and cleared
  on every grounded physics step;
- `jumpingTimer` > 0.75 s of air time;
- not already in a landing (`jumpLand`), not the opening plane crash;
- not riding a shell / gliding faster than 32 m/s.

**Ways the stored value ends up small:**
- **Contact kept while sliding**: you touch a steep surface (a body, a
  rock face) at a shallow angle - a slow collision enter - then slide down
  it keeping contact. No new enter happens, so when `Grounded` finally turns
  true the stored value is still the slide's small one.
- **A slow enter just before the landing**: grazing a seam between two
  colliders, an edge, a small prop - any new contact at low vertical speed
  overwrites the fast value.

**Steep terrain alone does not do it** [live]: the game holds the player on
a 55-80° terrain slope at ~3 m/s, so no speed builds while you keep
contact - you cannot "slide-fall" down terrain.

## Numbers

| What | Value |
|---|---|
| Damage threshold | `prevVelocity` > **28 m/s** |
| Damage | **0.9 x v² / 27.5** (28 m/s -> 25; 40 m/s -> 52; 50 m/s -> 81) |
| Max fall speed | 55.43 m/s (the speed cap - `player-physics`) -> **100 damage**, i.e. exactly lethal from full health (100) [inferred: calculated] |
| "Fell too long" | **1000 damage** (certain death) when air time > **3.8 s** (5 s on a shell) |
| Air time needed for any damage | > 0.75 s (and > 0.35 s to arm) |
| Gravity | 26 m/s²: 28 m/s is reached after ~1.1 s / ~15 m of free fall; 55 m/s after ~2.1 s / ~58 m [inferred: calculated] |

Fall damage ignores armour (`ignoreArmor: true`). `Hit` itself is skipped
during endgame cutscenes, cave-entrance animations, the plane crash and the
intro.

## Air time and pausing

`jumpingTimer` counts `Time.deltaTime` from the moment you leave the
ground. So:
- **The pause menu stops it** - pausing mid-fall does not add air time.
- **A long frame counts in full**: Unity's `maximumDeltaTime` is 9 s, so a
  load hitch or stutter mid-fall adds its whole length to the air time. A
  hitch during an otherwise survivable fall can push you over 3.8 s and
  kill you [code].
- A jump restarts it (and so does a zipline exit).

## The hard landing (when you survive)

A damaging landing that does not kill you triggers the heavy-landing
animation and `doHardfallRoutine`: input clamped to 0 and velocity zeroed
**every frame for 1 s**, mouse turn speed dropped to 0.55, arms down, no
jump until `resetAnimSpine` 1 s later (plus ~0.5 s to blend back). About
1-1.5 s lost. Avoiding it is the other reason to cancel fall damage.

## Why it goes wrong

- **A new fast contact at the bottom**: if the last thing you touch before
  grounding is a fresh collision at speed (the ground itself after leaving
  the slide), that enter stores the fast value and you take full damage.
  The slide must carry you onto the ground in contact.
- **Over 3.8 s in the air**: the 1000-damage "fell too long" sits inside
  the same `prevVelocity > 28` check, so a cancelled landing avoids even
  that - but only if the last collision enter really was slow. Any fast
  contact after it (the ground, a ledge) brings the full verdict back.

## Evidence

- Code: decompiled `FirstPersonCharacter.HandleLanded` and
  `OnCollisionEnterProxied` (above), `HandleStartJumping`.
- Live: an 82 m drop onto ground was judged at 55 m/s; a drop into the big
  lake was judged at 0 but swimming (no damage either way); terrain slopes
  never build speed; faking the cancel's state (stored speed zeroed every
  frame of an 82 m fall) gave no damage, as the code says.
- ForestOverlay's run mode reports a landing that passed every check but
  the stored speed while the player really fell faster than 30 m/s.

## Open questions

- The runners' own Cave 6 body slide has not been reproduced with real
  input yet.

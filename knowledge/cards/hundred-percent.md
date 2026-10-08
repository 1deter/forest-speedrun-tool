---
id: hundred-percent
title: 100% - the category's rules and the full item list
aliases: 100%, 100 percent, hundo, hundred percent, 100% rules, 100% items, 100% checklist, all items, every item, collectibles, story items, unique items, all story items, all unique items, nature guide, survival book complete, passenger manifest, passengers, all passengers, to do list, todo list, flintlock parts, magazines, cassettes, camcorder tapes, toy parts, megan drawings, timmy drawings, polaroids, photos, sketches
tags: categories, rules, collectibles
confidence: code
checked: 2026-10-04
sources: speedrun.com API v1, category "100%" (zdn5qzed) rules, read 2026-10-04; collectibles/100-percent.txt (ForestOverlay's checklist, mapped onto the game's item ids); docs/game-notes.md "Survival book (100%)", "Nature guide", "Passengers"
related: categories-and-rules, crafting-recipes, building-costs, forestoverlay, top-runners
code: TheForest.Player.TickOffSystem, TheForest.Player.PassengerManifest, TheForest.Player.SerializableSurvivalBookTodo
---

# 100%

The 100% category exists on speedrun.com with written rules but **no runs**
yet (2026-10-04). Its rules, quoted closely from speedrun.com (the
moderators' text):
- **Normal or Hardmode.**
- **Crashing the plane** - the ending where you take the plane-crash
  button in the lab's control room (`end-crash`, `endgame-splits`), not the
  shutdown one [inferred from the wording].
- **All story items, all unique items, the flashlight (non-unique) and the
  cooking pot (non-unique).**
- **The survival book / nature guide completed.**
- **The passenger manifest completed.**
- **The to-do list completed** - "pending bugginess, but required at this
  point".

Nothing else is written for it; the shared rules of every category (no
developer mode, no third-party software, video required, real time) apply
(`categories-and-rules`). Anything the text does not settle is the
moderators' call.

## The item list

ForestOverlay's 100% tab tracks this list, which maps the moderators'
names onto the game's item ids (`collectibles/100-percent.txt`) [code]:

- **Unique tools and weapons (18)**: plane axe, rusty axe, modern axe,
  climbing axe, machete, katana, tennis racket, chainsaw, modern bow, flare
  gun, crossbow, flintlock pistol, compass, map, pedometer, rebreather,
  flashlight, cooking pot.
- **Flintlock parts (8)**: parts 1-8 (crafted into the flintlock,
  `crafting-recipes`).
- **Story documents (9)**: autopsy report, bible pages 1 and 2, cargo
  manifest, "bring down a plane" email, Megan email, termination notice,
  fortune, restraining order.
- **Megan's drawings (5)**: Daddy, Dinosaur, Flower, Unicorn, and Megan's
  crayons.
- **Magazines (5)**: Beneath the Limestone, Ethical Scientist, The
  Practical Caver, Real!, Yacht.
- **Photos and sketches (16)**: cache photos 1-8, Megan's school photo,
  obelisk photo, Virginia photo, yacht photo, obelisk sketch, obelisk
  drawing, Virginia sketch, sinkhole sketch.
- **Keycard polaroids (3).**
- **Toy pieces**: torso, head, 2 arms, 2 legs, and the assembled toy.
- **Timmy's drawings**: one item holding the drawings as pieces (like the
  map pieces).
- **Camcorder and its tapes (7)**: the camcorder, tapes 1-6 (Megan's
  arrival, lunch with Megan, opening the artifact, side effects, Armsy
  breakout, second artifact test).
- **Cassettes (6)**: the cassette player (walkman) and cassettes 1-5.
- **Endgame (4)**: the keycard, the elevator keycard, the artifact ball,
  the artifact key.

## The book parts

- **Nature guide**: the survival book's tick-off pages - plant life 1-2
  and animals 1-3, **43 ticks** (44 entries in `TickOffSystem`, one with
  no tick mark). An entry ticks when you pick up the item, or inspect the
  animal or plant [code + live reads, game-notes *Nature guide*].
- **How an animal or plant registers** (`AnimalTypeTrigger` on the
  creature; each entry ticks once, then stops listening) [code, not
  reproduced]. Nothing is pressed. Each trigger is one of three kinds:
  - *on enable*: ticks as soon as the creature appears (is enabled).
  - *by view*: the creature's origin must be in the **middle 40% of the
    screen** (viewport 0.3-0.7 on both axes), in front of the camera,
    and **within ~18.7 m** (squared distance under 350). The check
    starts 1 s after the creature appears. Once it passes, the game
    waits **2.5 s** and checks the centre box **once more** (distance
    is not checked again): still centred -> it ticks; not -> it re-arms
    (1 s) and starts over. The creature does **not** have to stay in
    view during the 2.5 s - only be centred at the start and at the
    end. Fastest: centre it from close, keep the crosshair near it,
    about 2.5 s.
  - *by grab focus* (neither flag): the creature's pickup trigger must
    stay in the player's grab focus (the `Grabber`) for **4 s of real
    time**; out of focus for more than 1 s cancels it until it is
    focused again.
  - Fish register when their body is set up (`setupFishRagdoll`), not
    through this trigger.
  Which species use which kind is not recorded yet.
- **Passenger manifest**: **43 seats**. A passenger counts only while you
  carry the manifest (item 197) and are not upside down
  (`PassengerManifest.FoundPassenger`) [code]. The game's own passenger
  database has wrong locations for several (Cave 6's hanging bodies are
  listed in Caves 1 and 9) [live].
- **To-do list**: one task per objective (son, camp, caves 1-10,
  sinkhole, passengers, ...) in `SerializableSurvivalBookTodo`. What the
  rule's "pending bugginess" refers to is not recorded.

## Open questions

- Whether the moderators' "all unique items" matches this list exactly
  (the list says it is the one the admins agreed; not re-checked against
  them).
- Where each item lies - not in the knowledge base yet.

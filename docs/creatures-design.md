# Creature sync design (v0.9)

Goal: every player sees the same creatures in the same places doing the same things,
and creatures can attack any player for real.

## 1. Same creatures for everyone

The game fills "entity slots" when a cell loads: `EntitySlotsPlaceholder.Spawn` / `EntitySlot.Spawn`
ask `CellManager.GetPrefabForSlot(slot)` for a `Filler { classId, count }` (a dice roll), then create
`VirtualPrefabIdentifier` placeholders which later instantiate the real prefab through
`DeferredSpawner.InstantiateAsync(...)`. The real object gets a fresh random id.

- **Spawn book** (server): `slot key -> (classId, count)`. Slot key = slot world position rounded to 0.1 m.
  - Client postfix on `GetPrefabForSlot`: if the book has the key, replace the result; otherwise keep the
    local roll and send it to the server (first writer wins; server broadcasts). Book is sent in Welcome.
- **Stable ids**: while a slot spawns, remember `slot key`; every placeholder registered for it gets a
  deterministic id `slot key + #index`. When the placeholder's real prefab is created
  (`DeferredSpawner.Task` result for that owner), the real object's `UniqueIdentifier.Id` is set to the
  same deterministic id. Same slot, same roll, same id on every PC.
- **Deaths / eaten / picked up**: removal of a creature id is broadcast like picked-up items (existing
  `EntityRemoved` path), so it's gone everywhere, and stays gone for late joiners.

## 2. One brain per creature (simulation ownership)

- Clients claim unowned creatures within 80 m (`CreatureClaim`); server grants first come.
- Owner releases creatures farther than 120 m; owner disconnect releases everything.
- Owner streams owned creatures' position/rotation (+ aggression) at ~8 Hz in one batched packet,
  only while another player is within 150 m.
- Non-owners: creature AI behaviours disabled, rigidbody kinematic, smoothly follow the stream.
  Animator keeps playing from movement speed.

## 3. Attacks and damage

- Remote divers get an `EcoTarget` (same type as the local player's) on the owner's PC so creatures can
  notice them.
- When an owned creature targets a remote diver, ownership is handed to that player (`CreatureHandoff`);
  the victim's own game then runs the real attack code against the real player.
- Damage dealt to a creature you don't own is forwarded to its owner (`CreatureDamage`).
- Creature death is broadcast; other copies are killed so death animations/corpses match.

## Out of scope for v0.9
Eggs hatching, alien containment breeding, followers (cuddlefish), crabsquid EMP.

## Testing
- Server unit tests: spawn book first-writer-wins + persistence, claim/release/handoff rules,
  disconnect release, damage forwarding, death broadcast.
- Every game type/member the plugin uses is checked against the decompiled game assembly.

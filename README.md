# Yugioh: Forbidden Spire

**Version 0.5 (beta)**: a Slay the Spire 2 mod.

Play the Necrobinder as **Duel Master K** and climb the Spire with Duel Monsters from *Yu-Gi-Oh! Forbidden Memories*. You summon monsters, fuse cards and battle with Attack and Defense positions instead of playing Strikes and Defends. Bosses are full duels against Priest Seto.

## You need your own copy of Forbidden Memories

The mod ships **no game content** from Forbidden Memories. Card data, card art, fusions, music and the intro movie are all read at runtime from **your own Forbidden Memories disc image** (`.bin`). The mod finds the disc path in `settings.cfg` next to the mod (key `FM_DISC=`) or searches common emulator folders.

## Features in 0.5 (beta)

- **Duel Master K:** the Necrobinder becomes a Forbidden Memories duelist with 8000 Life Points, a hand of 5, one summon per turn and three monster zones.
- **All 722 Forbidden Memories cards:**
  - monsters, spells, traps, equips, rituals and fields
  - the game's own fusion table, including fusing onto a monster on the field
  - heal-card fusions and junk-card rules
- **Forbidden Memories battle rules:**
  - Attack and Defense positions
  - Guardian Stars (Sun and Moon)
  - field cards that change the stage background
- **Enemy AI:**
  - picks targets, goes into Defense Position, and defends less often each turn in a row
  - still uses the rest of its moves while defending
- **Boss duels (Priest Seto, Acts 1–3):**
  - 8000 LP and their own three monster zones
  - decks taken from the game
  - a face-down hand that slides in and plays like a real opponent, with a hidden 20-card pool
  - face-down monsters and traps
  - the Forbidden Memories defense switch and the first-turn no-attack rule
- **Re-themed run:**
  - Star Chips instead of gold
  - relics and potions shown as Forbidden Memories cards
  - StS stats reworked into Forbidden Memories equivalents (ATK/DEF loss, Burn, Counter Trap, Shadow Spell)
- **Starting options:**
  - Neow picks: Dragon Treasure, Exodia Card Box, Magic Protection Kit
  - Equip Booster Pack at the start of Act 2
- **Library and tutorial:**
  - Library with a searchable card list and a Fusion Calculator
  - in-game tutorial with Rules, Fusing, Cheat Sheet, Junk Cards and Bosses tabs
- **Music and sound:**
  - Forbidden Memories music with seamless loops
  - synthesized attack, summon, heal and spell sounds
  - attack, summon and spell-cast animations

## Building

- Requires the .NET 9 SDK and a Slay the Spire 2 install.
- Run `dotnet build -c Release` in `src/FMDuelist`.
- `tools/deploy.sh` copies the build into the game's `mods/FMDuelist` folder.

## Notes

- Mod id: `FMDuelist`, manifest version `0.5.0`.
- This is a fan project. *Yu-Gi-Oh! Forbidden Memories* belongs to Konami, and *Slay the Spire 2* belongs to Mega Crit.

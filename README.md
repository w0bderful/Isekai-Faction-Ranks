# Isekai Faction Ranks 1.1.1

RimWorld 1.7 update: policy lookups safely defer hostility checks until a player faction exists,
without using the error-logging `Faction.OfPlayer` getter. Load, settings changes, faction changes
and completed pawn spawning trigger immediate initialization checks. Fallback checks start every
1,500 ticks and visit at most 32 pawns per tick across maps; initialized pawns are not rerolled.
This replaces the 250-tick full scan. No faction policy cache is introduced, so diplomacy and
settings changes take effect on subsequent lookups. The retained 1.6 binary is unchanged.

A standalone patch for ISEKAI RPG LEVELING (Workshop 3657580708) on RimWorld 1.6 / 1.7.
Built against the currently installed original DLL (1.1.8.3). The original DLL is neither modified nor included.

## Installation and settings

1. Put the IsekaiFactionRanks folder in RimWorld/Mods.
2. Enable Harmony → ISEKAI RPG LEVELING → Isekai Faction Ranks in that order, then restart the game.
3. Adjust the options under Options → Mod settings → Isekai Faction Ranks.

Defaults:
- Allow ranks for colony animals: on.
- Disable ranks for hostile factions: on.
- Player faction only: off. When on, neutral, allied, factionless and wild pawns are excluded too.
- Tamed creature bonus retention: 100%. Adjustable from 0 to 100%.

The colony animal exception allows animals of the player faction even when the original mod's
exclude-animals option is on. Wild animals still follow the original setting. Turning the exception
off also restores the original retention value. The original settings file is never overwritten.
Animal progression and stat formulas are the original ones.

## Scope

Eligibility is decided by the pawn's current faction and its diplomatic relation to the player.
The player faction is never blocked. Prisoners from hostile factions stay blocked until recruited.
With only the hostile option on, the manhunter mental state of factionless animals does not count
as a hostile faction. Turn on player faction only to exclude them as well.

Excluded pawns lose the original mod's rank stats, XP growth, passive lookups, aura use and display,
rank-based extra gear generation, and creature rank rewards and display.
Base combat XP the player earns by attacking enemies, and abilities the player uses, are kept.
Animals, mechanoids and entities excluded by the original mod are also excluded from rank bonus lookups.

Existing progression components and save data are never deleted.
Already saved Isekai traits are suppressed and may remain on the info card as suppressed traits.
When a pawn becomes eligible again, its existing level and XP are used. Colony animals that had no
progression data yet are checked on map load, settings/faction changes and spawning, with a
fallback sweep every 1,500 ticks processed in batches of at most 32 pawns per tick.

Gear already upgraded, VPE psycasts already learned and past one-time effects are not removed retroactively.
Abilities of boss quests, boss-specific races and separate add-ons are not affected by this patch.
An excluded enemy without a rank can still be strong because of its race or equipment.

## Verification

- Built directly against RimWorld 1.7 (.NET 10) and the original DLL. The 1.6 DLL is kept unchanged in `1.6/Assemblies`.
- IL rewrite and signature checks for 73 original component lookups (66 methods).
- Component creation, repair and save paths are left untouched to avoid duplicate components.
- Signature checks for 33 entry points (progression, generation, auras and more).
- Harmony patch emission checked on the real DLLs, plus behavior tests with mock Pawn/Faction objects.
- Checked: original animal exclusion with the colony animal exception, hostile/neutral/player-only modes,
  XP preservation, recruitment, trait suppression and restoration, and overriding the original 0% retention.
- Initialization checks cover 10,000 repeated lookups before the player faction exists, restoration of hostile filtering after its creation, and bounded fallback sweeps.
- A full mod-list startup and world-generation run reached a map with no player-faction lookup errors after this fix. Unrelated mod errors remained in that run.
- Static initialization that needs the game engine, and in-game UI, combat and save/load, have not been tested in game yet.

If an update to the original mod changes its API and patching fails, an [Isekai Faction Ranks] error is
logged and all Harmony changes made by this patch are reverted.

## Building from source

Run ./build.ps1 in Windows PowerShell (.NET 10 SDK required). Pass -RimWorld and -Workshop paths if needed.
The output is `1.7/Assemblies/IsekaiFactionRanks.dll`; `LoadFolders.xml` picks the DLL for each game version.
The original Isekai DLL, the game DLLs and Harmony are referenced from their install locations.
Tests/test.ps1 runs the signature, IL, partial Harmony and behavior checks that are possible outside the game.
The test executables are not needed in the mod release.

## Links

- GitHub: https://github.com/w0bderful/Isekai-Faction-Ranks
- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3812345444

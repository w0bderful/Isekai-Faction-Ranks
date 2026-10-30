using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using IsekaiFactionRanks;
using RimWorld;
using Verse;
using Policy = IsekaiFactionRanks.Policy;

static class ReloadPolicyValidation
{
    static int checks, errors, hostilityQueries;
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static void Expect(bool value, string label)
    {
        if (!value) throw new Exception(label);
        checks++;
    }
    public static bool CaptureError(string text) { errors++; return false; }
    public static void ObserveHostility() { hostilityQueries++; }
    static Faction MakeFaction(int id, bool player = false)
    {
        var faction = Blank<Faction>();
        faction.def = new FactionDef { isPlayer = player };
        faction.loadID = id;
        AccessTools.Field(typeof(Faction), "relations").SetValue(faction, new List<FactionRelation>());
        return faction;
    }
    static Pawn MakePawn(Faction faction)
    {
        var pawn = Blank<Pawn>();
        AccessTools.Field(typeof(Thing), "factionInt").SetValue(pawn, faction);
        return pawn;
    }
    static void Relate(Faction faction, Faction player, FactionRelationKind kind)
    {
        AccessTools.Field(typeof(Faction), "relations").SetValue(faction,
            new List<FactionRelation> { new FactionRelation { other = player, kind = kind } });
    }
    public static void Run(Harmony harmony)
    {
        var previousGame = Current.Game;
        var previousState = Current.ProgramState;
        var previousSettings = RankMod.Settings;
        var errorMethod = AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) });
        var hostileMethod = AccessTools.Method(typeof(FactionUtility), nameof(FactionUtility.HostileTo), new[] { typeof(Faction), typeof(Faction) });
        harmony.Patch(errorMethod, prefix: new HarmonyMethod(typeof(ReloadPolicyValidation), nameof(CaptureError)));
        harmony.Patch(hostileMethod, prefix: new HarmonyMethod(typeof(ReloadPolicyValidation), nameof(ObserveHostility)));
        try
        {
            checks = errors = hostilityQueries = 0;
            RankMod.Settings = new RankSettings();
            var player = MakeFaction(21, true);
            var oldPlayer = MakeFaction(21, true);
            var world = Blank<RimWorld.Planet.World>();
            world.factionManager = new FactionManager();
            world.factionManager.AllFactionsListForReading.Add(player);
            AccessTools.Field(typeof(FactionManager), "ofPlayer").SetValue(world.factionManager, player);
            Current.Game = Blank<Game>();
            AccessTools.Field(typeof(Game), "worldInt").SetValue(Current.Game, world);
            Current.ProgramState = ProgramState.Playing;
            Expect(ReferenceEquals(Policy.PlayerFaction, player), "Playing policy uses the current world's player faction");

            var incomplete = MakeFaction(3);
            var incompletePawn = MakePawn(incomplete);
            Expect(!Policy.Blocked(incompletePawn) && errors == 0 && hostilityQueries == 0,
                "Missing relation defers without logging or invoking HostileTo");

            var stale = MakeFaction(3);
            Relate(stale, oldPlayer, FactionRelationKind.Hostile);
            var stalePawn = MakePawn(stale);
            var trait = Blank<Trait>();
            trait.def = new TraitDef { defName = "Isekai_Rank_S" };
            trait.pawn = stalePawn;
            for (int i = 0; i < 1000; i++)
            {
                bool suppressed = false;
                Hooks.TraitSuppressed(trait, ref suppressed);
                Expect(!Policy.Blocked(stalePawn) && !suppressed, "Stale trait/pawn relation is deferred");
            }
            Expect(errors == 0 && hostilityQueries == 0, "Repeated stale cache queries produce no errors");
            Expect(stale.RelationWith(oldPlayer, true)?.kind == FactionRelationKind.Hostile
                && stale.RelationWith(player, true) == null, "Old faction data is neither remapped nor repaired");

            var currentEnemy = MakeFaction(3);
            Relate(currentEnemy, player, FactionRelationKind.Hostile);
            Expect(Policy.Blocked(MakePawn(currentEnemy)) && hostilityQueries == 1,
                "Valid hostile filtering still invokes the original HostileTo compatibility path");
            var neutral = MakeFaction(4);
            Relate(neutral, player, FactionRelationKind.Neutral);
            Expect(!Policy.Blocked(MakePawn(neutral)) && hostilityQueries == 2, "Neutral faction remains eligible");
            Relate(neutral, player, FactionRelationKind.Ally);
            Expect(!Policy.Blocked(MakePawn(neutral)) && hostilityQueries == 3, "Allied faction remains eligible");
            Relate(neutral, player, FactionRelationKind.Hostile);
            Expect(Policy.Blocked(MakePawn(neutral)) && hostilityQueries == 4, "Diplomacy changes apply on the next query");

            int queries = hostilityQueries;
            RankMod.Settings.playerOnly = true;
            Expect(Policy.Blocked(incompletePawn) && Policy.Blocked(stalePawn)
                && hostilityQueries == queries, "Player-only mode excludes nonplayers even when relations are missing");
            Expect(!Policy.Blocked(MakePawn(player)), "Player pawns remain eligible");
            RankMod.Settings.playerOnly = false;
            RankMod.Settings.disableHostiles = false;
            Expect(!Policy.Blocked(MakePawn(currentEnemy)) && hostilityQueries == queries,
                "Disabled hostile filter skips relation and hostility queries");
            RankMod.Settings.disableHostiles = true;
            Relate(incomplete, player, FactionRelationKind.Hostile);
            Expect(Policy.Blocked(incompletePawn), "Deferred faction resumes hostile filtering as soon as its relation exists");
            Expect(errors == 0, "All reload cases complete without game error logs");
            Console.WriteLine("PASS: " + checks + " reload relation, stale trait, diplomacy and policy checks");
        }
        finally
        {
            Current.Game = previousGame;
            Current.ProgramState = previousState;
            RankMod.Settings = previousSettings;
            harmony.Unpatch(errorMethod, AccessTools.Method(typeof(ReloadPolicyValidation), nameof(CaptureError)));
            harmony.Unpatch(hostileMethod, AccessTools.Method(typeof(ReloadPolicyValidation), nameof(ObserveHostility)));
        }
    }
}

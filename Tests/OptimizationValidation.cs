using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using IsekaiFactionRanks;
using RimWorld;
using Verse;
using Policy = IsekaiFactionRanks.Policy;

static class OptimizationValidation
{
    static int checks;
    static void Expect(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    public static bool ForbiddenPlayerGetter() => throw new Exception("Logging Faction.OfPlayer must never be used by policy");
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    public static void Run(Harmony harmony)
    {
        harmony.Patch(AccessTools.PropertyGetter(typeof(Faction), "OfPlayer"), prefix: new HarmonyMethod(typeof(OptimizationValidation), nameof(ForbiddenPlayerGetter)));
        var previous = Current.Game;
        var settings = RankMod.Settings;
        try
        {
            RankMod.Settings = new RankSettings();
            var faction = Blank<Faction>(); faction.def = new FactionDef();
            var pawn = Blank<Pawn>();
            AccessTools.Field(typeof(Thing), "factionInt").SetValue(pawn, faction);
            Current.Game = null;
            Expect(Policy.PlayerFaction == null && !Policy.Blocked(pawn), "No game: defer hostile decision without logging");
            Current.Game = Blank<Game>();
            Expect(Policy.PlayerFaction == null && !Policy.Blocked(pawn), "No world: defer without logging");
            var world = Blank<RimWorld.Planet.World>();
            AccessTools.Field(typeof(Game), "worldInt").SetValue(Current.Game, world);
            Expect(Policy.PlayerFaction == null && !Policy.Blocked(pawn), "No faction manager: defer without logging");
            world.factionManager = new FactionManager();
            for (int i=0;i<10000;i++) Expect(!Policy.Blocked(pawn), "World generation repeated lookup without player faction");
            RankMod.Settings.playerOnly = true;
            Expect(Policy.Blocked(pawn), "Player-only policy remains enforced before player creation");
            RankMod.Settings.playerOnly = false;
            var player = Blank<Faction>(); player.def = new FactionDef { isPlayer = true };
            AccessTools.Field(typeof(Game), "initData").SetValue(Current.Game, new GameInitData { playerFaction = player });
            AccessTools.Field(typeof(Faction), "relations").SetValue(faction, new List<FactionRelation> { new FactionRelation { other = player, kind = FactionRelationKind.Hostile } });
            Expect(Policy.PlayerFaction == player && Policy.Blocked(pawn), "Hostile policy resumes as soon as init data has player faction");
        }
        finally
        {
            Current.Game = previous; RankMod.Settings = settings;
            harmony.Unpatch(AccessTools.PropertyGetter(typeof(Faction), "OfPlayer"), AccessTools.Method(typeof(OptimizationValidation), nameof(ForbiddenPlayerGetter)));
        }
        var sweep = new BoundedSweep<int>();
        var a = Enumerable.Range(0,10000).ToList();
        var seen = new List<int>();
        sweep.Start(new IReadOnlyList<int>[] { new List<int>(), a, new List<int>() });
        int steps = 0;
        while (sweep.Pending)
        {
            Expect(sweep.Step(RankRefresh.PawnsPerTick, seen.Add) <= 32, "Per-tick visit budget");
            if (++steps > 400) throw new Exception("Sweep did not finish");
        }
        Expect(seen.SequenceEqual(a), "Stable groups visited exactly once");
        Expect(RankRefresh.SweepInterval == 1500, "Fallback interval is 1500 ticks");
        a = new List<int> {1,2,3}; sweep.Start(new[]{(IReadOnlyList<int>)a});
        sweep.Step(1, _ => {}); a.Clear();
        sweep.Step(32, _ => throw new Exception("Removed pawn visited"));
        Expect(!sweep.Pending, "Despawned groups tolerate list shrinkage");
        sweep.Start(new[] {(IReadOnlyList<int>)new[]{4}}); seen.Clear();
        sweep.Step(32,seen.Add); Expect(seen.SequenceEqual(new[]{4}), "New sweep has no retained entries");
        var target = AccessTools.Method(typeof(Pawn), nameof(Pawn.SpawnSetup), new[]{typeof(Map),typeof(bool)});
        harmony.Patch(target,postfix:new HarmonyMethod(typeof(Hooks),nameof(Hooks.Spawned)));
        Expect(Harmony.GetPatchInfo(target).Owners.Contains(harmony.Id), "Spawn event hook emits against actual game method");
        Console.WriteLine("PASS: " + checks + " initialization, bounded sweep and spawn hook checks");
    }
}

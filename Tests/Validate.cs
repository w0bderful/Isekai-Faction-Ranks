using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using IsekaiFactionRanks;
using Verse;
using RimWorld;
using Policy = IsekaiFactionRanks.Policy;
using IsekaiLeveling;
using IsekaiLeveling.MobRanking;
using System.Collections.Generic;
using System.Runtime.Serialization;

class Validate
{
    static string root;
    static int Main(string[] args)
    {
        root = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string file = new AssemblyName(e.Name).Name + ".dll";
            foreach (string path in new[] { root + @"\Assemblies", @"D:\SteamLibrary\steamapps\workshop\content\294100\3657580708\Assemblies", @"D:\SteamLibrary\steamapps\workshop\content\294100\2009463077\Current\Assemblies", @"D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed" })
                if (File.Exists(Path.Combine(path, file))) return Assembly.LoadFrom(Path.Combine(path, file));
            return null;
        };
        try { Run(); return 0; } catch (Exception e) { Console.WriteLine(e); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run()
    {
        for (int bits = 0; bits < 16; bits++)
        {
            bool player = (bits & 1) != 0, hostile = (bits & 2) != 0;
            bool only = (bits & 4) != 0, disable = (bits & 8) != 0;
            bool expected = player ? false : only ? true : hostile && disable;
            if (Policy.Disabled(player, hostile, only, disable) != expected) throw new Exception("Policy truth table");
        }
        Console.WriteLine("PASS: 16 faction/settings combinations");
        var prefixes = PatchPlan.PrefixTargets();
        foreach (var pair in prefixes)
        {
            if (pair.Value == "PawnGate" && pair.Key.GetParameters()[0].ParameterType != typeof(Verse.Pawn))
                throw new Exception("Pawn prefix signature mismatch: " + pair.Key);
            if (pair.Value == "HumanGate" && pair.Key.DeclaringType != typeof(IsekaiLeveling.IsekaiComponent)) throw new Exception("Human signature");
            if (pair.Value == "MobGate" && pair.Key.DeclaringType != typeof(IsekaiLeveling.MobRanking.MobRankComponent)) throw new Exception("Mob signature");
        }
        Console.WriteLine("PASS: " + prefixes.Count + " lifecycle/gameplay gate signatures");
        var targets = PatchPlan.LookupTargets().ToList();
        int calls = 0;
        foreach (var method in targets)
        {
            var before = PatchProcessor.GetOriginalInstructions(method);
            int expected = before.Count(i => PatchPlan.FilterFor(i) != null);
            var after = PatchPlan.FilterLookups(before).ToList();
            if (after.Count != before.Count + expected) throw new Exception("Wrong rewrite count");
            for (int i = 0; i < after.Count; i++)
            {
                MethodInfo filter = PatchPlan.FilterFor(after[i]);
                if (filter == null) continue;
                var original = (MethodInfo)after[i].operand;
                if (!Equals(after[i + 1].operand, filter) || filter.ReturnType != original.ReturnType || filter.GetParameters()[0].ParameterType != original.ReturnType)
                    throw new Exception("Stack signature mismatch " + method);
            }
            if (method.DeclaringType.Name == "Patch_InjectComponent" || method.DeclaringType.Name.Contains("CharacterEditor")) throw new Exception("Component creation must not be filtered");
            calls += expected;
        }
        Console.WriteLine("PASS: " + calls + " lookup calls rewritten in " + targets.Count + " methods; component creation excluded");
        // Emit patches against real shipped IL. No game or Unity rendering is run.
        var harmony = new Harmony("local.isekai.factionranks.validation");
        try
        {
            int count = 0, skipped = 0;
            foreach (var method in targets)
            {
                if (method.DeclaringType.TypeInitializer != null) { skipped++; continue; }
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(PatchPlan), "FilterLookups"));
                count++;
            }
            foreach (var pair in prefixes)
            {
                if (pair.Key.DeclaringType.TypeInitializer != null) { skipped++; continue; }
                harmony.Patch(pair.Key, prefix: new HarmonyMethod(typeof(Hooks), pair.Value));
                count++;
            }
            harmony.Patch(AccessTools.PropertyGetter(typeof(RimWorld.Trait), "Suppressed"), postfix: new HarmonyMethod(typeof(Hooks), "TraitSuppressed"));
            harmony.Patch(AccessTools.Method(typeof(Verse.Pawn), "SetFaction", new[] { typeof(RimWorld.Faction), typeof(Verse.Pawn) }), postfix: new HarmonyMethod(typeof(Hooks), "FactionChanged"));
            Console.WriteLine("PASS: Harmony emitted " + (count + 2) + " patches on actual DLLs; " + skipped + " static-initializer targets checked by IL/signature only (Unity host required)");
            RunPawnCases(harmony);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    static Faction playerFaction;
    public static bool QuietLog() { return false; }
    public static bool FakePlayer(ref Faction __result) { __result = playerFaction; return false; }
    static T Blank<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
    static Pawn PawnFor(Faction faction, bool animal)
    {
        var pawn = Blank<Pawn>();
        pawn.def = Blank<ThingDef>(); pawn.def.race = Blank<RaceProperties>(); pawn.def.race.intelligence = animal ? Intelligence.Animal : Intelligence.Humanlike; var flesh = Blank<FleshTypeDef>(); flesh.isOrganic = true; AccessTools.Field(typeof(RaceProperties), "fleshType").SetValue(pawn.def.race, flesh);
        AccessTools.Field(typeof(Thing), "factionInt").SetValue(pawn, faction);
        return pawn;
    }
    static void Expect(bool value, string label) { if (!value) throw new Exception(label); }
    static void RunPawnCases(Harmony harmony)
    {
        // Replace only environment services that require a running Unity game.
        // Pawn, faction relation, component and original exclusion code remain real.
        harmony.Patch(AccessTools.Method(typeof(Log), "Message", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Validate), "QuietLog"));
        harmony.Patch(AccessTools.Method(typeof(Log), "Warning", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Validate), "QuietLog"));
        FleshTypeDefOf.Mechanoid = Blank<FleshTypeDef>();
        harmony.Patch(AccessTools.PropertyGetter(typeof(RaceProperties), "IsAnomalyEntity"), prefix: new HarmonyMethod(typeof(Validate), "QuietLog"));
        harmony.Patch(AccessTools.PropertyGetter(typeof(Faction), "OfPlayer"), prefix: new HarmonyMethod(typeof(Validate), "FakePlayer"));
        harmony.Patch(AccessTools.Method(typeof(MobRankInjector), "IsExcludedFromRanking"), postfix: new HarmonyMethod(typeof(Hooks), "Excluded"));
        harmony.Patch(AccessTools.Method(typeof(IsekaiComponent), "GetCached"), prefix: new HarmonyMethod(typeof(Hooks), "PawnGate"));
        harmony.Patch(AccessTools.PropertyGetter(typeof(IsekaiLevelingSettings), "TamedAnimalBonusRetention"), postfix: new HarmonyMethod(typeof(Hooks), "Retention"));
        AccessTools.PropertySetter(typeof(IsekaiMod), "Settings").Invoke(null, new object[] { new IsekaiSettings { ExcludeAnimalsFromRanking = true, TamedAnimalBonusRetention = 0f } });
        RankMod.Settings = new RankSettings();
        playerFaction = Blank<Faction>(); playerFaction.def = Blank<FactionDef>(); playerFaction.def.isPlayer = true;
        var enemy = Blank<Faction>(); enemy.def = Blank<FactionDef>();
        var neutral = Blank<Faction>(); neutral.def = Blank<FactionDef>();
        var relations = AccessTools.Field(typeof(Faction), "relations");
        relations.SetValue(enemy, new List<FactionRelation> { new FactionRelation { other = playerFaction, kind = FactionRelationKind.Hostile } });
        relations.SetValue(neutral, new List<FactionRelation> { new FactionRelation { other = playerFaction, kind = FactionRelationKind.Neutral } });
        Pawn colonist = PawnFor(playerFaction, false), pet = PawnFor(playerFaction, true), raider = PawnFor(enemy, false), enemyAnimal = PawnFor(enemy, true), visitor = PawnFor(neutral, false), wild = PawnFor(null, true);
        Expect(!Policy.Blocked(colonist) && !Policy.Blocked(pet), "Player pawns must remain enabled");
        Expect(Policy.Blocked(raider) && Policy.Blocked(enemyAnimal), "Hostile faction blocking");
        Expect(!Policy.Blocked(visitor) && !Policy.Blocked(wild), "Neutral/wild faction policy");
        Expect(!MobRankInjector.IsExcludedFromRanking(pet), "Original exclude-animals must allow colony pet");
        Expect(MobRankInjector.IsExcludedFromRanking(wild) && MobRankInjector.IsExcludedFromRanking(enemyAnimal), "Wild/enemy animal exclusion");
        var humanComp = new IsekaiComponent { parent = raider, currentLevel = 99, currentXP = 700 };
        AccessTools.Field(typeof(ThingWithComps), "comps").SetValue(raider, new List<ThingComp> { humanComp });
        Expect(IsekaiComponent.GetCached(raider) == null, "Hostile cached lookup blocked");
        humanComp.GainXP(100);
        Expect(humanComp.currentXP == 700 && humanComp.currentLevel == 99, "Blocked XP preserves progression");
        var rankTrait = Blank<Trait>(); rankTrait.def = Blank<TraitDef>(); rankTrait.def.defName = "Isekai_Rank_S";
        rankTrait.pawn = raider;
        bool suppressed = false; Hooks.TraitSuppressed(rankTrait, ref suppressed); Expect(suppressed, "Existing hostile rank trait suppressed");
        AccessTools.Field(typeof(Thing), "factionInt").SetValue(raider, playerFaction);
        suppressed = false; Hooks.TraitSuppressed(rankTrait, ref suppressed); Expect(!Policy.Blocked(raider) && Hooks.FilterHuman(humanComp) == humanComp && !suppressed, "Recruitment restores original data and trait");
        RankMod.Settings.playerOnly = true;
        Expect(Policy.Blocked(visitor) && Policy.Blocked(wild) && !Policy.Blocked(pet), "Player-only mode");
        RankMod.Settings.playerOnly = false;
        RankMod.Settings.disableHostiles = false;
        Expect(!Policy.Blocked(enemyAnimal), "Hostile toggle off");
        Expect(IsekaiLevelingSettings.TamedAnimalBonusRetention == 1f, "Original zero retention override");
        RankMod.Settings.animalRetention = 0.35f;
        Expect(Math.Abs(IsekaiLevelingSettings.TamedAnimalBonusRetention - 0.35f) < 0.0001f, "Configurable retention");
        RankMod.Settings.colonyAnimals = false;
        Expect(MobRankInjector.IsExcludedFromRanking(pet) && IsekaiLevelingSettings.TamedAnimalBonusRetention == 0f, "Exception off restores original settings");
        Console.WriteLine("PASS: real pawn/faction cases, exclusion override, disabled XP, reversible traits, recruitment and retention settings");
    }
}

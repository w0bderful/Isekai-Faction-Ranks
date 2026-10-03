using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using IsekaiLeveling;
using IsekaiLeveling.MobRanking;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiFactionRanks
{
    public class RankSettings : ModSettings
    {
        public bool colonyAnimals = true;
        public bool disableHostiles = true;
        public bool playerOnly = false;
        public float animalRetention = 1f;
        public override void ExposeData()
        {
            Scribe_Values.Look(ref colonyAnimals, "colonyAnimals", true);
            Scribe_Values.Look(ref disableHostiles, "disableHostiles", true);
            Scribe_Values.Look(ref playerOnly, "playerOnly", false);
            Scribe_Values.Look(ref animalRetention, "animalRetention", 1f);
            animalRetention = float.IsNaN(animalRetention) ? 1f : Mathf.Clamp01(animalRetention);
        }
    }

    public class RankMod : Mod
    {
        public static RankSettings Settings = new RankSettings();
        public static bool Ready;
        public RankMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RankSettings>();
            // Original startup constructors populate defs; apply after those complete.
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                try
                {
                    int count = PatchPlan.Install(new Harmony("local.isekai.factionranks"));
                    Ready = true;
                    Log.Message("[Isekai Faction Ranks] Ready: " + count + " patches.");
                }
                catch (Exception ex)
                {
                    Log.Error("[Isekai Faction Ranks] Installation failed; see exception. " + ex);
                }
            });
        }
        public override string SettingsCategory() { return "Isekai Faction Ranks"; }
        public override void DoSettingsWindowContents(Rect rect)
        {
            var list = new Listing_Standard();
            list.Begin(rect);
            list.Label("IFR_Heading".Translate());
            list.Gap();
            list.CheckboxLabeled("IFR_Animals".Translate(), ref Settings.colonyAnimals, "IFR_AnimalsTip".Translate());
            list.CheckboxLabeled("IFR_Hostiles".Translate(), ref Settings.disableHostiles, "IFR_HostilesTip".Translate());
            list.CheckboxLabeled("IFR_PlayerOnly".Translate(), ref Settings.playerOnly, "IFR_PlayerOnlyTip".Translate());
            list.Gap();
            list.Label("IFR_Notes".Translate());
            list.Gap();
            list.Label("IFR_Retention".Translate(Settings.animalRetention.ToStringPercent()));
            Settings.animalRetention = list.Slider(Settings.animalRetention, 0f, 1f);
            list.Label("IFR_RetentionTip".Translate());
            list.End();
        }
        public override void WriteSettings()
        {
            base.WriteSettings();
            RankRefresh.RefreshMaps();
        }
    }

    public static class Policy
    {
        // Pure decision table is independently tested; factionless wild animals are
        // not hostile factions. Player-only mode is the explicit broader exclusion.
        public static bool Disabled(bool player, bool hostile, bool playerOnly, bool disableHostiles)
        {
            return !player && (playerOnly || (disableHostiles && hostile));
        }
        public static bool Blocked(Pawn pawn)
        {
            if (pawn == null) return true;
            Faction faction = pawn.Faction;
            bool player = faction != null && faction.IsPlayer;
            var settings = RankMod.Settings;
            if (player) return false;
            if (settings.playerOnly) return true;
            return settings.disableHostiles && faction != null && Faction.OfPlayer != null
                && faction.HostileTo(Faction.OfPlayer);
        }
        public static bool AnimalException(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Animal
                && pawn.Faction != null && pawn.Faction.IsPlayer && RankMod.Settings.colonyAnimals;
        }
        public static bool MobAllowed(Pawn pawn)
        {
            return !Blocked(pawn) && !MobRankInjector.IsExcludedFromRanking(pawn);
        }
    }

    public static class Hooks
    {
        public static bool PawnGate(Pawn __0) { return !Policy.Blocked(__0); }
        public static bool HumanGate(IsekaiComponent __instance) { return !Policy.Blocked(__instance.Pawn); }
        public static bool MobGate(MobRankComponent __instance) { return Policy.MobAllowed(__instance.Pawn); }
        public static void Excluded(Pawn pawn, ref bool __result)
        {
            if (Policy.Blocked(pawn)) __result = true;
            else if (Policy.AnimalException(pawn)) __result = false;
        }
        public static IsekaiComponent FilterHuman(IsekaiComponent comp)
        {
            return comp == null || Policy.Blocked(comp.Pawn) ? null : comp;
        }
        public static MobRankComponent FilterMob(MobRankComponent comp)
        {
            return comp == null || !Policy.MobAllowed(comp.Pawn) ? null : comp;
        }
        public static void TraitSuppressed(Trait __instance, ref bool __result)
        {
            if (!__result && __instance.pawn != null && __instance.def != null && __instance.def.defName != null
                && __instance.def.defName.StartsWith("Isekai_", StringComparison.Ordinal)
                && Policy.Blocked(__instance.pawn)) __result = true;
        }
        public static void FactionChanged(Pawn __instance) { RankRefresh.RefreshPawn(__instance); }
        public static void Retention(ref float __result)
        {
            if (RankMod.Settings.colonyAnimals) __result = RankMod.Settings.animalRetention;
        }
    }

    public static class PatchPlan
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        public static readonly Assembly Original = typeof(IsekaiComponent).Assembly;
        public static readonly MethodInfo HumanFilter = AccessTools.Method(typeof(Hooks), "FilterHuman");
        public static readonly MethodInfo MobFilter = AccessTools.Method(typeof(Hooks), "FilterMob");

        public static MethodInfo FilterFor(CodeInstruction instruction)
        {
            if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt) return null;
            var method = instruction.operand as MethodInfo;
            if (method == null || !method.IsGenericMethod ||
                (method.Name != "GetComp" && method.Name != "TryGetComp")) return null;
            if (method.ReturnType == typeof(IsekaiComponent)) return HumanFilter;
            if (method.ReturnType == typeof(MobRankComponent)) return MobFilter;
            return null;
        }
        public static IEnumerable<MethodInfo> LookupTargets()
        {
            foreach (Type type in Original.GetTypes())
            {
                // These paths create/repair missing components. They must still see
                // the real component to avoid injecting duplicates or losing saves.
                if (type.Name == "Patch_InjectComponent" || type.Name.Contains("CharacterEditor")) continue;
                foreach (MethodInfo method in type.GetMethods(Declared))
                {
                    if (method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                    if (PatchProcessor.GetOriginalInstructions(method).Any(i => FilterFor(i) != null)) yield return method;
                }
            }
        }
        public static IEnumerable<CodeInstruction> FilterLookups(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                MethodInfo filter = FilterFor(instruction);
                yield return instruction;
                if (filter != null) yield return new CodeInstruction(OpCodes.Call, filter);
            }
        }
        static MethodInfo Required(Type type, string name)
        {
            MethodInfo method = AccessTools.DeclaredMethod(type, name);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }
        static Type RequiredType(string name)
        {
            return Original.GetType(name, true);
        }
        public static List<KeyValuePair<MethodInfo, string>> PrefixTargets()
        {
            var targets = new List<KeyValuePair<MethodInfo, string>>();
            Action<Type, string, string> add = delegate(Type type, string methods, string hook)
            {
                foreach (string name in methods.Split(new[] { ',' })) targets.Add(new KeyValuePair<MethodInfo, string>(Required(type, name), hook));
            };
            add(typeof(IsekaiComponent), "GetCached", "PawnGate");
            add(typeof(IsekaiComponent), "PostSpawnSetup,CompTickRare,GainXP,FlushPendingXP,GetStatBonus,CompInspectStringExtra", "HumanGate");
            add(typeof(MobRankComponent), "RecalculateRank,GainXP,CompTickRare,ProcessDeferredLevelUps,SetRankOverride,SetEliteOverride,CompInspectStringExtra", "MobGate");
            add(typeof(PawnStatGenerator), "ShouldGenerateStats,InitializePawnStats,SyncToRankTrait,AssignRankTrait,UpdateRankTraitFromStats", "PawnGate");
            add(typeof(IsekaiTraitHelper), "HasTrait,RollRandomTraits,ApplyOneTimeEffects", "PawnGate");
            add(RequiredType("IsekaiLeveling.Patches.Patch_AssignRankTraitAfterGeneration"), "Postfix", "PawnGate");
            add(RequiredType("IsekaiLeveling.Patches.Patch_EnhancePawnEquipmentOnGeneration"), "Postfix,GetPawnRank", "PawnGate");
            add(RequiredType("IsekaiLeveling.IsekaiTitleApplier"), "Refresh", "PawnGate");
            add(RequiredType("IsekaiLeveling.SkillTree.ConstellationHediffs"), "Sync", "PawnGate");
            add(RequiredType("IsekaiLeveling.Compatibility.VPEPsycastRankBonus"), "TryApply,TryApplyForced", "PawnGate");
            add(RequiredType("IsekaiLeveling.Effects.AuraSystem"), "ShouldShowAura,DrawAura", "PawnGate");
            add(RequiredType("IsekaiLeveling.Abilities.AuraFeel"), "Activate,CanUseNow", "PawnGate");
            return targets;
        }
        public static int Install(Harmony harmony)
        {
            // Resolve everything first; a renamed API cannot leave a half-installed patch.
            var lookups = LookupTargets().ToList();
            var prefixes = PrefixTargets();
            var excluded = Required(typeof(MobRankInjector), "IsExcludedFromRanking");
            var suppressed = AccessTools.PropertyGetter(typeof(Trait), "Suppressed");
            var factionChanged = AccessTools.Method(typeof(Pawn), "SetFaction", new[] { typeof(Faction), typeof(Pawn) });
            var retention = AccessTools.PropertyGetter(typeof(IsekaiLevelingSettings), "TamedAnimalBonusRetention");
            if (suppressed == null || factionChanged == null || retention == null || lookups.Count < 40) throw new MissingMethodException("Unsupported RimWorld / Isekai API");
            try
            {
                foreach (var target in lookups) harmony.Patch(target, transpiler: new HarmonyMethod(typeof(PatchPlan), "FilterLookups"));
                foreach (var target in prefixes) harmony.Patch(target.Key, prefix: new HarmonyMethod(typeof(Hooks), target.Value));
                harmony.Patch(excluded, postfix: new HarmonyMethod(typeof(Hooks), "Excluded"));
                harmony.Patch(suppressed, postfix: new HarmonyMethod(typeof(Hooks), "TraitSuppressed"));
                harmony.Patch(factionChanged, postfix: new HarmonyMethod(typeof(Hooks), "FactionChanged"));
                harmony.Patch(retention, postfix: new HarmonyMethod(typeof(Hooks), "Retention"));
            }
            catch
            {
                harmony.UnpatchAll(harmony.Id);
                throw;
            }
            return lookups.Count + prefixes.Count + 4;
        }
    }

    public class RankRefresh : GameComponent
    {
        public RankRefresh(Game game) { }
        public override void FinalizeInit() { RefreshMaps(); }
        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 == 0) RefreshMaps();
        }
        public static void RefreshMaps()
        {
            if (!RankMod.Ready || Current.Game == null || Find.Maps == null) return;
            foreach (Map map in Find.Maps)
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned) RefreshPawn(pawn);
        }
        public static void RefreshPawn(Pawn pawn)
        {
            if (!RankMod.Ready || pawn == null || !pawn.Spawned || pawn.Dead || pawn.RaceProps == null || Policy.Blocked(pawn)) return;
            // Previously excluded pets have isInitialized=true but statsInitialized=false.
            // Recalculate only unseeded stats; never reroll an animal with progression.
            if (Policy.AnimalException(pawn))
            {
                var mob = pawn.GetComp<MobRankComponent>();
                if (mob != null && !mob.statsInitialized) mob.RecalculateRank();
            }
            if (pawn.RaceProps.Humanlike && pawn.Spawned)
            {
                var comp = pawn.GetComp<IsekaiComponent>();
                if (comp != null && !comp.statsInitialized && PawnStatGenerator.ShouldGenerateStats(pawn)) comp.PostSpawnSetup(true);
            }
        }
    }
}

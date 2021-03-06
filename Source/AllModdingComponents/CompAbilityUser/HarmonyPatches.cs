﻿using System;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace AbilityUser
{
    public class AbilityUserMod : Mod
    {
        public AbilityUserMod(ModContentPack content) : base(content)
        {
            var harmony = HarmonyInstance.Create("rimworld.jecrell.abilityuser");
            harmony.Patch(AccessTools.Method(typeof(Targeter), "TargeterUpdate"), null,
                new HarmonyMethod(typeof(AbilityUserMod).GetMethod("TargeterUpdate_PostFix")), null);
            harmony.Patch(AccessTools.Method(typeof(Targeter), "ProcessInputEvents"),
                new HarmonyMethod(typeof(AbilityUserMod).GetMethod("ProcessInputEvents_PreFix")), null);
            harmony.Patch(AccessTools.Method(typeof(Targeter), "ConfirmStillValid"),
                new HarmonyMethod(typeof(AbilityUserMod).GetMethod(nameof(ConfirmStillValid))), null);

            // RimWorld.Targeter
            //private void ConfirmStillValid()

            // Initializes the AbilityUsers on Pawns
            harmony.Patch(AccessTools.Method(typeof(ThingWithComps), "InitializeComps"), null,
                new HarmonyMethod(typeof(AbilityUserMod).GetMethod("InitializeComps_PostFix")), null);

            // when the Pawn_EquipmentTracker is notified of a new item, see if that has CompAbilityItem.
            harmony.Patch(AccessTools.Method(typeof(Pawn_EquipmentTracker), "Notify_EquipmentAdded"), null,
                new HarmonyMethod(typeof(AbilityUserMod).GetMethod("Notify_EquipmentAdded_PostFix")), null);
            // when the Pawn_EquipmentTracker is notified of one less item, see if that has CompAbilityItem.
            harmony.Patch(AccessTools.Method(typeof(Pawn_EquipmentTracker), "Notify_EquipmentRemoved"), null,
                new HarmonyMethod(typeof(AbilityUserMod).GetMethod("Notify_EquipmentRemoved_PostFix")), null);

            // when the Pawn_ApparelTracker is notified of a new item, see if that has CompAbilityItem.
            harmony.Patch(AccessTools.Method(typeof(Pawn_ApparelTracker), "Notify_ApparelAdded"), null,
                new HarmonyMethod(typeof(AbilityUserMod).GetMethod("Notify_ApparelAdded_PostFix")), null);
            // when the Pawn_ApparelTracker is notified of one less item, see if that has CompAbilityItem.
            harmony.Patch(AccessTools.Method(typeof(Pawn_ApparelTracker), "Notify_ApparelRemoved"), null,
                new HarmonyMethod(typeof(AbilityUserMod).GetMethod("Notify_ApparelRemoved_PostFix")), null);

            harmony.Patch(AccessTools.Method(typeof(ShortHashGiver), "GiveShortHash"),
                new HarmonyMethod(typeof(AbilityUserMod), nameof(GiveShortHash_PrePatch)), null);
            
            harmony.Patch(AccessTools.Method(typeof(PawnGroupKindWorker), "GeneratePawns", 
                    new Type[]{typeof(PawnGroupMakerParms), typeof(PawnGroupMaker), typeof(bool)}), null,
                new HarmonyMethod(typeof(AbilityUserMod), nameof(GeneratePawns_PostFix)));
        }
        
        // RimWorld.PawnGroupKindWorker_Normal
        public static void GeneratePawns_PostFix(PawnGroupMakerParms parms, PawnGroupMaker groupMaker, bool errorOnZeroResults, ref List<Pawn> __result)
        {
            //Anyone special?
            if (__result.Any() && __result.FindAll(x => x.TryGetComp<CompAbilityUser>() is CompAbilityUser cu && cu.CombatPoints() > 0) is List<Pawn> specialPawns)
            {
                //Log.Message("Special Pawns Detected");
                //Log.Message("------------------");
                
                //Points
                var previousPoints = parms.points;
                //Log.Message("Points: " +  previousPoints);
                
                //Log.Message("Average Characters");
                //Log.Message("------------------");
                
                //Anyone average?
                int avgPawns = 0;
                var avgCombatPoints = new Dictionary<Pawn, float>();
                if (__result.FindAll(x => x.TryGetComp<CompAbilityUser>() == null) is List<Pawn> averagePawns)
                {
                    avgPawns = averagePawns.Count;
                    averagePawns.ForEach(x =>
                    {
                        avgCombatPoints.Add(x, x.kindDef.combatPower);
                        //Log.Message(x.LabelShort + " : " + x.kindDef.combatPower);
                    });
                    
                }
                
                //Log.Message("------------------");                                
                //Log.Message("Special Characters");
                //Log.Message("------------------");
                
                //What's your powers?
                var specCombatPoints = new Dictionary<Pawn, float>();
                specialPawns.ForEach(x =>
                {
                    var combatValue = x.kindDef.combatPower;
                    foreach (var thingComp in x.AllComps.FindAll(y => y is CompAbilityUser))
                    {
                        //var compAbilityUser = (CompAbilityUser) thingComp;
                        var val = Traverse.Create(thingComp).Method("CombatPoints").GetValue<float>();
                        combatValue += val; //compAbilityUser.CombatPoints();
                    }
                    specCombatPoints.Add(x, combatValue);
                    //Log.Message(x.LabelShort + " : " + combatValue);
                });
                
                
                //Special case -- single raider/character should not be special to avoid problems (e.g. Werewolf raid destroys everyone).
                if (avgPawns == 0 && specCombatPoints.Sum(x => x.Value) > 0 && specialPawns.Count == 1)
                {
                    //Log.Message("Special case called: Single character");
                    specialPawns.First().TryGetComp<CompAbilityUser>().DisableAbilityUser();
                    return;
                }
                
                //Should we rebalance?
                int tryLimit = avgPawns + specialPawns.Count + 1;
                int initTryLimit = tryLimit;
                var tempAvgCombatPoints = new Dictionary<Pawn, float>(avgCombatPoints);
                var tempSpecCombatPoints = new Dictionary<Pawn, float>(specCombatPoints);
                var removedCharacters = new List<Pawn>();
                while (previousPoints < tempAvgCombatPoints.Sum(x => x.Value) + tempSpecCombatPoints.Sum(x => x.Value))
                {
                    
                    //Log.Message("------------------");                                
                    //Log.Message("Rebalance Attempt # " + (initTryLimit - tryLimit + 1));
                    //Log.Message("------------------");
                    //Log.Message("Scenario Points: " + previousPoints + ". Total Points: " + tempAvgCombatPoints.Sum(x => x.Value) + tempSpecCombatPoints.Sum(x => x.Value));
                    
                    //In-case some stupid stuff occurs
                    --tryLimit;
                    if (tryLimit < 0)
                        break;
                    
                    //If special characters outnumber the avg characters, try removing some of the special characters instead.
                    if (tempSpecCombatPoints.Count >= tempAvgCombatPoints.Count)
                    {
                        var toRemove = tempSpecCombatPoints.Keys.RandomElement();
                        //Log.Message("Removed: " + toRemove.LabelShort + " : " + tempSpecCombatPoints[toRemove]);
                        removedCharacters.Add(toRemove);
                        tempSpecCombatPoints.Remove(toRemove);
                    }
                    //If average characters outnumber special characters, then check if the combat value of avg is greater.
                    else if (tempSpecCombatPoints.Count < tempAvgCombatPoints.Count)
                    {
                        //Remove a random average character if the average characters have more combat points for a score
                        if (tempAvgCombatPoints.Sum(x => x.Value) > tempSpecCombatPoints.Sum(x => x.Value))
                        {
                            var toRemove = tempAvgCombatPoints.Keys.RandomElement();
                            //Log.Message("Removed: " + toRemove.LabelShort + " : " + tempSpecCombatPoints[toRemove]);
                            removedCharacters.Add(toRemove);
                            tempAvgCombatPoints.Remove(toRemove);                           
                        }
                        else
                        {
                            var toRemove = tempSpecCombatPoints.Keys.RandomElement();
                            //Log.Message("Removed: " + toRemove.LabelShort + " : " + tempSpecCombatPoints[toRemove]);
                            removedCharacters.Add(toRemove);
                            tempSpecCombatPoints.Remove(toRemove);
                        }
                    }
                }
                avgCombatPoints = tempAvgCombatPoints;
                specCombatPoints = tempSpecCombatPoints;
                
//                Log.Message("------------");                                
//                Log.Message("Final Report");
//                Log.Message("------------");
//                Log.Message("Scenario Points: " + previousPoints + ". Total Points: " + tempAvgCombatPoints.Sum(x => x.Value) + tempSpecCombatPoints.Sum(x => x.Value));
//                Log.Message("------------");
//                Log.Message("Characters");
//                Log.Message("------------------");
                __result.ForEach(x =>
                {
                    var combatValue = x.kindDef.combatPower + x?.TryGetComp<CompAbilityUser>()?.CombatPoints() ?? 0f;
                    //Log.Message(x.LabelShort + " : " + combatValue);
                });
                foreach (var x in removedCharacters)
                {
                    if (x.TryGetComp<CompAbilityUser>() is CompAbilityUser cu && cu.CombatPoints() > 0) cu.DisableAbilityUser();
                    else x.DestroyOrPassToWorld();
                }
                removedCharacters.Clear();
                avgCombatPoints.Clear();
                specCombatPoints.Clear();
            }
        }

        //static HarmonyPatches()
        //{

        //}

        //Verse.ShortHashGiver
        public static bool GiveShortHash_PrePatch(Def def, Type defType)
        {
            //Log.Message("Shorthash called");
            if (def.shortHash != 0)
                if (defType.IsAssignableFrom(typeof(AbilityDef)) || defType == typeof(AbilityDef) ||
                    def is AbilityDef)
                    return false;
            return true;
        }

        public static void Notify_EquipmentAdded_PostFix(Pawn_EquipmentTracker __instance, ThingWithComps eq)
        {
            foreach (var cai in eq.GetComps<CompAbilityItem>()
                ) //((Pawn)__instance.ParentHolder).GetComps<CompAbilityItem>() )
                //Log.Message("Notify_EquipmentAdded_PostFix 1 : "+eq.ToString());
                //Log.Message("  Found CompAbilityItem, for CompAbilityUser of "+cai.Props.AbilityUserClass.ToString());

            foreach (var cau in ((Pawn) __instance.ParentHolder).GetComps<CompAbilityUser>())
                //Log.Message("  Found CompAbilityUser, "+cau.ToString() +" : "+ cau.GetType()+":"+cai.Props.AbilityUserClass ); //Props.AbilityUserTarget.ToString());
                if (cau.GetType() == cai.Props.AbilityUserClass)
                {
                    //Log.Message("  and they match types " );
                    cai.AbilityUserTarget = cau;
                    foreach (var abdef in cai.Props.Abilities) cau.AddWeaponAbility(abdef);
                }
        }

        public static void Notify_EquipmentRemoved_PostFix(Pawn_EquipmentTracker __instance, ThingWithComps eq)
        {
            foreach (var cai in eq.GetComps<CompAbilityItem>()
                ) //((Pawn)__instance.ParentHolder).GetComps<CompAbilityItem>() )
                //Log.Message("Notify_EquipmentAdded_PostFix 1 : "+eq.ToString());
                //Log.Message("  Found CompAbilityItem, for CompAbilityUser of "+cai.Props.AbilityUserClass.ToString());

            foreach (var cau in ((Pawn) __instance.ParentHolder).GetComps<CompAbilityUser>())
                //Log.Message("  Found CompAbilityUser, "+cau.ToString() +" : "+ cau.GetType()+":"+cai.Props.AbilityUserClass ); //Props.AbilityUserTarget.ToString());
                if (cau.GetType() == cai.Props.AbilityUserClass)
                    foreach (var abdef in cai.Props.Abilities) cau.RemoveWeaponAbility(abdef);
        }

        public static void Notify_ApparelAdded_PostFix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            foreach (var cai in apparel.GetComps<CompAbilityItem>()
            ) //((Pawn)__instance.ParentHolder).GetComps<CompAbilityItem>() )
            foreach (var cau in ((Pawn) __instance.ParentHolder).GetComps<CompAbilityUser>())
                if (cau.GetType() == cai.Props.AbilityUserClass)
                {
                    cai.AbilityUserTarget = cau;
                    foreach (var abdef in cai.Props.Abilities) cau.AddApparelAbility(abdef);
                }
        }

        public static void Notify_ApparelRemoved_PostFix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            foreach (var cai in apparel.GetComps<CompAbilityItem>()
            ) //((Pawn)__instance.ParentHolder).GetComps<CompAbilityItem>() )
            foreach (var cau in ((Pawn) __instance.ParentHolder).GetComps<CompAbilityUser>())
                if (cau.GetType() == cai.Props.AbilityUserClass)
                    foreach (var abdef in cai.Props.Abilities) cau.RemoveApparelAbility(abdef);
        }

        // RimWorld.Targeter
        public static bool ConfirmStillValid(Targeter __instance)
        {
            if (__instance.targetingVerb is Verb_UseAbility)
            {
                var caster = Traverse.Create(__instance).Field("caster").GetValue<Pawn>();

                if (caster != null && (caster.Map != Find.VisibleMap || caster.Destroyed ||
                                       !Find.Selector.IsSelected(caster) ||
                                       caster.Faction != Faction.OfPlayerSilentFail))
                    __instance.StopTargeting();
                if (__instance.targetingVerb != null)
                {
                    var selector = Find.Selector;
                    if (__instance.targetingVerb.caster.Map != Find.VisibleMap ||
                        __instance.targetingVerb.caster.Destroyed ||
                        !selector.IsSelected(__instance.targetingVerb.caster))
                    {
                        __instance.StopTargeting();
                    }
                    else
                    {
                        if (!__instance.targetingVerbAdditionalPawns.NullOrEmpty())
                            for (var i = 0; i < __instance.targetingVerbAdditionalPawns.Count; i++)
                                if (__instance.targetingVerbAdditionalPawns[i].Destroyed ||
                                    !selector.IsSelected(__instance.targetingVerbAdditionalPawns[i]))
                                {
                                    __instance.StopTargeting();
                                    break;
                                }
                    }
                }
                return false;
            }
            return true;
        }


        // RimWorld.Targeter
        public static bool ProcessInputEvents_PreFix(Targeter __instance)
        {
            if (__instance.targetingVerb is Verb_UseAbility v)
            {
                if (v.UseAbilityProps.AbilityTargetCategory == AbilityTargetCategory.TargetSelf)
                {
                    var caster = (Pawn) __instance.targetingVerb.caster;
                    v.Ability.TryCastAbility(AbilityContext.Player,
                        caster); // caster, source.First<LocalTargetInfo>(), caster.GetComp<CompAbilityUser>(), (Verb_UseAbility)__instance.targetingVerb, ((Verb_UseAbility)(__instance.targetingVerb)).ability.powerdef as AbilityDef)?.Invoke();
                    SoundDefOf.TickHigh.PlayOneShotOnCamera();
                    __instance.StopTargeting();
                    Event.current.Use();
                    return false;
                }
                AccessTools.Method(typeof(Targeter), "ConfirmStillValid").Invoke(__instance, null);
                if (Event.current.type == EventType.MouseDown)
                    if (Event.current.button == 0 && __instance.IsTargeting)
                    {
                        var obj = (LocalTargetInfo) AccessTools.Method(typeof(Targeter), "CurrentTargetUnderMouse")
                            .Invoke(__instance, new object[] {false});
                        if (obj.IsValid)
                            v.Ability.TryCastAbility(AbilityContext.Player, obj);
                        SoundDefOf.TickHigh.PlayOneShotOnCamera(null);
                        __instance.StopTargeting();
                        Event.current.Use();
                        return false;
                        //if (__instance.targetingVerb is Verb_UseAbility)
                        //{
                        //    Verb_UseAbility abilityVerb = __instance.targetingVerb as Verb_UseAbility;
                        //    if (abilityVerb.Ability.Def.MainVerb.AbilityTargetCategory != AbilityTargetCategory.TargetSelf)
                        //    {
                        //        TargetingParameters targetParams = abilityVerb.Ability.Def.MainVerb.targetParams;
                        //        if (targetParams != null)
                        //        {
                        //            IEnumerable<LocalTargetInfo> source = GenUI.TargetsAtMouse(targetParams, false);

                        //            if (source != null && source.Count<LocalTargetInfo>() > 0)
                        //            {

                        //                if (source.Any<LocalTargetInfo>())
                        //                {

                        //                    Pawn caster = (Pawn)__instance.targetingVerb.caster;
                        //                    abilityVerb.Ability.TryCastAbility(AbilityContext.Player, source.First<LocalTargetInfo>());// caster, source.First<LocalTargetInfo>(), caster.GetComp<CompAbilityUser>(), (Verb_UseAbility)__instance.targetingVerb, ((Verb_UseAbility)(__instance.targetingVerb)).ability.powerdef as AbilityDef)?.Invoke();
                        //                    SoundDefOf.TickHigh.PlayOneShotOnCamera();
                        //                    __instance.StopTargeting();
                        //                    Event.current.Use();
                        //                    return false;
                        //                }
                        //            }
                        //        }
                        //    }
                        //    else
                        //    {
                        //        Pawn caster = (Pawn)__instance.targetingVerb.caster;
                        //        abilityVerb.Ability.TryCastAbility(AbilityContext.Player, null);// caster.GetComp<CompAbilityUser>(), (Verb_UseAbility)__instance.targetingVerb, ((Verb_UseAbility)(__instance.targetingVerb)).ability.powerdef as AbilityDef)?.Invoke();
                        //        SoundDefOf.TickHigh.PlayOneShotOnCamera();
                        //        __instance.StopTargeting();
                        //        Event.current.Use();
                        //        return false;
                        //    }
                        //}
                        //}
                    }
            }
            return true;
        }

        public static void TargeterUpdate_PostFix(Targeter __instance)
        {
            if (__instance.targetingVerb is Verb_UseAbility tVerb &&
                tVerb.verbProps is VerbProperties_Ability tVerbProps)
            {
                if (tVerbProps?.range > 0)
                    GenDraw.DrawRadiusRing(tVerb.CasterPawn.PositionHeld, tVerbProps.range);
                if (tVerbProps?.TargetAoEProperties?.range > 0 && Find.VisibleMap is Map map &&
                    UI.MouseCell().InBounds(map))
                    GenDraw.DrawRadiusRing(UI.MouseCell(), tVerbProps.TargetAoEProperties.range);
            }
        }

        public static void InitializeComps_PostFix(ThingWithComps __instance)
        {
            if (__instance is Pawn p) InternalAddInAbilityUsers(p);
        }

        //// Catches loading of Pawns
        //public static void ExposeData_PostFix(Pawn __instance)
        //{ HarmonyPatches.internalAddInAbilityUsers(__instance); }

        //// Catches generation of Pawns
        //public static void GeneratePawn_PostFix(PawnGenerationRequest request, Pawn __result)
        //{ HarmonyPatches.internalAddInAbilityUsers(__result); }

        // Add in any AbilityUser Components, if the Pawn is accepting
        public static void InternalAddInAbilityUsers(Pawn pawn)
        {
            //            Log.Message("Trying to add AbilityUsers to Pawn");
            if (pawn != null && pawn.RaceProps != null && pawn.RaceProps.Humanlike)
                AbilityUserUtility.TransformPawn(pawn);
        }
    }
}
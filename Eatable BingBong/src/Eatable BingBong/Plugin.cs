using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Peak.Afflictions;
using System.Collections;
using UnityEngine;
using Zorro.Core;

namespace Eatable_BingBong
{
    [BepInAutoPlugin]
    public partial class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log { get; private set; } = null!;

        private static Harmony harmony = null!;

        private void Awake()
        {
            Log = Logger;

            harmony = new Harmony(Id);
            harmony.PatchAll();
            Log.LogInfo($"Plugin {Name} is loaded!");

            gameObject.AddComponent<CrabManager>();
        }
    }

    public class CrabManager : MonoBehaviour
    {
        public static CrabManager instance = null!;

        private void Awake()
        {
            instance = this;
        }

        public static void TriggerGiveCrab(Character character)
        {
            if (character == null)
            {
                Debug.LogError("Cannot give crab to a null character.");
                return;
            }

            if (instance == null)
            {
                Debug.LogError("CrabManager instance is missing from the scene.");
                return;
            }

            instance.StartCoroutine(GiveCrabRoutine(character));
        }

        private static IEnumerator GiveCrabRoutine(Character character)
        {
            var longWaitInstruction = new WaitForSeconds(60f);
            yield return longWaitInstruction;

            var waitInstruction = new WaitForSeconds(1f);

            for (var i = 1; i < 50; i++)
            {
                if (character == null || character.refs.afflictions == null)
                {
                    yield break;
                }

                if (i <= 4)
                {
                    character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Drowsy, 0.15f);
                }

                character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Crab, 0.01f);

                yield return waitInstruction;
            }
        }
    }

    [HarmonyPatch]
    public static class PatchesBingBong
    {
        public static SFX_Instance Lollipop;

        public static void BingBong(Item __instance)
        {
            __instance.UIData.hasSecondInteract = true;
            __instance.UIData.secondaryInteractPrompt = "eat";

            __instance._useFeedback.useAnimation = "Eat";

            __instance.OnSecondaryStarted += () =>
            {
                __instance._useFeedback.useAnimation = "Eat";
                __instance.usingTimePrimary = 2f;
            };

            __instance.OnPrimaryStarted += () =>
            {
                __instance._useFeedback.useAnimation = "";
                __instance.usingTimePrimary = 0.25f;
            };

            __instance.OnSecondaryFinishedCast += () =>
            {
                var cooking = __instance.transform.GetComponent<ItemCooking>();

                GlobalEvents.TriggerItemConsumed(__instance, __instance.holderCharacter);
                Optionable<byte> currentSelectedSlot = __instance.holderCharacter.refs.items.currentSelectedSlot;
                __instance.holderCharacter.player.EmptySlot(currentSelectedSlot);
                __instance.holderCharacter.refs.items.EquipSlot(currentSelectedSlot);

                __instance.holderCharacter.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Curse, 0.1f * (cooking.timesCookedLocal + 1));

                var restoreHunger = new Action_RestoreHunger { item = __instance, restorationAmount = 0.8f };
                restoreHunger.RunAction();

                var drowsy = new Affliction_AdjustDrowsyOverTime
                {
                    character = __instance.holderCharacter,
                    statusPerSecond = 0.1f,
                    totalTime = 10f
                };

                var infStamina = new Affliction_InfiniteStamina
                {
                    character = __instance.holderCharacter,
                    drowsyAffliction = drowsy,
                    climbDelay = 3f,
                    totalTime = 15f + cooking.timesCookedLocal,
                };

                var staminaBuff = new Action_ApplyAffliction { item = __instance, affliction = infStamina };
                staminaBuff.RunAction();
            };
        }

        public static void AncientIdol(Item __instance)
        {
            __instance.UIData.hasMainInteract = true;
            __instance.UIData.mainInteractPrompt = "eat";

            __instance._useFeedback.useAnimation = "Eat";

            __instance.OnPrimaryStarted += () =>
            {
                __instance._useFeedback.useAnimation = "Eat";
                __instance.usingTimePrimary = 2f;
            };

            __instance.OnPrimaryFinishedCast += () =>
            {
                var cooking = __instance.transform.GetComponent<ItemCooking>();

                CrabManager.TriggerGiveCrab(__instance.holderCharacter);

                GlobalEvents.TriggerItemConsumed(__instance, __instance.holderCharacter);
                Optionable<byte> currentSelectedSlot = __instance.holderCharacter.refs.items.currentSelectedSlot;
                __instance.holderCharacter.player.EmptySlot(currentSelectedSlot);
                __instance.holderCharacter.refs.items.EquipSlot(currentSelectedSlot);

                __instance.holderCharacter.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Curse, 0.2f * (cooking.timesCookedLocal + 1));

                var restoreHunger = new Action_RestoreHunger { item = __instance, restorationAmount = 1f };
                restoreHunger.RunAction();

                var infStamina = new Affliction_InfiniteStamina
                {
                    character = __instance.holderCharacter,
                    climbDelay = 3f,
                    totalTime = 10f + cooking.timesCookedLocal,
                };

                var invincibility = new Affliction_Invincibility
                {
                    character = __instance.holderCharacter,
                    isFromMilk = true,
                    totalTime = 60f
                };

                var staminaBuff = new Action_ApplyAffliction { item = __instance, affliction = infStamina };
                staminaBuff.RunAction();

                var milkBuff = new Action_ApplyAffliction { item = __instance, affliction = invincibility };
                milkBuff.RunAction();
            };
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Item), nameof(Item.Awake))]
        public static void GetBingBong(Item __instance)
        {
            if (__instance.itemID == 13) BingBong(__instance);
            if (__instance.itemID == 47) AncientIdol(__instance);
        }
    }
}
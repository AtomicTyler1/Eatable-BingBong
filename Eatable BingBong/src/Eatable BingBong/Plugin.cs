using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Peak.Afflictions;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zorro.Core;
using static Zorro.ControllerSupport.Rumble.RumbleClip;

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
            gameObject.AddComponent<ScoutmasterEffectManager>();
            gameObject.AddComponent<GhostManager>();
        }
    }

    public class ScoutmasterEffectManager : MonoBehaviour
    {
        public static ScoutmasterEffectManager instance = null!;
        private readonly Dictionary<Character, float> activeEffects = new();

        private void Awake()
        {
            instance = this;
        }

        private void Update()
        {
            if (activeEffects.Count == 0)
            {
                return;
            }

            var expiredKeys = new List<Character>();

            foreach (var kvp in activeEffects)
            {
                var character = kvp.Key;
                var expiryTime = kvp.Value;

                if (character == null || character.data == null)
                {
                    expiredKeys.Add(character);
                    continue;
                }

                if (Time.time < expiryTime)
                {
                    character.data.myersDistance = 5f;

                    if (character.IsLocal)
                    {
                        if (Scoutmaster.GetPrimaryScoutmaster(out var scoutmaster) && scoutmaster != null && scoutmaster.mat != null)
                        {
                            scoutmaster.mat.SetFloat(Shader.PropertyToID("_Strength"), 1f);
                            scoutmaster.mat.SetFloat(Shader.PropertyToID("_GrainMult"), GUIManager.instance != null && GUIManager.instance.photosensitivity ? 0f : 1f);
                        }
                    }
                }
                else
                {
                    expiredKeys.Add(character);
                }
            }

            foreach (var key in expiredKeys)
            {
                activeEffects.Remove(key);
            }
        }

        public static void TriggerScoutmasterEffect(Character character, float duration)
        {
            if (character == null)
            {
                Debug.LogError("Cannot apply Scoutmaster effect to a null character.");
                return;
            }

            if (instance == null)
            {
                Debug.LogError("ScoutmasterEffectManager instance is missing from the scene.");
                return;
            }

            instance.activeEffects[character] = Time.time + duration;
        }
    }

    public class GhostManager : MonoBehaviour
    {
        public static GhostManager instance = null!;

        public static float OrbitSpeed = 0.5f;
        public static float OrbitRadius = 7f;

        private void Awake()
        {
            instance = this;
        }

        public static void SpawnGhosts(Character targetCharacter, int count, float duration)
        {
            if (targetCharacter == null || !targetCharacter.IsLocal)
            {
                return;
            }

            if (instance == null)
            {
                Debug.LogError("GhostManager instance is missing from the scene.");
                return;
            }

            instance.StartCoroutine(GhostRoutine(targetCharacter, count, duration));
        }

        private static IEnumerator GhostRoutine(Character targetCharacter, int count, float duration)
        {
            GameObject ghostPrefab = Resources.Load<GameObject>("PlayerGhost");
            if (ghostPrefab == null)
            {
                var allObjs = Resources.FindObjectsOfTypeAll<GameObject>();
                foreach (var obj in allObjs)
                {
                    if (obj.name.Contains("Ghost") && obj.GetComponent<PlayerGhost>() != null)
                    {
                        ghostPrefab = obj;
                        break;
                    }
                }
            }

            if (ghostPrefab == null)
            {
                Debug.LogWarning("Could not find PlayerGhost prefab via Resources or memory search.");
                yield break;
            }

            var activeGhosts = new List<PlayerGhost>();
            var ghostDataList = new List<OrbitData>();
            var customization = Singleton<Customization>.Instance;

            for (int i = 0; i < count; i++)
            {
                float angle = (i * Mathf.PI * 2f) / count;
                Vector3 spawnOffset = new Vector3(Mathf.Cos(angle) * OrbitRadius, 1f, Mathf.Sin(angle) * OrbitRadius);
                Vector3 spawnPos = targetCharacter.Center + spawnOffset;

                var clonedObj = Instantiate(ghostPrefab, spawnPos, Quaternion.identity);
                var ghost = clonedObj.GetComponent<PlayerGhost>();

                if (ghost != null)
                {
                    ghost.m_target = targetCharacter;
                    ghost.m_owner = targetCharacter;

                    if (customization != null)
                    {
                        var randomData = new PersistentPlayerData
                        {
                            customizationData =
                            {
                                currentSkin = Random.Range(0, customization.skins.Count()),
                                currentEyes = Random.Range(0, customization.eyes.Count()),
                                currentMouth = Random.Range(0, customization.mouths.Count()),
                                currentAccessory = Random.Range(0, customization.accessories.Count())
                            }
                        };

                        var customizeMethod = typeof(PlayerGhost).GetMethod("CustomizeGhost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        customizeMethod?.Invoke(ghost, new object[] { randomData });
                    }

                    if (ghost.PlayerRenderers != null)
                    {
                        foreach (var r in ghost.PlayerRenderers)
                        {
                            if (r != null) r.enabled = true;
                        }
                    }

                    if (ghost.EyeRenderers != null)
                    {
                        foreach (var r in ghost.EyeRenderers)
                        {
                            if (r != null) r.enabled = true;
                        }
                    }

                    if (ghost.mouthRenderer != null) ghost.mouthRenderer.enabled = true;
                    if (ghost.accessoryRenderer != null) ghost.accessoryRenderer.enabled = true;
                    if (ghost.thirdEye != null) ghost.thirdEye.SetActive(false);

                    activeGhosts.Add(ghost);
                    ghostDataList.Add(new OrbitData { ghost = ghost, angleOffset = angle, bobSpeed = Random.Range(1.5f, 3f), bobHeight = Random.Range(0.8f, 1.8f), isApproaching = false });
                }
            }

            float elapsed = 0f;
            int targetJumpscares = 5;
            int jumpscaresTriggered = 0;
            float nextTriggerInterval = duration / (targetJumpscares + 1f);
            float nextTriggerTime = Random.Range(nextTriggerInterval * 0.5f, nextTriggerInterval * 1.5f);

            while (elapsed < duration)
            {
                if (targetCharacter == null)
                {
                    break;
                }

                if (jumpscaresTriggered < targetJumpscares && elapsed >= nextTriggerTime)
                {
                    var availableData = ghostDataList.Where(d => d != null && d.ghost != null && !d.isApproaching).ToList();
                    if (availableData.Count > 0)
                    {
                        var chosenData = availableData[Random.Range(0, availableData.Count)];
                        instance.StartCoroutine(GhostApproachRoutine(chosenData, targetCharacter));
                        jumpscaresTriggered++;
                    }

                    float remainingDuration = Mathf.Max(1f, duration - elapsed);
                    nextTriggerTime = elapsed + Random.Range(3f, remainingDuration / (targetJumpscares - jumpscaresTriggered + 1));
                }

                float currentOrbitAngle = Time.time * OrbitSpeed;

                for (int i = 0; i < ghostDataList.Count; i++)
                {
                    var data = ghostDataList[i];
                    if (data.ghost != null)
                    {
                        if (data.isApproaching)
                        {
                            continue;
                        }

                        float totalAngle = currentOrbitAngle + data.angleOffset;
                        float x = targetCharacter.Center.x + Mathf.Cos(totalAngle) * OrbitRadius;
                        float z = targetCharacter.Center.z + Mathf.Sin(totalAngle) * OrbitRadius;
                        float y = targetCharacter.Center.y + 1f + Mathf.Sin(Time.time * data.bobSpeed + i) * data.bobHeight;

                        Vector3 targetPos = new Vector3(x, y, z);
                        data.ghost.transform.position = Vector3.Lerp(data.ghost.transform.position, targetPos, Time.deltaTime * 5f);

                        if (MainCamera.instance != null && MainCamera.instance.cam != null)
                        {
                            Quaternion lookRot = Quaternion.LookRotation(MainCamera.instance.cam.transform.position - data.ghost.transform.position);
                            data.ghost.transform.rotation = Quaternion.Slerp(data.ghost.transform.rotation, lookRot, Time.deltaTime * 5f);
                        }
                    }
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            foreach (var ghost in activeGhosts)
            {
                if (ghost != null)
                {
                    Destroy(ghost.gameObject);
                }
            }
        }

        private static IEnumerator GhostApproachRoutine(OrbitData data, Character targetCharacter)
        {
            var ghost = data.ghost;
            if (ghost == null || targetCharacter == null)
            {
                yield break;
            }

            data.isApproaching = true;
            Vector3 startPos = ghost.transform.position;

            Transform camTransform = MainCamera.instance != null && MainCamera.instance.cam != null
                ? MainCamera.instance.cam.transform
                : targetCharacter.transform;

            Vector3 frontPos = camTransform.position + camTransform.forward * 1.8f;

            float elapsed = 0f;
            float duration = 0.35f;
            while (elapsed < duration && ghost != null)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                ghost.transform.position = Vector3.Lerp(startPos, frontPos, t);

                if (camTransform != null)
                {
                    ghost.transform.rotation = Quaternion.LookRotation(camTransform.position - ghost.transform.position);
                }
                yield return null;
            }

            float lingerElapsed = 0f;
            float lingerDuration = 1.5f;
            while (lingerElapsed < lingerDuration && ghost != null)
            {
                lingerElapsed += Time.deltaTime;
                if (camTransform != null)
                {
                    frontPos = camTransform.position + camTransform.forward * 1.8f;
                    ghost.transform.position = Vector3.Lerp(ghost.transform.position, frontPos, Time.deltaTime * 10f);
                    ghost.transform.rotation = Quaternion.LookRotation(camTransform.position - ghost.transform.position);
                }
                yield return null;
            }

            float retreatElapsed = 0f;
            float retreatDuration = 0.4f;
            Vector3 retreatStartPos = ghost != null ? ghost.transform.position : frontPos;
            while (retreatElapsed < retreatDuration && ghost != null)
            {
                retreatElapsed += Time.deltaTime;
                float t = retreatElapsed / retreatDuration;

                float currentOrbitAngle = Time.time * OrbitSpeed;
                float totalAngle = currentOrbitAngle + data.angleOffset;
                float x = targetCharacter.Center.x + Mathf.Cos(totalAngle) * OrbitRadius;
                float z = targetCharacter.Center.z + Mathf.Sin(totalAngle) * OrbitRadius;
                float y = targetCharacter.Center.y + 1f + Mathf.Sin(Time.time * data.bobSpeed) * data.bobHeight;
                Vector3 targetOrbitPos = new Vector3(x, y, z);

                ghost.transform.position = Vector3.Lerp(retreatStartPos, targetOrbitPos, t);

                if (camTransform != null)
                {
                    ghost.transform.rotation = Quaternion.LookRotation(camTransform.position - ghost.transform.position);
                }
                yield return null;
            }

            data.isApproaching = false;
        }

        private class OrbitData
        {
            public PlayerGhost ghost;
            public float angleOffset;
            public float bobSpeed;
            public float bobHeight;
            public bool isApproaching;
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
            var waitTime = new WaitForSeconds(60f);
            yield return waitTime;

            var tick = new WaitForSeconds(1f);

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

                yield return tick;
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

                ScoutmasterEffectManager.TriggerScoutmasterEffect(__instance.holderCharacter, 45f);
                GhostManager.SpawnGhosts(__instance.holderCharacter, 10, 45f);

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

                ApplyAffliction(__instance, infStamina);
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

                ScoutmasterEffectManager.TriggerScoutmasterEffect(__instance.holderCharacter, 90f);
                GhostManager.SpawnGhosts(__instance.holderCharacter, 15, 90f);

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

                var fast = new Affliction_FasterBoi
                {
                    character = __instance.holderCharacter,
                    moveSpeedMod = 1.3f,
                    climbSpeedMod = 1.3f,
                    totalTime = 60f,
                };

                var numb = new Affliction_Numb
                {
                    character = __instance.holderCharacter,
                    totalTime = 60f
                };

                ApplyAffliction(__instance, infStamina);
                ApplyAffliction(__instance, invincibility);
                ApplyAffliction(__instance, fast);
                ApplyAffliction(__instance, numb);
            };
        }

        public static void ApplyAffliction(Item __instance, Affliction affl)
        {
            var aff = new Action_ApplyAffliction { item = __instance, affliction = affl };
            aff.RunAction();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Item), nameof(Item.Awake))]
        public static void GetBingBong(Item __instance)
        {
            if (__instance.itemID == 13 || __instance.itemID == 124) BingBong(__instance);
            if (__instance.itemID == 47) AncientIdol(__instance);
        }
    }
}
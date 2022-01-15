using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using DiskCardGame;
using HarmonyLib;
using InscryptionAPI.Guid;
using InscryptionAPI.Helpers;
using UnityEngine;
using UnityEngine.UI;

namespace InscryptionAPI.Card;

[HarmonyPatch]
public static class AbilityManager
{
    public class FullAbility
    {
        public readonly Ability Id;
        public readonly AbilityInfo Info;
        public readonly Type AbilityBehavior;
        public Texture Texture { get; internal set; }
        public Texture CustomFlippedTexture { get; internal set; }

        public FullAbility(Ability id, AbilityInfo info, Type behaviour, Texture texture)
        {
            Id = id;
            Info = info;
            AbilityBehavior = behaviour;
            Texture = texture;

            TypeManager.Add(id.ToString(), behaviour);
        }
    }

    public readonly static ReadOnlyCollection<FullAbility> BaseGameAbilities = new(GenBaseGameAbilityList());
    private readonly static ObservableCollection<FullAbility> NewAbilities = new();
    
    public static List<FullAbility> AllAbilities { get; private set; } = BaseGameAbilities.ToList();
    public static List<AbilityInfo> AllAbilityInfos { get; private set; } = BaseGameAbilities.Select(x => x.Info).ToList();

    static AbilityManager()
    {
        NewAbilities.CollectionChanged += static (_, _) =>
        {
            AllAbilities = BaseGameAbilities.Concat(NewAbilities).ToList();
            AllAbilityInfos = AllAbilities.Select(x => x.Info).ToList();
        };
    }

    private static List<FullAbility> GenBaseGameAbilityList()
    {           
        bool useReversePatch = true;
        try
        {
            OriginalLoadAbilityIcon(Ability.AllStrike.ToString());
        }
        catch (NotImplementedException)
        {
            useReversePatch = false;
        }

        List<FullAbility> baseGame = new();
        var gameAsm = typeof(AbilityInfo).Assembly;
        foreach (var ability in Resources.LoadAll<AbilityInfo>("Data/Abilities"))
        {
            var name = ability.ability.ToString();
            baseGame.Add(new FullAbility
            (
                ability.ability,
                ability,
                gameAsm.GetType($"DiskCardGame.{name}"),
                useReversePatch ? OriginalLoadAbilityIcon(name) : AbilitiesUtil.LoadAbilityIcon(name)
            ));
        }
        return baseGame;
    }

    public static FullAbility Add(string guid, AbilityInfo info, Type behavior, Texture tex)
    {
        FullAbility full = new(GuidManager.GetEnumValue<Ability>(guid, info.rulebookName), info, behavior, tex);
        full.Info.ability = full.Id;
        NewAbilities.Add(full);
        return full;
    }

    public static AbilityInfo New(string guid, string rulebookName, string rulebookDescription, Type behavior, string pathToArt)
    {
        return New(guid, rulebookName, rulebookDescription, behavior, TextureHelper.GetImageAsTexture(pathToArt));
    }

    public static AbilityInfo New(string guid, string rulebookName, string rulebookDescription, Type behavior, Texture tex)
    {
        AbilityInfo info = ScriptableObject.CreateInstance<AbilityInfo>();
        info.rulebookName = rulebookName;
        info.rulebookDescription = rulebookDescription;
        Add(guid, info, behavior, tex);
        return info;
    }

    public static void Remove(Ability id) => NewAbilities.Remove(NewAbilities.FirstOrDefault(x => x.Id == id));
    public static void Remove(FullAbility ability) => NewAbilities.Remove(ability);
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ScriptableObjectLoader<UnityObject>), nameof(ScriptableObjectLoader<UnityObject>.LoadData))]
    [SuppressMessage("Member Access", "Publicizer001", Justification = "Need to set internal list of abilities")]
    private static void AbilityLoadPrefix()
    {
        ScriptableObjectLoader<AbilityInfo>.allData = AllAbilityInfos;
    }

    [HarmonyReversePatch(HarmonyReversePatchType.Original)]
    [HarmonyPatch(typeof(AbilitiesUtil), nameof(AbilitiesUtil.LoadAbilityIcon))]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Texture OriginalLoadAbilityIcon(string abilityName, bool fillerAbility = false, bool something = false) { throw new NotImplementedException(); }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AbilitiesUtil), nameof(AbilitiesUtil.LoadAbilityIcon))]
    private static bool LoadAbilityIconReplacement(string abilityName, ref Texture __result)
    {
        if (string.IsNullOrEmpty(abilityName))
            return true;

        bool normalTexture = true;
        if (abilityName.EndsWith("_opponent"))
        {
            normalTexture = false;
            abilityName = abilityName.Replace("_opponent", "");
        }

        if (int.TryParse(abilityName, out int abilityId))
        {
            FullAbility ability = AllAbilities.FirstOrDefault(x => x.Id == (Ability)abilityId);
            __result = (normalTexture || ability.CustomFlippedTexture == null) ? ability.Texture : ability.CustomFlippedTexture;
            return false;
        }

        if (Enum.TryParse<Ability>(abilityName, out Ability abilityEnum))
        {
            FullAbility ability = AllAbilities.FirstOrDefault(x => x.Id == abilityEnum);
            __result = (normalTexture || ability.CustomFlippedTexture == null) ? ability.Texture : ability.CustomFlippedTexture;
            return false;
        }

        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AbilitiesUtil), nameof(AbilitiesUtil.GetLearnedAbilities))]
    private static bool GetLearnedAbilitesReplacement(bool opponentUsable, int minPower, int maxPower, AbilityMetaCategory categoryCriteria, ref List<Ability> __result)
    {
        __result = new();

        foreach (var ability in AllAbilityInfos)
        {
            bool canUse = true;
            canUse &= !opponentUsable || ability.opponentUsable;
            canUse &= minPower <= ability.powerLevel && maxPower >= ability.powerLevel;
            canUse &= ability.metaCategories.Contains(categoryCriteria);
            canUse &= ProgressionData.LearnedAbility(ability.ability);

            if (canUse)
            {
                __result.Add(ability.ability);
            }
        }
        
        return false;
    }

    // [HarmonyPrefix]
    // [HarmonyPatch(typeof(CardTriggerHandler), nameof(CardTriggerHandler.AddAbility), new[] { typeof(Ability) })]
    // private static bool AddAbilityReplacement(CardTriggerHandler __instance, Ability ability)
    // {
    //     var full = AllAbilities.FirstOrDefault(x => x.Id == ability);
    //     if (!__instance.triggeredAbilities.Exists(x => x.Item1 == ability) || full.Info.canStack && !full.Info.passive)
    //     {
    //         var reciever = (AbilityBehaviour)__instance.gameObject.GetComponent(full.AbilityBehavior);
    //         if (!reciever)
    //         {
    //             reciever = (AbilityBehaviour)__instance.gameObject.AddComponent(full.AbilityBehavior);
    //         }
    //         __instance.triggeredAbilities.Add(new Tuple<Ability, AbilityBehaviour>(ability, reciever));
    //     }

    //     return false;
    // }

    [HarmonyPatch(typeof(RuleBookInfo), "ConstructPageData", new Type[] { typeof(AbilityMetaCategory) })]
    [HarmonyPostfix]
	public static void FixRulebook(AbilityMetaCategory metaCategory, RuleBookInfo __instance, ref List<RuleBookPageInfo> __result)
    {
        //InscryptionAPIPlugin.Logger.LogInfo($"In rulebook patch: I see {NewAbilities.Count}");
        if (NewAbilities.Count > 0)
        {
            foreach (PageRangeInfo pageRangeInfo in __instance.pageRanges)
            {
                // regular abilities
                if (pageRangeInfo.type == PageRangeType.Abilities)
                {
                    int insertPosition = __result.FindLastIndex(rbi => rbi.pagePrefab == pageRangeInfo.rangePrefab) + 1;
                    int curPageNum = (int)Ability.NUM_ABILITIES;
                    List<FullAbility> abilitiesToAdd = NewAbilities.Where(x => __instance.AbilityShouldBeAdded((int)x.Id, metaCategory)).ToList();
                    //InscryptionAPIPlugin.Logger.LogInfo($"Adding {abilitiesToAdd.Count} out of {NewAbilities.Count} abilities to rulebook");
                    foreach(FullAbility fab in abilitiesToAdd)
                    {
                        RuleBookPageInfo info = new();
                        info.pagePrefab = pageRangeInfo.rangePrefab;
                        info.headerText = string.Format(Localization.Translate("APPENDIX XII, SUBSECTION I - MOD ABILITIES {0}"), curPageNum);
                        __instance.FillAbilityPage(info, pageRangeInfo, (int)fab.Id);
                        __result.Insert(insertPosition, info);
                        curPageNum += 1;
                        insertPosition += 1;
                    }
                }
            }
        }
    }
}
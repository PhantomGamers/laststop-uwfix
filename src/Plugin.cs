using BepInEx;
using BepInEx.Configuration;

using Code;
using Code.Core.Progress;
using Code.Game;

using HarmonyLib;

using System.Linq;

using UnityEngine;

namespace LastStopUWFix
{
    [BepInPlugin("com.github.phantomgamers.laststop_uwfix", "Ultrawide Fix", PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static ConfigEntry<string> resOptions;

        public static BepInEx.Logging.ManualLogSource Log;

        public static float TargetAspect { get; set; } = (float)Display.main.systemWidth / Display.main.systemHeight;

        private void Awake()
        {
            Log = Logger;
            resOptions = Config.Bind("General", "Resolutions", $"{Display.main.systemWidth}x{Display.main.systemHeight}", "Resolutions to add (multiple can be added separated by , )");
            SetupResolutions();
            Harmony.CreateAndPatchAll(typeof(Patches));
        }

        public static void SetupResolutions()
        {
            Log.LogInfo("Adding resolutions...");
            var resolutions = Code.Platform.PlatformManager.ResolutionSettings;

            var configResolutions = resOptions.Value.Split(',').ToList();
            configResolutions.Add($"{Display.main.renderingWidth}x{Display.main.renderingHeight}");

            foreach (var res in configResolutions)
            {
                var splitRes = res.Split('x');
                if (int.TryParse(splitRes[0], out int resWidth) && int.TryParse(splitRes[1], out int resHeight))
                {
                    var resolution = new Code.Platform.PlatformManager.ResolutionSetting()
                    {
                        DisplayName = res,
                        Width = resWidth,
                        Height = resHeight
                    };
                    // Resolutions will be duplicated with different refresh rates, only add the first
                    if (resolutions.Where((currRes) => currRes.DisplayName == resolution.DisplayName).ToArray().Length == 0)
                    {
                        Log.LogDebug($"Adding resolution {resolution.DisplayName}");
                        resolutions.Add(resolution);
                    }
                }
            }

            // sort by width and then by ascending resolution
            var sortedResolutions = resolutions.OrderBy(resolution => resolution.Width).ThenBy(resolution => resolution.Height).ToList();
            // needed for order to show correctly in-game
            resolutions.Clear();
            resolutions.AddRange(sortedResolutions);
        }

        public static void ClampResIdx(ref int aResIdx)
        {
            // Makes sure resolution index is within the bounds of the resolution options
            var resSettings = Code.Platform.PlatformManager.ResolutionSettings;
            if (aResIdx > (resSettings.Count - 1) || aResIdx < 0)
            {
                Log.LogDebug("Clamping resolution index...");
                aResIdx = resSettings.Count - 1;
            }
        }

        public static void RemoveAspectRestraint()
        {
            Log.LogInfo($"Applying aspect {TargetAspect}");
            GameObject.Find("MasterCamera(Clone)").GetComponent<Camera>().aspect = 16f / 9f;
            GameObject.Find("Camera").GetComponent<Camera>().aspect = TargetAspect;
        }

        public static void RestoreAspectRestraint()
        {
            Log.LogInfo("Restoring 16:9 aspect ratio...");
            GameObject.Find("MasterCamera(Clone)").GetComponent<Camera>().aspect = TargetAspect;
            GameObject.Find("Camera").GetComponent<Camera>().aspect = 16f / 9f;
        }
    }

    [HarmonyPatch]
    public class Patches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameOptions), nameof(GameOptions.Apply))]
        public static void Apply_Prefix(Progress progress)
        {
            int ResolutionIdx = progress.GetIntValue(GeneralProgress.ProgressInt.Graphics_ResolutionIdx);
            Plugin.ClampResIdx(ref ResolutionIdx);
            progress.SetIntValue(GeneralProgress.ProgressInt.Graphics_ResolutionIdx, ResolutionIdx);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameRender), nameof(GameRender.SetGameScreenScale))]
        public static void SetGameScreenScale_Postfix(GameRender __instance)
        {
            Plugin.Log.LogInfo($"GameRender Aspect is {__instance._screenAspect}");
            Plugin.TargetAspect = __instance._screenAspect;
            Plugin.RemoveAspectRestraint();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UnityEngine.Video.VideoPlayer), nameof(UnityEngine.Video.VideoPlayer.Play))]
        public static void RestoreCutsceneAspectRestraints()
        {
            Plugin.Log.LogInfo("CutSceneStart");
            Plugin.RestoreAspectRestraint();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnityEngine.Video.VideoPlayer), nameof(UnityEngine.Video.VideoPlayer.Stop))]
        [HarmonyPatch(typeof(MoonLakeGame), nameof(MoonLakeGame.DoCutscene))]
        public static void RemoveCutsceneAspectRestraints()
        {
            Plugin.Log.LogInfo("CutSceneEnd");
            Plugin.RemoveAspectRestraint();
        }

#if DEBUG
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Code.UI.Menu.MenuPageStart), nameof(Code.UI.Menu.MenuPageStart.ReadSettings))]
        public static void EnableChapterSelection(Code.UI.Menu.MenuPageStart __instance)
        {
            __instance._chapterSelectAllowed = true;
        }
#endif
    }
}
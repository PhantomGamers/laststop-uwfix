using BepInEx;
using BepInEx.Configuration;

using Code.Core.Progress;

using HarmonyLib;

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace BepInExPlugin
{
    /// <summary>
    /// This is the main plugin class that BepInEx injects and executes.
    /// This class provides MonoBehaviour methods and additional BepInEx-specific services like logging and
    /// configuration system.
    /// </summary>
    /// <remarks>
    /// BepInEx plugins are MonoBehaviours. Refer to Unity documentation for information on how to use various Unity
    /// events: https://docs.unity3d.com/Manual/class-MonoBehaviour.html
    ///
    /// To get started, check out the plugin writing walkthrough:
    /// https://bepinex.github.io/bepinex_docs/master/articles/dev_guide/plugin_tutorial/index.html
    /// </remarks>
    [BepInPlugin(PluginInfo.PLUGIN_ID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string RESOLUTIONS = "2560x1080,3440x1440,3840x1080,5120x1440";
        public static ConfigEntry<string> resOptions;
        public static BepInEx.Logging.ManualLogSource log;
        private void Awake()
        {
            log = Logger;
            resOptions = Config.Bind("General", "Resolutions", RESOLUTIONS, "Resolutions to add");
            SetupResolutions();
            Harmony.CreateAndPatchAll(typeof(Patches));
        }

        public static void SetupResolutions()
        {
            log.LogInfo("Adding resolutions...");
            var resolutions = Code.Platform.PlatformManager.ResolutionSettings;
            foreach (var res in resOptions.Value.Split(','))
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
                        log.LogDebug($"Adding resolution {resolution.DisplayName}");
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
                log.LogDebug("Clamping resolution index...");
                aResIdx = resSettings.Count - 1;
            }
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

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(Code.Game.GameRender), "SetGameScreenScale")]
        public static IEnumerable<CodeInstruction> SetGameScreenScale_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            /*
             * Replaces
             * if (this._gameScreenScaleFactor < 1f)
             * With
             * if (this._gameScreenScaleFactor > 1f || this._gameScreenScaleFactor < 1f)
             * In Code.Game.GameRender.SetGameScreenScale
            */

            Plugin.log.LogInfo("Patching Code.Game.GameRender.SetGameScreenScale...");
            CodeMatcher codeMatcher = new CodeMatcher(instructions)
                        .MatchForward(false,
                            new CodeMatch(OpCodes.Ldarg_0),
                            new CodeMatch(i => i.opcode == OpCodes.Ldfld && ((FieldInfo)i.operand).Name == "_gameScreenScaleFactor"),
                            new CodeMatch(OpCodes.Ldc_R4),
                            new CodeMatch(OpCodes.Bge_Un)
                        );

            if (codeMatcher.IsInvalid)
            {
                Plugin.log.LogError("Patch failed, fix version is not compatible with game version.");
                return instructions;
            }

            CodeInstruction[] checkInstructions = new CodeInstruction[4];
            for (int i = 0; i < checkInstructions.Length; i++)
            {
                checkInstructions[i] = codeMatcher.InstructionAt(i);
            }

            foreach (var instruction in checkInstructions)
            {
                codeMatcher = codeMatcher.InsertAndAdvance(instruction);
            }

            return codeMatcher
                   .Advance(-1)
                   .SetOpcodeAndAdvance(OpCodes.Ble_Un)
                   .InstructionEnumeration();
        }
    }
}

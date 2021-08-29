﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using SoG.Modding.API;
using SoG.Modding.Core;
using SoG.Modding.Extensions;
using SoG.Modding.Utils;

namespace SoG.Modding.Patches
{
    using CodeList = System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction>;

    /// <summary>
    /// Contains patches for Game1 class.
    /// </summary>

    [HarmonyPatch(typeof(Game1))]
    internal static class Game1Patches
    {

        /// <summary>
        /// Starts the API, which loads mods and their content.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("Initialize")]
        internal static void OnGame1Initialize()
        {
            Globals.API.Start();
        }

        /// <summary>
        /// Implements custom command parsing.
        /// </summary>
        [HarmonyTranspiler]
        [HarmonyPatch("_Chat_ParseCommand")]
        internal static CodeList CommandTranspiler(CodeList code, ILGenerator gen)
        {
            Label afterRet = gen.DefineLabel();

            MethodInfo target = typeof(string).GetMethod("ToLowerInvariant");

            var insert = new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldloc_S, 2),
                new CodeInstruction(OpCodes.Ldarg_S, 1),
                new CodeInstruction(OpCodes.Ldarg_S, 2),
                new CodeInstruction(OpCodes.Call, typeof(HelperCallbacks).GetPrivateStaticMethod(nameof(HelperCallbacks.InChatParseCommand))),
                new CodeInstruction(OpCodes.Brfalse, afterRet),
                new CodeInstruction(OpCodes.Ret),
                new CodeInstruction(OpCodes.Nop).WithLabels(afterRet)
            };

            return PatchTools.InsertAfterMethod(code, target, insert);
        }

        /// <summary>
        /// Implements failsafe custom texture paths for shields.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("_Animations_GetAnimationSet", typeof(PlayerView), typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(bool))]
        internal static bool OnGetAnimationSet(PlayerView xPlayerView, string sAnimation, string sDirection, bool bWithWeapon, bool bCustomHat, bool bWithShield, bool bWeaponOnTop, ref PlayerAnimationTextureSet __result)
        {
            ContentManager VanillaContent = RenderMaster.contPlayerStuff;

            __result = new PlayerAnimationTextureSet() { bWeaponOnTop = bWeaponOnTop };

            Tools.TryLoadTex($"Sprites/Heroes/{sAnimation}/{sDirection}", VanillaContent, out __result.txBase);

            string resource = xPlayerView.xEquipment.DisplayShield?.sResourceName ?? "";
            if (bWithShield && resource != "")
            {
                var enType = xPlayerView.xEquipment.DisplayShield.enItemType;
                bool modItem = enType.IsFromMod();

                if (modItem)
                {
                    Tools.TryLoadTex($"{resource}/{sAnimation}/{sDirection}", Globals.API.Registry.Library.Items[enType].Config.Manager, out __result.txShield);
                }
                else
                {
                    Tools.TryLoadTex($"Sprites/Heroes/{sAnimation}/Shields/{resource}/{sDirection}", VanillaContent, out __result.txShield);
                }
            }

            if (bWithWeapon)
                __result.txWeapon = RenderMaster.txNullTex;

            return false; // Never executes the original
        }

        /// <summary>
        /// Implements failsafe custom texture paths for perks.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("_RogueLike_GetPerkTexture")]
        internal static bool OnGetPerkTexture(RogueLikeMode.Perks enPerk, ref Texture2D __result)
        {
            if (!enPerk.IsFromMod())
                return true;

            string path = Globals.API.Registry.Library.Perks[enPerk].Config.TexturePath;

            Tools.TryLoadTex(path, Globals.Game.Content, out __result);

            return false;
        }

        /// <summary>
        /// Implements failsafe custom texture paths for treats and perks.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("_RogueLike_GetTreatCurseTexture")]
        internal static bool OnGetTreatCurseTexture(RogueLikeMode.TreatsCurses enTreat, ref Texture2D __result)
        {
            if (!enTreat.IsFromMod())
                return true;

            string path = Globals.API.Registry.Library.Curses[enTreat].Config.TexturePath;

            Tools.TryLoadTex(path, Globals.Game.Content, out __result);

            return false;
        }

        /// <summary>
        /// Implements custom treats and curses.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("_RogueLike_GetTreatCurseInfo")]
        internal static bool OnGetTreatCurseInfo(RogueLikeMode.TreatsCurses enTreatCurse, out string sNameHandle, out string sDescriptionHandle, out float fScoreModifier)
        {
            if (!enTreatCurse.IsFromMod())
            {
                sNameHandle = "";
                sDescriptionHandle = "";
                fScoreModifier = 1f;
                return true;
            }

            var entry = Globals.API.Registry.Library.Curses[enTreatCurse];

            sNameHandle = entry.NameHandle;
            sDescriptionHandle = entry.DescriptionHandle;
            fScoreModifier = entry.Config.ScoreModifier;

            return false;
        }

        /// <summary>
        /// Implements activation code for custom perks.
        /// Activation happens when a run starts, and can be used to modify player stats, etc.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("_RogueLike_ActivatePerks")]
        internal static void PostPerkActivation(PlayerView xView, List<RogueLikeMode.Perks> len)
        {
            foreach (var perk in len)
            {
                if (perk.IsFromMod())
                    Globals.API.Registry.Library.Perks[perk].Config.RunStartActivator?.Invoke(xView);
            }
        }

        /// <summary>
        /// Implements saving of extra information for ".cha" save files.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("_Saving_SaveCharacterToFile")]
        internal static void PostCharacterSave(int iFileSlot)
        {
            string ext = ModSaving.SaveFileExtension;

            PlayerView player = Globals.Game.xLocalPlayer;
            string appData = Globals.Game.sAppData;

            int carousel = player.iSaveCarousel - 1;
            if (carousel < 0)
                carousel += 5;

            string backupPath = "";

            string chrFile = $"{appData}Characters/" + $"{iFileSlot}.cha{ext}";

            if (File.Exists(chrFile))
            {
                if (player.sSaveableName == "")
                {
                    player.sSaveableName = player.sNetworkNickname;
                    foreach (char c in Path.GetInvalidFileNameChars())
                        player.sSaveableName = player.sSaveableName.Replace(c, ' ');
                }

                backupPath = $"{appData}Backups/" + $"{player.sSaveableName}_{player.xJournalInfo.iCollectorID}{iFileSlot}/";
                Tools.TryCreateDirectory(backupPath);

                File.Copy(chrFile, backupPath + $"auto{carousel}.cha{ext}", overwrite: true);

                string wldFile = $"{appData}Worlds/" + $"{iFileSlot}.wld{ext}";
                if (File.Exists(wldFile))
                {
                    File.Copy(wldFile, backupPath + $"auto{carousel}.wld{ext}", overwrite: true);
                }
            }

            using (BinaryWriter bw = new BinaryWriter(new FileStream($"{chrFile}.temp", FileMode.Create, FileAccess.Write)))
            {
                Globals.Logger.Info($"Saving mod character {iFileSlot}...");
                Globals.API.Saving.SaveModCharacter(bw);
            }

            try
            {
                File.Copy($"{chrFile}.temp", chrFile, overwrite: true);
                if (backupPath != "")
                {
                    File.Copy($"{chrFile}.temp", backupPath + $"latest.cha{ext}", overwrite: true);
                }
                File.Delete($"{chrFile}.temp");
            }
            catch { }
        }

        /// <summary>
        /// Implements saving of extra information for ".wld" save files.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("_Saving_SaveWorldToFile")]
        internal static void PostWorldSave(int iFileSlot)
        {
            string ext = ModSaving.SaveFileExtension;

            PlayerView player = Globals.Game.xLocalPlayer;
            string appData = Globals.Game.sAppData;

            string backupPath = "";
            string chrFile = $"{appData}Characters/" + $"{iFileSlot}.cha{ext}";
            string wldFile = $"{appData}Worlds/" + $"{iFileSlot}.wld{ext}";

            if (File.Exists(chrFile))
            {
                if (player.sSaveableName == "")
                {
                    player.sSaveableName = player.sNetworkNickname;
                    foreach (char c in Path.GetInvalidFileNameChars())
                        player.sSaveableName = player.sSaveableName.Replace(c, ' ');
                }

                backupPath = $"{appData}Backups/" + $"{player.sSaveableName}_{player.xJournalInfo.iCollectorID}{iFileSlot}/";
                Tools.TryCreateDirectory(backupPath);
            }

            using (BinaryWriter bw = new BinaryWriter(new FileStream($"{wldFile}.temp", FileMode.Create, FileAccess.Write)))
            {
                Globals.Logger.Info($"Saving mod world {iFileSlot}...");
                Globals.API.Saving.SaveModWorld(bw);
            }

            try
            {
                File.Copy($"{wldFile}.temp", wldFile, overwrite: true);
                if (backupPath != "" && iFileSlot != 100)
                {
                    File.Copy($"{wldFile}.temp", backupPath + $"latest.wld{ext}", overwrite: true);
                }
                File.Delete($"{wldFile}.temp");
            }
            catch { }
        }

        /// <summary>
        /// Implements saving of extra information for ".sav" save files.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("_Saving_SaveRogueToFile", typeof(string))]
        internal static void PostArcadeSave()
        {
            string ext = ModSaving.SaveFileExtension;

            bool other = CAS.IsDebugFlagSet("OtherArcadeMode");
            string savFile = Globals.Game.sAppData + $"arcademode{(other ? "_other" : "")}.sav{ext}";

            using (BinaryWriter bw = new BinaryWriter(new FileStream($"{savFile}.temp", FileMode.Create, FileAccess.Write)))
            {
                Globals.Logger.Info($"Saving mod arcade...");
                Globals.API.Saving.SaveModArcade(bw);
            }

            File.Copy($"{savFile}.temp", savFile, overwrite: true);
            File.Delete($"{savFile}.temp");
        }

        /// <summary>
        /// Implements loading of extra information for ".cha" save files.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("_Loading_LoadCharacterFromFile")]
        internal static void PostCharacterLoad(int iFileSlot, bool bAppearanceOnly)
        {
            string ext = ModSaving.SaveFileExtension;

            string chrFile = Globals.Game.sAppData + "Characters/" + $"{iFileSlot}.cha{ext}";

            if (!File.Exists(chrFile)) return;

            using (BinaryReader br = new BinaryReader(new FileStream(chrFile, FileMode.Open, FileAccess.Read)))
            {
                Globals.Logger.Info($"Loading mod character {iFileSlot}...");
                Globals.API.Saving.LoadModCharacter(br);
            }
        }

        /// <summary>
        /// Implements loading of extra information for ".wld" save files.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("_Loading_LoadWorldFromFile")]
        internal static void PostWorldLoad(int iFileSlot)
        {
            string ext = ModSaving.SaveFileExtension;

            string wldFile = Globals.Game.sAppData + "Worlds/" + $"{iFileSlot}.wld{ext}";

            if (!File.Exists(wldFile)) return;

            using (BinaryReader br = new BinaryReader(new FileStream(wldFile, FileMode.Open, FileAccess.Read)))
            {
                Globals.Logger.Info($"Loading mod world {iFileSlot}...");
                Globals.API.Saving.LoadModWorld(br);
            }
        }

        /// <summary>
        /// Implements loading of extra information for ".sav" save files.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("_Loading_LoadRogueFile")]
        internal static void PostArcadeLoad()
        {
            string ext = ModSaving.SaveFileExtension;

            if (RogueLikeMode.LockedOutDueToHigherVersionSaveFile) return;

            bool other = CAS.IsDebugFlagSet("OtherArcadeMode");
            string savFile = Globals.Game.sAppData + $"arcademode{(other ? "_other" : "")}.sav{ext}";

            if (!File.Exists(savFile)) return;

            using (BinaryReader br = new BinaryReader(new FileStream(savFile, FileMode.Open, FileAccess.Read)))
            {
                Globals.Logger.Info($"Loading mod arcade...");
                Globals.API.Saving.LoadModArcade(br);
            }
        }

        /// <summary>
        /// Implements mod content loading by inserting a call before important game code runs.
        /// </summary>
        [HarmonyTranspiler]
        [HarmonyPatch("__StartupThreadExecute")]
        internal static CodeList StartupTranspiler(CodeList code, ILGenerator gen)
        {
            MethodInfo target = typeof(DialogueCharacterLoading).GetMethod("Init");

            var insert = new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Call, typeof(HelperCallbacks).GetPrivateStaticMethod(nameof(HelperCallbacks.InContentLoad)))
            };

            return PatchTools.InsertAfterMethod(code, target, insert);
        }

        /// <summary>
        /// Implements custom level load code.
        /// </summary>
        [HarmonyTranspiler]
        [HarmonyPatch("_LevelLoading_DoStuff")]
        internal static CodeList LevelDoStuffTranspiler(CodeList code, ILGenerator gen)
        {
            MethodInfo target = typeof(Quests.QuestLog).GetMethod("UpdateCheck_PlaceVisited");

            var insert = new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldarg_S, 1),
                new CodeInstruction(OpCodes.Ldarg_S, 2),
                new CodeInstruction(OpCodes.Call, typeof(HelperCallbacks).GetPrivateStaticMethod(nameof(HelperCallbacks.InLevelLoadDoStuff)))
            };

            return PatchTools.InsertBeforeMethod(code, target, insert);
        }

        /// <summary>
        /// Implements an updated interface for Treat and Curse shops in Arcade.
        /// The new menus support viewing more than 10 entries at a time.
        /// </summary>
        [HarmonyTranspiler]
        [HarmonyPatch("_ShopMenu_Render_TreatCurseAssign")]
        internal static CodeList RenderTreatCurseAssignTranspiler(CodeList code, ILGenerator gen)
        {
            const string ErrorMessage = "ShopMenu_Render_TreatCurseAssign transpiler is invalid!";

            List<CodeInstruction> codeList = new List<CodeInstruction>(code);

            // Assert to check if underlying method hasn't shifted heavily
            OpCode op = OpCodes.Nop;
            Debug.Assert(PatchTools.TryILAt(code, 457, out op) && op == OpCodes.Ldarg_0, ErrorMessage);
            Debug.Assert(PatchTools.TryILAt(code, 451, out op) && op == OpCodes.Ldarg_0, ErrorMessage);
            Debug.Assert(PatchTools.TryILAt(code, 105, out op) && op == OpCodes.Ldc_I4_5, ErrorMessage);
            Debug.Assert(PatchTools.TryILAt(code, 94, out op) && op == OpCodes.Ldc_I4_5, ErrorMessage);
            Debug.Assert(PatchTools.TryILAt(code, 70, out op) && op == OpCodes.Ldc_I4_0, ErrorMessage);

            LocalBuilder start = gen.DeclareLocal(typeof(int));
            LocalBuilder end = gen.DeclareLocal(typeof(int));
            LocalBuilder worker = gen.DeclareLocal(typeof(TCMenuWorker));

            var firstInsert = new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Call, typeof(HelperCallbacks).GetProperty(nameof(HelperCallbacks.TCMenuWorker)).GetGetMethod()),
                new CodeInstruction(OpCodes.Stloc_S, worker.LocalIndex),
                new CodeInstruction(OpCodes.Ldloc_S, worker.LocalIndex),
                new CodeInstruction(OpCodes.Call, typeof(TCMenuWorker).GetMethod(nameof(TCMenuWorker.Update))),
                new CodeInstruction(OpCodes.Ldloc_S, worker.LocalIndex),
                new CodeInstruction(OpCodes.Call, typeof(TCMenuWorker).GetProperty(nameof(TCMenuWorker.TCListStart)).GetGetMethod()),
                new CodeInstruction(OpCodes.Stloc_S, start.LocalIndex),
                new CodeInstruction(OpCodes.Ldloc_S, worker.LocalIndex),
                new CodeInstruction(OpCodes.Call, typeof(TCMenuWorker).GetProperty(nameof(TCMenuWorker.TCListEnd)).GetGetMethod()),
                new CodeInstruction(OpCodes.Stloc_S, end.LocalIndex),
                new CodeInstruction(OpCodes.Ldloc_S, start.LocalIndex),
            };

            var secondInsert = new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldloc_S, end.LocalIndex)
            };

            var thirdInsert = new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldloc_S, worker.LocalIndex),
                new CodeInstruction(OpCodes.Call, typeof(HelperCallbacks).GetProperty(nameof(HelperCallbacks.SpriteBatch)).GetGetMethod()),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call, typeof(TCMenuWorker).GetMethod(nameof(TCMenuWorker.DrawScroller))),
            };

            var offsetInsert = new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldloc_S, start.LocalIndex),
                new CodeInstruction(OpCodes.Sub)
            };

            // These should be done from highest offset to lowest
            // to avoid accounting for previous inserts / removes

            // Inserts the DrawScroller method after the for
            code = PatchTools.InsertAt(code, thirdInsert, 457);

            // Replaces the for's condition with i < end
            code = PatchTools.ReplaceAt(code, 5, secondInsert, 451);

            // Inserts an offset for vector.X, vector.Y expressions
            code = PatchTools.InsertAt(code, offsetInsert, 105);
            code = PatchTools.InsertAt(code, offsetInsert, 94);

            // Initializes local fields, calls some TCMenuWorker methods, replaces the for's init with i = start
            code = PatchTools.ReplaceAt(code, 1, firstInsert, 70);

            return code;
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Game1), "FinalDraw")]
        internal static void OnFinalDraw()
        {
            foreach (BaseScript mod in Globals.API.Registry.LoadedMods)
                mod.OnDrawBegin();
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Game1), "_Player_TakeDamage")]
        internal static void OnPlayerTakeDamage(PlayerView xView, ref int iInDamage, ref byte byType)
        {
            foreach (BaseScript mod in Globals.API.Registry.LoadedMods)
                mod.OnPlayerDamaged(xView, ref iInDamage, ref byType);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Game1), "_Player_KillPlayer", new Type[] { typeof(PlayerView), typeof(bool), typeof(bool) })]
        internal static void OnPlayerKilled(PlayerView xView)
        {
            foreach (BaseScript mod in Globals.API.Registry.LoadedMods)
                mod.OnPlayerKilled(xView);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Game1), "_Player_ApplyLvUpBonus")]
        internal static void PostPlayerLevelUp(PlayerView xView)
        {
            foreach (BaseScript mod in Globals.API.Registry.LoadedMods)
                mod.PostPlayerLevelUp(xView);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Game1), "_Enemy_TakeDamage")]
        internal static void OnEnemyTakeDamage(Enemy xEnemy, ref int iDamage, ref byte byType)
        {
            foreach (BaseScript mod in Globals.API.Registry.LoadedMods)
                mod.OnEnemyDamaged(xEnemy, ref iDamage, ref byType);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Game1), "_NPC_TakeDamage")]
        internal static void OnNPCTakeDamage(NPC xEnemy, ref int iDamage, ref byte byType)
        {
            foreach (BaseScript mod in Globals.API.Registry.LoadedMods)
                mod.OnNPCDamaged(xEnemy, ref iDamage, ref byType);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Game1), "_NPC_Interact")]
        internal static void OnNPCInteraction(PlayerView xView, NPC xNPC)
        {
            foreach (BaseScript mod in Globals.API.Registry.LoadedMods)
                mod.OnNPCInteraction(xNPC);
        }

        [HarmonyPrefix]
        [HarmonyPatch("_LevelLoading_DoStuff_Arcadia")]
        internal static void OnArcadiaLoad()
        {
            foreach (BaseScript mod in Globals.API.Registry.LoadedMods)
                mod.OnArcadiaLoad();

            // Just in case it didn't get set before; submitting modded runs is not a good idea
            Globals.Game.xGameSessionData.xRogueLikeSession.bTemporaryHighScoreBlock = true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("_Item_Use", new Type[] { typeof(ItemCodex.ItemTypes), typeof(PlayerView), typeof(bool) })]
        internal static void OnItemUse(ItemCodex.ItemTypes enItem, PlayerView xView, ref bool bSend)
        {
            if (xView.xViewStats.bIsDead)
                return;
            foreach (BaseScript mod in Globals.API.Registry.LoadedMods)
                mod.OnItemUse(enItem, xView, ref bSend);
        }

        [HarmonyPostfix]
        [HarmonyPatch("_LevelLoading_DoStuff_ArcadeModeRoom")]
        internal static void PostArcadeRoomStart()
        {
            foreach (BaseScript mod in Globals.API.Registry.LoadedMods)
                mod.PostArcadeRoomStart(Globals.Game.xGameSessionData.xRogueLikeSession);
        }

        [HarmonyPostfix]
        [HarmonyPatch("_Skill_ActivateSkill")]
        internal static void PostSpellActivation(PlayerView xView, ISpellActivation xact, SpellCodex.SpellTypes enType, int iBoostState)
        {
            foreach (BaseScript mod in Globals.API.Registry.LoadedMods)
                mod.PostSpellActivation(xView, xact, enType, iBoostState);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Game1), "_Enemy_AdjustForDifficulty")]
        internal static void OnEnemyAdjustForDifficulty(Enemy xEn)
        {
            if (!xEn.enType.IsFromMod())
                return;

            Globals.API.Registry.Library.Enemies[xEn.enType].Config.DifficultyScaler?.Invoke(xEn);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(Game1), "_Enemy_MakeElite")]
        internal static CodeList EnemyMakeEliteTranspiler(CodeList code, ILGenerator gen)
        {
            // * vanilla switch case *
            // if (!bRet) {
            // // check if mod enemy, run elite scaler
            // // assign bRet = true if mod enemy has elite
            // }
            // * elite bonuses *

            // However, we need to move ALL of the labels that point to the elite bonuses' "if (bRet)" line to our "if (!bRet)" line

            Label skipBranch = gen.DefineLabel();

            List<CodeInstruction> codeList = code.ToList();

            // Assert to check if underlying method hasn't shifted heavily
            OpCode op = OpCodes.Nop;
            Debug.Assert(PatchTools.TryILAt(codeList, 11511, out op) && op == OpCodes.Ldloc_1, "Enemy_MakeElite transpiler is invalid!");

            var insert = new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldloc_1).WithLabels(codeList[11511].labels.ToArray()),
                new CodeInstruction(OpCodes.Brtrue, skipBranch),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, typeof(HelperCallbacks).GetPrivateStaticMethod(nameof(HelperCallbacks.InEnemyMakeElite))),
                new CodeInstruction(OpCodes.Stloc_1), // Store elite status in bRet
                new CodeInstruction(OpCodes.Nop).WithLabels(skipBranch)
            };

            codeList[11511].labels.Clear(); // Shifts labels to account for insertion

            return PatchTools.InsertAt(code, insert, 11511);
        }

        /// <summary>
        /// Overrides OutputError to always output an error log in "Logs" folder,
        /// instead of launching the Grindea Beta Error Report tool.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Game1), "OutputError", typeof(string), typeof(string))]
        internal static bool OnOutputError(string p_sLocation, string e)
        {
            if (CAS.IsDebugFlagSet_Release("silentsend"))
            {
                // Ignore silent sends for now
                return false;
            }

            if (e.Contains("OutOfMemoryException") && e.Contains("VertexBuffer"))
            {
                Globals.Game.xOptions.bLoneBats = true;
                Globals.Game.xOptions.SaveText();
            }

            e = e.Replace("C:\\Dropbox\\Eget jox\\!DugTrio\\Legend Of Grindia\\Legend Of Grindia\\Legend Of Grindia", "(path)");
            e = e.Replace("F:\\Stable Branch\\Legend Of Grindia\\Legend Of Grindia", "(path)");

            StringBuilder msg = new StringBuilder(2048);

            msg.Append("An error happened while running a modded game instance!").AppendLine();
            msg.Append("=== Exception message ===").AppendLine();
            msg.Append(e).AppendLine();
            msg.Append("=== Game Settings ===").AppendLine();
            msg.Append("Game Version = " + Globals.Game.sVersionNumberOnly).AppendLine();
            msg.Append("Fullscreen = " + Globals.Game.xOptions.enFullScreen).AppendLine();
            msg.Append("Network role = " + Globals.Game.xNetworkInfo.enCurrentRole).AppendLine();
            msg.Append("Extra Error Info => " + DebugKing.dssExtraErrorInfo.Count + " pairs").AppendLine();

            foreach (KeyValuePair<string, string> kvp in DebugKing.dssExtraErrorInfo)
            {
                msg.Append("  " + kvp.Key + " = " + kvp.Value).AppendLine();
            }

            msg.Append("=== GrindScript Info ===").AppendLine();
            msg.Append("Mod List => " + Globals.API.Registry.LoadedMods.Count + " mods").AppendLine();

            foreach (BaseScript mod in Globals.API.Registry.LoadedMods)
            {
                msg.Append("  " + mod.ToString()).AppendLine();
            }

            var time = DateTime.Now;

            string logName = $"CrashLog_{time.Year}.{time.Month}.{time.Day}_{time.Hour}.{time.Minute}.{time.Second}.txt";

            StreamWriter writer = null;
            try
            {
                writer = new StreamWriter(new FileStream(Path.Combine("Logs", logName), FileMode.Create, FileAccess.Write));
                writer.Write(msg.ToString());
            }
            catch { }
            finally
            {
                writer?.Close();
            }

            return false;
        }
    }
}

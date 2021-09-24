﻿using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;

namespace SoG.Modding.Utils
{
    /// <summary>
    /// Provides various helper methods.
    /// </summary>
    public static class ModUtils
    {
        /// <summary>
        /// Tries to create a directory, ignoring any exceptions thrown.
        /// </summary>
        public static bool TryCreateDirectory(string name)
        {
            try
            {
                Directory.CreateDirectory(name);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tries to load a texture from the given path and ContentManager.
        /// Returns true if the operation succeeded, false otherwise.
        /// If the operation failed, result is set to RenderMaster.txNullTexture.
        /// </summary>
        public static bool TryLoadTex(string path, ContentManager manager, out Texture2D result)
        {
            try
            {
                result = manager.Load<Texture2D>(path);
                return true;
            }
            catch
            {
                result = RenderMaster.txNullTex;
                return false;
            }
        }

        /// <summary>
        /// Tries to load a WaveBank using the provided path and AudioEngine.
        /// Returns true if the operation succeeded, false otherwise.
        /// If the operation failed, result is set to null.
        /// </summary>
        public static bool TryLoadWaveBank(string assetPath, AudioEngine engine, out WaveBank result)
        {
            try
            {
                result =  new WaveBank(engine, assetPath);
                return true;
            }
            catch
            {
                result = null;
                return false;
            }
        }

        /// <summary>
        /// Tries to load a SoundBank using the provided path and AudioEngine, and returns it if successful.
        /// If an exception is thrown during load, null is returned, and a warning message is logged.
        /// </summary>
        public static bool TryLoadSoundBank(string assetPath, AudioEngine engine, out SoundBank result)
        {
            try
            {
                result = new SoundBank(engine, assetPath);
                return true;
            }
            catch (Exception e)
            {
                Globals.Logger.Warn(ShortenModPaths(e.Message), source: nameof(TryLoadSoundBank));

                result = null;
                return false;
            }
        }

        /// <summary>
        /// For audio IDs generated by GrindScript, extracts relevant information.
        /// Returns true on success, false otherwise.
        /// </summary>
        internal static bool SplitAudioID(string ID, out int entryID, out bool isMusic, out int cueID)
        {
            entryID = -1;
            isMusic = false;
            cueID = -1;

            if (!ID.StartsWith("GS_"))
                return false;

            string[] words = ID.Remove(0, 3).Split('_');

            if (words.Length != 2 || !(words[1][0] == 'M' || words[1][0] == 'S'))
                return false;

            try
            {
                entryID = int.Parse(words[0]);
                isMusic = words[1][0] == 'M';
                cueID = int.Parse(words[1].Substring(1));

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Splits a message in words, removing any empty results.
        /// </summary>
        public static string[] GetArgs(string message)
        {
            return message == null ? new string[0] : message.Split(new char[] { ' ' }, options: StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Finds and replaces commonly occuring game paths with shortened forms.
        /// </summary>
        public static string ShortenModPaths(string path)
        {
            return path
                .Replace('/', '\\')
                .Replace(Directory.GetCurrentDirectory() + @"\Content\ModContent", "(ModContent)")
                .Replace(Directory.GetCurrentDirectory() + @"\Content\Mods", "(Mods)")
                .Replace(Directory.GetCurrentDirectory() + @"\Content", "(Content)")
                .Replace(Directory.GetCurrentDirectory(), "(SoG)");
        }
    }
}
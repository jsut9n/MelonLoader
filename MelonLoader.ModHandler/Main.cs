﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using MelonLoader.ICSharpCode.SharpZipLib.Zip;
#pragma warning disable 0618

namespace MelonLoader
{
    public static class Main
    {
        public static List<MelonMod> Mods = new List<MelonMod>();
        internal static MelonModGameAttribute CurrentGameAttribute = null;
        public static bool IsVRChat = false;
        public static bool IsBoneworks = false;
        public static string UnityVersion = null;
        private static Assembly Assembly_CSharp = null;
        private static bool HasGeneratedAssembly = false;

        private static void Initialize()
        {
            if (string.IsNullOrEmpty(AppDomain.CurrentDomain.BaseDirectory))
                ((AppDomainSetup)typeof(AppDomain).GetProperty("SetupInformationNoCopy", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(AppDomain.CurrentDomain, new object[0])).ApplicationBase = Imports.GetGameDirectory();
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            AppDomain.CurrentDomain.UnhandledException += ExceptionHandler;

            CurrentGameAttribute = new MelonModGameAttribute(Imports.GetCompanyName(), Imports.GetProductName());
            UnityVersion = GetUnityFileVersion();

            if (Imports.IsIl2CppGame())
            {
                IsVRChat = CurrentGameAttribute.IsGame("VRChat", "VRChat");
                IsBoneworks = CurrentGameAttribute.IsGame("Stress Level Zero", "BONEWORKS");
            }

            if (!Imports.IsDebugMode() && Imports.IsConsoleEnabled())
            {
                Console.Enabled = true;
                Console.Create();
            }

            if (!Imports.IsIl2CppGame() || AssemblyGenerator.Main.Initialize())
                HasGeneratedAssembly = true;
            else
                Imports.UNLOAD_MELONLOADER();
        }

        private static void OnApplicationStart()
        {
            if (!HasGeneratedAssembly)
                return;

            if (Imports.IsIl2CppGame())
            {
                if (IsVRChat)
                    Assembly_CSharp = Assembly.Load("Assembly-CSharp");
                UnhollowerSupport.Initialize();
            }
            SupportModule.Initialize();

            MelonModLogger.Log("------------------------------");
            MelonModLogger.Log("Unity " + UnityVersion);
            MelonModLogger.Log("------------------------------");
            MelonModLogger.Log("Name: " + CurrentGameAttribute.GameName);
            MelonModLogger.Log("Developer: " + CurrentGameAttribute.Developer);
            MelonModLogger.Log("Type: " + (Imports.IsIl2CppGame() ? "Il2Cpp" : (Imports.IsOldMono() ? "Mono" : "MonoBleedingEdge")));
            MelonModLogger.Log("------------------------------");
            MelonModLogger.Log("Using v" + BuildInfo.Version + " Open-Beta");
            MelonModLogger.Log("------------------------------");

            LoadMods();
            if (Mods.Count > 0)
            {
                AddUnityDebugLog();

                for (int i = 0; i < Mods.Count; i++)
                {
                    MelonMod mod = Mods[i];
                    if (mod != null)
                    {
                        MelonModLogger.Log(mod.InfoAttribute.Name
                            + (!string.IsNullOrEmpty(mod.InfoAttribute.Version)
                            ? (" v" + mod.InfoAttribute.Version) : "")
                            + (!string.IsNullOrEmpty(mod.InfoAttribute.Author)
                            ? (" by " + mod.InfoAttribute.Author) : "")
                            + (!string.IsNullOrEmpty(mod.InfoAttribute.DownloadLink)
                            ? (" (" + mod.InfoAttribute.DownloadLink + ")")
                            : "")
                            );
                        if (Imports.IsDebugMode())
                            MelonModLogger.Log("Plugin: " + mod.IsPlugin.ToString());
                        MelonModLogger.LogModStatus((mod.GameAttributes.Any()) ? (mod.IsUniversal ? 0 : 1) : 2);
                        MelonModLogger.Log("------------------------------");
                    }
                }

                for (int i = 0; i < Mods.Count; i++)
                {
                    MelonMod mod = Mods[i];
                    if (mod != null)
                        try { mod.OnApplicationStart(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                }
            }
            else
            {
                MelonModLogger.Log("No Mods Loaded!");
                MelonModLogger.Log("------------------------------");
            }
        }

        public static void OnApplicationQuit()
        {
            if (Mods.Count() > 0)
            {
                for (int i = 0; i < Mods.Count; i++)
                {
                    MelonMod mod = Mods[i];
                    if (mod != null)
                        try { mod.OnApplicationQuit(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                }
                ModPrefs.SaveConfig();
            }
            Harmony.HarmonyInstance.UnpatchAllInstances();
            Imports.UNLOAD_MELONLOADER();
            if (Imports.IsQuitFix()) Process.GetCurrentProcess().Kill();
        }

        public static void OnModSettingsApplied()
        {
            if (Mods.Count() > 0)
                for (int i = 0; i < Mods.Count; i++)
                {
                    MelonMod mod = Mods[i];
                    if (mod != null)
                        try { mod.OnModSettingsApplied(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                }
        }

        public static void OnUpdate()
        {
            SceneHandler.CheckForSceneChange();
            if (Imports.IsIl2CppGame() && IsVRChat)
                VRChat_CheckUiManager();
            if (Mods.Count() > 0)
                for (int i = 0; i < Mods.Count; i++)
                {
                    MelonMod mod = Mods[i];
                    if (mod != null)
                        try { mod.OnUpdate(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                }
        }

        public static void OnFixedUpdate()
        {
            if (Mods.Count() > 0)
                for (int i = 0; i < Mods.Count; i++)
                {
                    MelonMod mod = Mods[i];
                    if (mod != null)
                        try { mod.OnFixedUpdate(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                }
        }

        public static void OnLateUpdate()
        {
            if (Mods.Count() > 0)
                for (int i = 0; i < Mods.Count; i++)
                {
                    MelonMod mod = Mods[i];
                    if (mod != null)
                        try { mod.OnLateUpdate(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                }
        }

        public static void OnGUI()
        {
            if (Mods.Count() > 0)
            {
                for (int i = 0; i < Mods.Count; i++)
                {
                    MelonMod mod = Mods[i];
                    if (mod != null)
                        try { mod.OnGUI(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                }
            }
        }

        internal static void OnLevelIsLoading()
        {
            if (Mods.Count() > 0)
                for (int i = 0; i < Mods.Count; i++)
                {
                    MelonMod mod = Mods[i];
                    if (mod != null)
                        try { mod.OnLevelIsLoading(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                }
        }

        internal static void OnLevelWasLoaded(int level)
        {
            if (Mods.Count() > 0)
                for (int i = 0; i < Mods.Count; i++)
                {
                    MelonMod mod = Mods[i];
                    if (mod != null)
                        try { mod.OnLevelWasLoaded(level); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                }
        }

        internal static void OnLevelWasInitialized(int level)
        {
            if (Mods.Count() > 0)
                for (int i = 0; i < Mods.Count; i++)
                {
                    MelonMod mod = Mods[i];
                    if (mod != null)
                        try { mod.OnLevelWasInitialized(level); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                }
        }

        private static bool ShouldCheckForUiManager = true;
        private static Type VRCUiManager = null;
        private static PropertyInfo VRCUiManager_Instance = null;
        private static void VRChat_CheckUiManager()
        {
            if (ShouldCheckForUiManager)
            {
                if (VRCUiManager == null)
                    VRCUiManager = Assembly_CSharp.GetType("VRCUiManager");
                if (VRCUiManager != null)
                {
                    if (VRCUiManager_Instance == null)
                        VRCUiManager_Instance = VRCUiManager.GetProperty("field_Protected_Static_VRCUiManager_0");
                    if (VRCUiManager_Instance != null)
                    {
                        object returnval = VRCUiManager_Instance.GetValue(null, new object[0]);
                        if (returnval != null)
                        {
                            ShouldCheckForUiManager = false;
                            if (Mods.Count() > 0)
                                for (int i = 0; i < Mods.Count; i++)
                                {
                                    MelonMod mod = Mods[i];
                                    if (mod != null)
                                        try { mod.VRChat_OnUiManagerInit(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                                }
                        }
                    }
                }
            }
        }

        private static void LoadMods(bool plugins = false)
        {
            string searchdir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, (plugins ? "Plugins" : "Mods"));
            if (!Directory.Exists(searchdir))
                Directory.CreateDirectory(searchdir);
            else
            {
                // DLL
                string[] files = Directory.GetFiles(searchdir, "*.dll");
                if (files.Length > 0)
                {
                    for (int i = 0; i < files.Count(); i++)
                    {
                        string file = files[i];
                        if (!string.IsNullOrEmpty(file))
                        {
                            if (plugins)
                            {
                                if ((Imports.IsDevPluginsOnly() && !file.EndsWith("-dev.dll")) || (!Imports.IsDevPluginsOnly() && file.EndsWith("-dev.dll")))
                                    continue;
                            }
                            else
                            {
                                if ((Imports.IsDevModsOnly() && !file.EndsWith("-dev.dll")) || (!Imports.IsDevModsOnly() && file.EndsWith("-dev.dll")))
                                    continue;
                            }
                            try
                            {
                                LoadAssembly(File.ReadAllBytes(file), plugins);
                            }
                            catch (Exception e)
                            {
                                MelonModLogger.LogError("Unable to load " + file + ":\n" + e.ToString());
                                MelonModLogger.Log("------------------------------");
                            }
                        }
                    }
                }

                // ZIP
                string[] zippedFiles = Directory.GetFiles(searchdir, "*.zip");
                if (zippedFiles.Length > 0)
                {
                    for (int i = 0; i < zippedFiles.Count(); i++)
                    {
                        string file = zippedFiles[i];
                        if (!string.IsNullOrEmpty(file))
                        {
                            try
                            {
                                using (var fileStream = File.OpenRead(file))
                                {
                                    using (var zipInputStream = new ZipInputStream(fileStream))
                                    {
                                        ZipEntry entry;
                                        while ((entry = zipInputStream.GetNextEntry()) != null)
                                        {
                                            string filename = Path.GetFileName(entry.Name);
                                            if (string.IsNullOrEmpty(filename) || !filename.EndsWith(".dll"))
                                                continue;

                                            if (plugins)
                                            {
                                                if ((Imports.IsDevPluginsOnly() && !filename.EndsWith("-dev.dll")) || (!Imports.IsDevPluginsOnly() && filename.EndsWith("-dev.dll")))
                                                    continue;
                                            }
                                            else
                                            {
                                                if ((Imports.IsDevModsOnly() && !filename.EndsWith("-dev.dll")) || (!Imports.IsDevModsOnly() && filename.EndsWith("-dev.dll")))
                                                    continue;
                                            }

                                            using (var unzippedFileStream = new MemoryStream())
                                            {
                                                int size = 0;
                                                byte[] buffer = new byte[4096];
                                                while (true)
                                                {
                                                    size = zipInputStream.Read(buffer, 0, buffer.Length);
                                                    if (size > 0)
                                                        unzippedFileStream.Write(buffer, 0, size);
                                                    else
                                                        break;
                                                }
                                                LoadAssembly(unzippedFileStream.ToArray(), plugins);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                MelonModLogger.LogError("Unable to load " + file + ":\n" + e.ToString());
                                MelonModLogger.Log("------------------------------");
                            }
                        }
                    }
                }
            }
        }

        private static void LoadModFromAssembly(Assembly assembly, bool isPlugin = false)
        {
            MelonModInfoAttribute modInfoAttribute = assembly.GetCustomAttributes(false).FirstOrDefault(x => (x.GetType() == typeof(MelonModInfoAttribute))) as MelonModInfoAttribute;
            if ((modInfoAttribute != null) && (modInfoAttribute.ModType != null) && modInfoAttribute.ModType.IsSubclassOf(typeof(MelonMod)))
            {
                bool should_continue = false;
                bool isUniversal = false;
                MelonModGameAttribute[] modGameAttributes = assembly.GetCustomAttributes(typeof(MelonModGameAttribute), true) as MelonModGameAttribute[];
                int modGameAttributes_Count = modGameAttributes.Length;
                if (modGameAttributes_Count > 0)
                {
                    for (int i = 0; i < modGameAttributes_Count; i++)
                    {
                        MelonModGameAttribute modGameAttribute = modGameAttributes[i];
                        if (CurrentGameAttribute.IsCompatible(modGameAttribute))
                        {
                            isUniversal = CurrentGameAttribute.IsCompatibleBecauseUniversal(modGameAttribute);
                            should_continue = true;
                            break;
                        }
                    }
                }
                else
                {
                    isUniversal = true;
                    should_continue = true;
                }
                if (should_continue)
                {
                    try
                    {
                        MelonMod modInstance = Activator.CreateInstance(modInfoAttribute.ModType) as MelonMod;
                        if (modInstance != null)
                        {
                            modInstance.IsUniversal = isUniversal;
                            modInstance.IsPlugin = isPlugin;
                            modInstance.InfoAttribute = modInfoAttribute;
                            if (modGameAttributes_Count > 0)
                                modInstance.GameAttributes = modGameAttributes;
                            else
                                modInstance.GameAttributes = null;
                            modInstance.ModAssembly = assembly;
                            Harmony.HarmonyInstance.Create(assembly.FullName).PatchAll(assembly);
                            Mods.Add(modInstance);
                        }
                        else
                            MelonModLogger.LogError("Unable to load Mod in " + assembly.GetName() + "! Failed to Create Instance!");
                    }
                    catch (Exception e) { MelonModLogger.LogError("Unable to load Mod in " + assembly.GetName() + "! " + e.ToString()); }
                }
                else
                    MelonModLogger.LogModStatus(3);
            }
        }

        private static void LoadAssembly(string file, bool isPlugin = false) => LoadModFromAssembly(Assembly.LoadFrom(file), isPlugin);
        private static void LoadAssembly(byte[] data, bool isPlugin = false) => LoadModFromAssembly(Assembly.Load(data), isPlugin);
        private static void LoadAssembly(Assembly asm, bool isPlugin = false)
        {
            if (!asm.Equals(null))
                LoadModFromAssembly(asm, isPlugin);
            else
                MelonModLogger.LogError("Unable to load " + asm);
        }

        private static void ExceptionHandler(object sender, UnhandledExceptionEventArgs e) => MelonModLogger.LogError((e.ExceptionObject as Exception).ToString());
        private static string GetUnityFileVersion()
        {
            string file_version = FileVersionInfo.GetVersionInfo(Imports.GetExePath()).FileVersion;
            return file_version.Substring(0, file_version.LastIndexOf('.'));
        }

        private static void AddUnityDebugLog()
        {
            SupportModule.UnityDebugLog("--------------------------------------------------------------------------------------------------");
            SupportModule.UnityDebugLog("~   This Game has been MODIFIED using MelonLoader. DO NOT report any issues to the Developers!   ~");
            SupportModule.UnityDebugLog("--------------------------------------------------------------------------------------------------");
        }
    }
}
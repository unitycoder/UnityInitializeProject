using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityLauncherProTools
{
    public class InitializeProject
    {
        // settings
        static string[] folders = new string[] { "Fonts", "Materials", "Models", "Prefabs", "Scenes", "Scripts", "Shaders", "Sounds", "Textures" };

        static string assetsFolder;
        static bool deleteFile = true;

        [MenuItem("Tools/Initialize Project")]
        public static void InitManually()
        {
            // called manually from menu, so dont delete file when testing
            deleteFile = false;
            Init();
        }

        // this method is called from launcher, without parameters, so delete is called
        public static void Init()
        {
            assetsFolder = Application.dataPath;

            // TODO show window to select options for project init

            CreateFolders();
            // TODO adjust project settings, linear, company name
            // TODO remove extra packages
            // TODO setup light settings
            // TODO adjust mainscene: camera pos, skybox off?
            // TODO save mainscene
            // TODO add mainscene to build scenes list
            // TODO adjust quality settings (but only in mobile?)

            // self destruct this editor script file
            if (deleteFile == true)
            {
                var scriptPath = Path.Combine(assetsFolder, "Scripts/Editor/InitializeProject.cs");
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
                if (File.Exists(scriptPath + ".meta")) File.Delete(scriptPath + ".meta");
            }

            // refresh folder
            AssetDatabase.Refresh();
        }

        static void CreateFolders()
        {
            // create each folder if it doesnt exists
            foreach (string folder in folders)
            {
                if (!Directory.Exists(assetsFolder + "/" + folder))
                {
                    Directory.CreateDirectory(assetsFolder + "/" + folder);
                }
            }
        }
    }
}

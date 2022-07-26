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

        [MenuItem("Tools/Initialize Project")]
        public static void Init()
        {
            assetsFolder = Application.dataPath;

            CreateFolders();
            // TODO adjust project settings, linear, company name
            // TODO remove extra packages
            // TODO setup light settings
            // TODO adjust mainscene: camera pos, skybox off?
            // TODO save mainscene
            // TODO add mainscene to build scenes list
            // TODO adjust quality settings (but only in mobile?)

            // TODO self destruct this editor script file?

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

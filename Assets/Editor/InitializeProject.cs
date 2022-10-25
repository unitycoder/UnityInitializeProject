using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityLauncherProTools
{
    public class InitializeProject : EditorWindow
    {
        // settings
        static string[] folders = new string[] { "Fonts", "Materials", "Models", "Prefabs", "Scenes", "Scripts", "Shaders", "Sounds", "Textures" };

        static InitializeProject window;
        static string assetsFolder;
        static bool deleteFile = true;

        // settings
        static bool createFolders = true;

        [MenuItem("Tools/UnityLibrary/Initialize Project")]
        public static void InitManually()
        {
            // called manually from menu, so dont delete file when testing
            deleteFile = false;
            Init();
        }

        // this method is called from launcher, without parameters, so delete is called
        public static void Init()
        {
            window = (InitializeProject)EditorWindow.GetWindow(typeof(InitializeProject));
            window.Show();
        }

        void OnGUI()
        {
            GUILayout.Label("Project Setup", EditorStyles.boldLabel);
            GUILayout.Space(10);

            Checkbox("Create Folders", ref createFolders);

            GUILayout.Space(10);
            if (GUILayout.Button("Setup Project", GUILayout.Height(64))) SetupProject();

            // enter to confirm
            if (Event.current.keyCode == KeyCode.Return) SetupProject();
        }

        static void SetupProject()
        {
            assetsFolder = Application.dataPath;

            if (createFolders) CreateFolders();

            // TODO set these somewhere
            PlayerSettings.companyName = "Company";
            PlayerSettings.productName = "Project";

            PlayerSettings.colorSpace = ColorSpace.Linear;

            // save scene
            var scenePath = "Assets/Scenes/Main.unity";
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);

            // add scene to build settings
            List<EditorBuildSettingsScene> editorBuildSettingsScenes = new List<EditorBuildSettingsScene>();
            if (!string.IsNullOrEmpty(scenePath)) editorBuildSettingsScenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = editorBuildSettingsScenes.ToArray();

            // TODO 2d/3d mode for editor
            // TODO remove extra packages
            // TODO setup light settings
            // TODO adjust mainscene: camera pos, skybox off?
            Camera.main.transform.position = Vector3.zero;

            // disable editor camera easing and acceleration
            SceneView.lastActiveSceneView.cameraSettings.easingEnabled = false;
            SceneView.lastActiveSceneView.cameraSettings.accelerationEnabled = false;

            // GizmoUtility in 2022.1
            //GizmoUtility.SetGizmoEnabled(GizmoType.Move, true);

            // set sceneview gizmos size https://github.com/unity3d-kr/GizmoHotkeys/blob/05516ebfc3ce1655cbefb150d328e2b66e03646d/Editor/SelectionGizmo.cs
            Assembly asm = Assembly.GetAssembly(typeof(Editor));
            Type type = asm.GetType("UnityEditor.AnnotationUtility");
            if (type != null)
            {
                PropertyInfo iconSizeProperty = type.GetProperty("iconSize", BindingFlags.Static | BindingFlags.NonPublic);
                if (iconSizeProperty != null)
                {
                    //float nowIconSize = (float)iconSizeProperty.GetValue(asm, null);
                    iconSizeProperty.SetValue(asm, 0.01f, null);
                }
            }

            // disable unity splash
            PlayerSettings.SplashScreen.show = false;

            // TODO adjust quality settings (but only in mobile? add toggle: webgl/mobile/windows)

            window.Close();

            // self destruct this editor script file
            if (deleteFile == true)
            {
                // FIXME file is deleted, if reimport some script, while editorwindow is open
                var scriptPath = Path.Combine(assetsFolder, "Editor/InitializeProject.cs");
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
                if (File.Exists(scriptPath + ".meta")) File.Delete(scriptPath + ".meta");
            }
            else
            {
                Debug.Log("File not deleted when called init manually");
            }

            // refresh folder
            AssetDatabase.Refresh();
        }



        // toggle with clickable label text
        static void Checkbox(string label, ref bool value)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(label, EditorStyles.label)) value = !value;
            value = EditorGUILayout.Toggle("", value);
            EditorGUILayout.EndHorizontal();
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

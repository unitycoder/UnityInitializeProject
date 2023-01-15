using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityLauncherProTools
{
    public class InitializeProject : EditorWindow
    {
        // settings
        static string[] folders = new string[] { "Fonts", "Materials", "Models", "Plugins", "Prefabs", "Scenes", "Scripts", "Shaders", "Sounds", "Textures" };

        static Dictionary<string, string> addPackages = new Dictionary<string, string>() { { "com.unity.ide.visualstudio", "2.0.17" } };
        static string[] blackListedPackages = new string[] { "com.unity.modules.unityanalytics", "com.unity.modules.director", "com.unity.collab-proxy", "com.unity.ide.rider", "com.unity.ide.vscode", "com.unity.test-framework", "com.unity.timeline" };

        static InitializeProject window;
        static string assetsFolder;
        static bool deleteFile = true;

        // settings
        static bool createFolders = true;
        static bool updatePackages = true;

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
            Checkbox("updatePackages", ref updatePackages);

            GUILayout.Space(10);
            if (GUILayout.Button("Setup Project", GUILayout.Height(64))) SetupProject();

            // enter to confirm
            if (Event.current.keyCode == KeyCode.Return) SetupProject();
        }

        static void SetupProject()
        {
            assetsFolder = Application.dataPath;

            if (createFolders) CreateFolders();

            // TODO set these somewhere globally?
            PlayerSettings.companyName = "Company";
            PlayerSettings.productName = "Project";

            PlayerSettings.colorSpace = ColorSpace.Linear;

            // save scene
            var scenePath = "Assets/Scenes/Main.unity";
            if (Directory.Exists(scenePath) == false)
            {
                Directory.CreateDirectory("Assets/Scenes");
            }
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);

            // add scene to build settings
            List<EditorBuildSettingsScene> editorBuildSettingsScenes = new List<EditorBuildSettingsScene>();
            if (!string.IsNullOrEmpty(scenePath)) editorBuildSettingsScenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = editorBuildSettingsScenes.ToArray();

            // TODO 2d/3d mode for editor?

            UpdatePackages();

            // skybox off from lighting settings
            RenderSettings.skybox = null;

            // skybox off from camera
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            // TODO set background color?

            // reset camera pos
            Camera.main.transform.position = Vector3.zero;

            // disable editor camera easing and acceleration
#if UNITY_2019_1_OR_NEWER
            SceneView.lastActiveSceneView.cameraSettings.easingEnabled = false;
            SceneView.lastActiveSceneView.cameraSettings.accelerationEnabled = false;
#endif

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
                // FIXME in editor: file is deleted, if re-import some script, while editorwindow is open (resets deletefile var?)
                var scriptPath = Path.Combine(assetsFolder, "Editor/InitializeProject.cs");
                Debug.Log("Deleting init script: " + scriptPath);
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


        static void UpdatePackages()
        {
            // check if packages.json exists
            var packagesPath = Path.Combine(assetsFolder, "../Packages/manifest.json");
            if (!File.Exists(packagesPath)) return;

            var json = File.ReadAllText(packagesPath);

            // NOTE this seems to work in 2020.3 and later?
            var jsonConvertType = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json");
            if (jsonConvertType != null)
            {
                jsonConvertType = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
            }
            IJsonSerializer jsonSerializer;
            if (jsonConvertType != null)
            {
                jsonSerializer = new NewtonsoftJsonSerializer(jsonConvertType);
            }
            else
            {
                jsonSerializer = new DefaultJsonSerializer();
            }

            // do we have real newtonsoft
            Type type = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json");
            if (type != null)
            {
                //Debug.Log("We have Newtonsoft.Json");
                var fromJson = jsonSerializer.Deserialize<DependenciesManifest>(json);

                for (int i = fromJson.dependencies.Count; i > -1; i--)
                {
                    for (int k = 0; k < blackListedPackages.Length; k++)
                    {
                        if (fromJson.dependencies.ContainsKey(blackListedPackages[k]))
                        {
                            fromJson.dependencies.Remove(blackListedPackages[k]);
                            //Debug.Log("Removed " + blackListedPackages[k]);
                        }
                    }
                }

                // add wanted packages, if missing
                foreach (KeyValuePair<string, string> item in addPackages)
                {
                    // TODO check if want to increase version number?
                    if (fromJson.dependencies.ContainsKey(item.Key) == false)
                    {
                        fromJson.dependencies.Add(item.Key, item.Value);
                        Debug.Log("Added " + item.Key);
                    }
                    else
                    {
                        //Debug.Log("Already contains " + item.Key);
                    }
                }

                // TODO add pretty print
                var toJson = jsonSerializer.Serialize(fromJson);
                // FIXME temporary pretty print, by adding new lines and tabs
                toJson = toJson.Replace(",", ",\n");
                toJson = toJson.Replace("{", "{\n");
                toJson = toJson.Replace("}", "\n}");
                toJson = toJson.Replace("\"dependencies", "\t\"dependencies");
                toJson = toJson.Replace("\"com.", "\t\t\"com.");
                //Debug.Log(toJson);
                File.WriteAllText(packagesPath, toJson);
            }
            else
            {
                Debug.Log("Newtonsoft.Json is not available, cannot remove packages..");
            }
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
                if (!Directory.Exists(assetsFolder + " / " + folder))
                {
                    Directory.CreateDirectory(assetsFolder + "/" + folder);
                }
            }
        }
    }

    // manifest.json
    public class DependenciesManifest
    {
        public Dictionary<string, string> dependencies { get; set; }
    }

    public interface IJsonSerializer
    {
        string Serialize(object obj);
        T Deserialize<T>(string json);
    }

    public class DefaultJsonSerializer : IJsonSerializer
    {
        public string Serialize(object obj)
        {
            return "default serializer";
        }

        public T Deserialize<T>(string json)
        {
            return default(T);
        }
    }

    public class NewtonsoftJsonSerializer : IJsonSerializer
    {
        private readonly Type jsonConvertType;

        public NewtonsoftJsonSerializer(Type jsonConvertType)
        {
            this.jsonConvertType = jsonConvertType;
        }

        public string Serialize(object obj)
        {
            var serializeMethod = jsonConvertType.GetMethod("SerializeObject", new Type[] { typeof(object) });
            return (string)serializeMethod.Invoke(null, new object[] { obj });
        }

        public T Deserialize<T>(string json)
        {
            var deserializeMethod = jsonConvertType.GetMethod("DeserializeObject", new Type[] { typeof(string), typeof(Type) });
            return (T)deserializeMethod.Invoke(null, new object[] { json, typeof(T) });
        }
    }
}

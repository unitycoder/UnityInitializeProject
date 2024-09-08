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
        static readonly string id = "InitializeProject_";

        // settings
        static string[] folders = new string[] { "Fonts", "Materials", "Models", "Prefabs", "Scenes", "Scripts", "Shaders", "Sounds", "Textures" };

        static Dictionary<string, string> addPackages = new Dictionary<string, string>() { { "com.unity.ide.visualstudio", "2.0.22" } };
        static string[] blackListedPackages = new string[] { "com.unity.modules.unityanalytics", "com.unity.modules.director", "com.unity.collab-proxy", "com.unity.ide.rider", "com.unity.ide.vscode", "com.unity.test-framework", "com.unity.timeline", "com.unity.visualscripting" };

        static InitializeProject window;
        static string assetsFolder;
        static bool deleteFile = true;

        static bool createFolders = true;
        static bool updatePackages = true;

        static bool importAssets = true;
        static List<string> items;
        static List<bool> checkedStates;

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
            window.titleContent = new GUIContent("Initialize Project");
            window.minSize = new Vector2(450, 550);
            LoadSettings();
            window.Show();
        }

        void OnGUI()
        {
            GUILayout.Label("Project Setup", EditorStyles.boldLabel);
            GUILayout.Space(10);

            Checkbox("Create Folders", "Common folders: Scripts, Scenes, Textures..", ref createFolders);
            Checkbox("Update Packages", "Adds and removes packages from manifest.json", ref updatePackages);
            Checkbox("Import Assets", "Imports selected assets from the list below", ref importAssets);

            GUILayout.Space(10);
            if (GUILayout.Button("Setup Project", GUILayout.Height(64)))
            {
                SetupProject();
            }

            DrawAddAssets();
            DrawAssetList();

            // enter to confirm
            if (Event.current.keyCode == KeyCode.Return) SetupProject();
        }

        static void DrawAssetList()
        {
            GUILayout.Space(5);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(2));
            GUILayout.Space(5);

            // Draw list of items
            for (int i = 0; i < items.Count; i++)
            {
                GUILayout.BeginHorizontal();

                bool newState = EditorGUILayout.Toggle(checkedStates[i], GUILayout.Width(20));
                if (newState != checkedStates[i])
                {
                    checkedStates[i] = newState;
                }

                var filename = Path.GetFileName(items[i]);
                GUILayout.Label(filename, GUILayout.Width(350));

                EditorGUI.BeginDisabledGroup(i == 0);
                if (GUILayout.Button("^", GUILayout.Width(20)))
                {
                    var item = items[i];
                    var state = checkedStates[i];
                    items.RemoveAt(i);
                    checkedStates.RemoveAt(i);
                    items.Insert(i - 1, item);
                    checkedStates.Insert(i - 1, state);
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(i == items.Count - 1);
                if (GUILayout.Button("v", GUILayout.Width(20)))
                {
                    var item = items[i];
                    var state = checkedStates[i];
                    items.RemoveAt(i);
                    checkedStates.RemoveAt(i);
                    items.Insert(i + 1, item);
                    checkedStates.Insert(i + 1, state);
                }
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button("x", GUILayout.Width(20)))
                {
                    items.RemoveAt(i);
                    checkedStates.RemoveAt(i);
                }
                GUILayout.EndHorizontal();
            }
        }

        private static void DrawAddAssets()
        {
            GUILayout.Space(10);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(2));

            if (GUILayout.Button("Select assets..."))
            {
                // TODO add support for custom asset store folder (2022 and later)
                var assetsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity\\Asset Store-5.x");
                if (Directory.Exists(assetsFolder) == true)
                {
                    string path = EditorUtility.OpenFilePanel("Select Asset to Include", assetsFolder, "unitypackage");
                    if (!string.IsNullOrEmpty(path))
                    {
                        // dont add if already in list
                        if (items.Contains(path) == false)
                        {
                            items.Add(path);
                            checkedStates.Add(true);
                        }
                        else
                        {
                            var filename = Path.GetFileName(path);
                            Debug.LogWarning(filename + " is already added.");
                        }
                    }
                }
                else
                {
                    Debug.LogError("Asset folder not found: " + assetsFolder);
                }
            }
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

            if (updatePackages == true) UpdatePackages();
            AssetDatabase.Refresh();
            SaveSettingsAndImportAssets(import: true);

            // skybox off from lighting settings
            RenderSettings.skybox = null;

            // skybox off from camera
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            // TODO set background color?

            // reset camera pos
            Camera.main.transform.position = new Vector3(0, 3, -10);

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
                // FIXME in editor: file is deleted, if re-import some script, while editorwindow is open (resets deletefile bool?)
                var scriptPath = Path.Combine(assetsFolder, "Editor/InitializeProject.cs");

                Debug.Log("Deleting init script: " + scriptPath);
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
                if (File.Exists(scriptPath + ".meta")) File.Delete(scriptPath + ".meta");
            }
            else
            {
                Debug.Log("File not deleted when called init manually");
            }
            AssetDatabase.Refresh();
            // if imported assets, need to enter playmode and off for some reason..
        }

        private void OnEnable()
        {
            LoadSettings();
        }

        private void OnDestroy()
        {
            SaveSettingsAndImportAssets();
            if (importAssets == true)
            {
                // NOTE have to enter playmode to fully import asset packages???
                EditorApplication.EnterPlaymode();

                var stopperScript = Path.Combine(assetsFolder, "Editor/StopPlaymode.cs");
                string contents = @"
using UnityEditor;
using UnityEngine;
using System.IO;
using UnityEditor.Callbacks;

[InitializeOnLoad]
public class StopPlaymode
{
    static StopPlaymode()
    {
        EditorApplication.ExitPlaymode();
        EditorApplication.delayCall += DeleteSelfScript;
    }

    static void DeleteSelfScript()
    {
        var scriptPath = Path.Combine(Application.dataPath, ""Editor/StopPlaymode.cs"");
        if (File.Exists(scriptPath))
        {
            File.Delete(scriptPath);
            File.Delete(scriptPath + "".meta"");
            AssetDatabase.Refresh();
        }
    }
}";
                File.WriteAllText(stopperScript, contents);
            }

            //// create dummy editor script to stop playmode and delete itself
            //if (File.Exists(stopperScript) == false)
            //{
            //    string contents = "using UnityEditor;\n\n[InitializeOnLoad]\npublic class StopPlaymode\n{\n static StopPlaymode()\n {\n EditorApplication.ExitPlaymode();\n System.IO.File.Delete(\"" + stopperScript + "\");}\n}";
            //    File.WriteAllText(stopperScript, contents);
            //}

        }

        private void OnDisable()
        {
            SaveSettingsAndImportAssets();
        }

        private static void LoadSettings()
        {
            items = new List<string>();
            checkedStates = new List<bool>();

            importAssets = EditorPrefs.GetBool(id + "importAssets", true);
            var listOfAssets = EditorPrefs.GetString(id + "listOfAssets", "");
            var checkedState = EditorPrefs.GetString(id + "checkedState", "");

            if (listOfAssets != "")
            {
                var assets = listOfAssets.Split('|');
                foreach (var asset in assets)
                {
                    if (asset != "")
                    {
                        items.Add(asset);
                    }
                }

                var states = checkedState.Split('|');
                foreach (var state in states)
                {
                    if (state != "")
                    {
                        checkedStates.Add(state == "1");
                    }
                }
            }
        }

        static void SaveSettingsAndImportAssets(bool import = false)
        {
            string listOfAssets = "";
            string checkedState = "";
            for (int i = 0; i < items.Count; i++)
            {
                if (checkedStates[i] == true)
                {
                    if (import == true)
                    {
                        if (File.Exists(items[i]) == false)
                        {
                            Debug.LogError("File not found: " + items[i]);
                            continue;
                        }
                        Debug.Log("Importing: " + Path.GetFileName(items[i]));
                        if (importAssets == true) AssetDatabase.ImportPackage(items[i], false);
                    }
                }
                listOfAssets += items[i] + "|";
                checkedState += (checkedStates[i] == true ? 1 : 0) + "|";
            }
            EditorPrefs.SetString(id + "listOfAssets", listOfAssets);
            EditorPrefs.SetString(id + "checkedState", checkedState);
            EditorPrefs.SetBool(id + "importAssets", importAssets);
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
                    if (fromJson.dependencies.ContainsKey(item.Key) == false)
                    {
                        fromJson.dependencies.Add(item.Key, item.Value);
                        Debug.Log("Added " + item.Key);
                    }
                    else
                    {
                        // upgrade version if newer from script
                        if (fromJson.dependencies[item.Key] != item.Value)
                        {
                            Debug.Log("Updated " + item.Key + " from " + fromJson.dependencies[item.Key] + " to " + item.Value);
                            fromJson.dependencies[item.Key] = item.Value;
                        }
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
        static void Checkbox(string label, string tooltip, ref bool value)
        {
            EditorGUILayout.BeginHorizontal();
            //if (GUILayout.Button(label, EditorStyles.label)) value = !value;
            //value = EditorGUILayout.ToggleLeft(label, value);
            // show tooltip in toggle
            value = EditorGUILayout.Toggle(new GUIContent(label, tooltip), value);
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

    #region JSON_HANDLING
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
    #endregion
}

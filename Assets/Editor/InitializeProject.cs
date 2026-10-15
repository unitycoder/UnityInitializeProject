using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityLauncherProTools
{
    public class InitializeProject : EditorWindow
    {
        static readonly string id = "InitializeProject_";

        // settings
        static string[] folders = new string[] { "Fonts", "Materials", "Models", "Prefabs", "Scenes", "Scripts", "Shaders", "Settings", "Sounds", "Textures" };

        static Dictionary<string, string> addPackages = new Dictionary<string, string>() { { "com.unity.ide.visualstudio", "2.0.28" }, { "com.unity.ugui", null } };
        static string[] blackListedPackages = new string[] { "com.unity.modules.unityanalytics", "com.unity.modules.director", "com.unity.collab-proxy", "com.unity.ide.rider", "com.unity.ide.vscode", "com.unity.test-framework", "com.unity.timeline", "com.unity.visualscripting", "com.unity.multiplayer.center" };

        static InitializeProject window;
        static string assetsFolder;
        static bool deleteFile = true;

        const string PendingPackagesKey = "InitializeProject_PendingPackages";
        const string PendingURPKey = "InitializeProject_PendingURP";
        const string PendingDeleteKey = "InitializeProject_PendingDelete";
        const string PendingAddsKey = "InitializeProject_PendingAdds";
        const string PendingRemovalsKey = "InitializeProject_PendingRemovals";
        const string PendingURPConfiguredKey = "InitializeProject_URPConfigured";
        const string PendingURPMoveStartKey = "InitializeProject_URPMoveStart";
        static double pendingManifestCheckStarted = -1;

        [InitializeOnLoadMethod]
        static void ResumePendingSetup()
        {
            EditorApplication.update -= CompletePendingSetup;
            EditorApplication.update += CompletePendingSetup;
        }

        static bool createFolders = true;
        static bool updatePackages = true;
        static bool useURP = false;

        static bool importAssets = false;
        static bool addEssentials = false;
        //static bool importGITPackages = false;
        static List<string> items;
        static List<bool> checkedStates;

        static SearchRequest currentSearch;
        static List<string> packagesToResolve = new List<string>();
        static int currentPackageIndex = 0;
        static bool isSearching = false;
        //static string gitPackagesList = string.Empty;

        private static AddRequest addRequest;

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
            window.minSize = new Vector2(450, 558);

            LoadSettings();
            window.Show();
        }

        void OnGUI()
        {
            GUILayout.Label("Project Setup", EditorStyles.boldLabel);
            GUILayout.Space(10);

            Checkbox("Create Folders", "Common folders: Scripts, Scenes, Textures..", ref createFolders);
            Checkbox("Update Packages", "Adds and removes packages from manifest.json", ref updatePackages);
            Checkbox("Use URP", "Setup Universal Render Pipeline", ref useURP);
            Checkbox("Import Assets", "Imports selected assets from the list below", ref importAssets);
#if UNITY_6000_3_OR_NEWER
            Checkbox("Add Essentials Package", "https://github.com/unitycoder/UnityEditorEssentials.git", ref addEssentials);
#endif
            //Checkbox("Import GIT Packages", "Imports given git package urls from list below", ref importGITPackages);

            GUILayout.Space(10);
            if (GUILayout.Button("Setup Project", GUILayout.Height(64)))
            {
                SetupProject();
            }

            DrawSelectAssets();
            DrawAssetList();

            GUILayout.Space(5);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(2));
            GUILayout.Space(5);

            // textarea
            //GUILayout.Label("UPM Git Packages (1 per line, use #6000.3 for minimum version)");
            //gitPackagesList = EditorGUILayout.TextArea(gitPackagesList, GUILayout.Height(64));

            // enter to confirm, except when typing in textarea
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl() != "TextArea")
            {
                SetupProject();
            }

            // if mouse click anywhere on window
            var theWholeWindowRect = new Rect(0, 0, position.width, position.height);
            if (GUI.Button(theWholeWindowRect, "", GUIStyle.none))
            {
                GUI.FocusControl(null);
            }

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

        private static void DrawSelectAssets()
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

            AssetDatabase.Refresh();
            SaveSettingsAndImportAssets(import: true);

            // skybox off from lighting settings
            RenderSettings.skybox = null;

            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                // skybox off from camera
                mainCamera.clearFlags = CameraClearFlags.SolidColor;
                // reset camera pos
                mainCamera.transform.position = new Vector3(0, 3, -10);
            }

            // disable editor camera easing and acceleration
#if UNITY_2019_1_OR_NEWER
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                sceneView.cameraSettings.easingEnabled = false;
                sceneView.cameraSettings.accelerationEnabled = false;
            }
#endif

            // paraller imports 2021.2 or later (BUT doesnt seem to work..?)
#if UNITY_2021_2_OR_NEWER
            EnableParallelAssetImport(true);
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

            // Package changes may reload scripts, so keep the setup state in SessionState.
            bool installPackages = updatePackages || useURP;
#if UNITY_6000_3_OR_NEWER
            installPackages |= addEssentials;
#endif
            SessionState.SetBool(PendingPackagesKey, installPackages);
            SessionState.SetBool(PendingURPKey, useURP);
            SessionState.SetBool(PendingDeleteKey, deleteFile);
            SessionState.SetBool(PendingURPConfiguredKey, false);
            SessionState.SetFloat(PendingURPMoveStartKey, -1f);

            if (installPackages) UpdatePackages();
            window.Close();
            if (!installPackages) FinishSetup();

            //if (importGITPackages == true && string.IsNullOrEmpty(gitPackagesList) == false)
            //{
            //    var gitUrls = gitPackagesList.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            //    var gitPackagesToInstall = new List<string>();
            //    foreach (var url in gitUrls)
            //    {
            //        var trimmerURL = url.Trim();
            //        // skip empty, comment rows and invalid urls
            //        if (string.IsNullOrEmpty(trimmerURL) == false && trimmerURL.IndexOf("#") != 0 && trimmerURL.IndexOf("http") == 0)
            //        {
            //            // remove trailing # if any
            //            var hashIndex = trimmerURL.IndexOf("#");
            //            if (hashIndex > -1)
            //            {
            //                // compare hash with editor version, has is like "#6000.3", and if version is lower, then dont add package, and log warning
            //                var versionPart = trimmerURL.Substring(hashIndex + 1);
            //                var versionNumbers = versionPart.Split('.');

            //                var editorVersion = Application.unityVersion;
            //                var editorVersionParts = editorVersion.Split('.');

            //                if (versionNumbers.Length > 1 && editorVersionParts.Length > 1)
            //                {
            //                    if (int.TryParse(versionNumbers[0], out int requiredMajor) && int.TryParse(editorVersionParts[0], out int editorMajor))
            //                    {
            //                        if (editorMajor < requiredMajor)
            //                        {
            //                            Debug.LogWarning($"Skipping {trimmerURL} because it requires Unity {versionPart} or higher, but current version is {editorVersion}");
            //                            continue;
            //                        }

            //                        if (int.TryParse(versionNumbers[1], out int requiredMinor) && int.TryParse(editorVersionParts[1], out int editorMinor))
            //                        {
            //                            if (editorMinor < requiredMinor)
            //                            {
            //                                Debug.LogWarning($"Skipping {trimmerURL} because it requires Unity {versionPart} or higher, but current version is {editorVersion}");
            //                                continue;
            //                            }
            //                        }
            //                    }
            //                }

            //                trimmerURL = trimmerURL.Substring(0, hashIndex);
            //            }

            //            gitPackagesToInstall.Add(trimmerURL);
            //            Debug.Log($"Queued {trimmerURL} for installation");
            //        }
            //    }
            //    BatchGitInstaller.InstallAll(gitPackagesToInstall);
            //} // if import git packages

            // if imported assets, need to enter playmode and off for some reason..
        }

        private void OnEnable()
        {
            LoadSettings();
        }

        private void OnDestroy()
        {
            SaveSettingsAndImportAssets();
            if (SessionState.GetBool(PendingPackagesKey, false)) return;
            if (importAssets == true)
            {
                // NOTE have to enter playmode to fully import asset packages???
#if UNITY_2019_1_OR_NEWER
                EditorApplication.EnterPlaymode(); // not available in <2019.1

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

#endif
        } // OnDestroy()

        private void OnDisable()
        {
            SaveSettingsAndImportAssets();
        }

        private static void LoadSettings()
        {
            items = new List<string>();
            checkedStates = new List<bool>();

            importAssets = EditorPrefs.GetBool(id + "importAssets", true);
            addEssentials = EditorPrefs.GetBool(id + "addEssentials", false);
            useURP = EditorPrefs.GetBool(id + "useURP", false);
            //gitPackagesList = EditorPrefs.GetString(id + "gitPackagesList", "");
            var listOfAssets = EditorPrefs.GetString(id + "listOfAssets", "");
            var checkedState = EditorPrefs.GetString(id + "checkedState", "");
            // importGITPackages

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
            if (items == null || checkedStates == null) return;
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
            EditorPrefs.SetBool(id + "addEssentials", addEssentials);
            EditorPrefs.SetBool(id + "useURP", useURP);

            //EditorPrefs.SetString(id + "gitPackagesList", gitPackagesList);
        }

        private static void AddCustomGitPackages()
        {
#if UNITY_6000_3_OR_NEWER
            if (addEssentials == true)
            {
                var essentialsUrl = "https://github.com/unitycoder/UnityEditorEssentials.git";
                addRequest = Client.Add(essentialsUrl);
            }
#endif
        }

        private static AddAndRemoveRequest packageRequest;

        // Only packages in manifest.json/dependencies can be removed with Client.AddAndRemove.
        // Do not use Client.List() here: its results also contain indirect dependencies.
        static Dictionary<string, string> GetDirectDependencies()
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/manifest.json"));
            if (!File.Exists(path)) throw new FileNotFoundException("Project package manifest not found", path);

            string json = File.ReadAllText(path);
            var section = System.Text.RegularExpressions.Regex.Match(json, "\"dependencies\"\\s*:\\s*\\{(?<body>[^}]*)\\}", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (!section.Success) throw new InvalidOperationException("The project manifest has no dependencies object.");

            var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match entry in System.Text.RegularExpressions.Regex.Matches(
                section.Groups["body"].Value, "\"(?<name>[^\"]+)\"\\s*:\\s*\"(?<version>(?:\\\\.|[^\"\\\\])*)\""))
            {
                dependencies[entry.Groups["name"].Value] = entry.Groups["version"].Value;
            }
            return dependencies;
        }

        static void UpdatePackages()
        {
            if (packageRequest != null && !packageRequest.IsCompleted) return;

            Dictionary<string, string> current;
            try { current = GetDirectDependencies(); }
            catch (Exception ex)
            {
                Debug.LogError("Cannot update packages: " + ex.Message);
                CancelPendingSetup();
                return;
            }

            var packagesToAdd = updatePackages
                ? addPackages.Select(p => string.IsNullOrEmpty(p.Value) ? p.Key : p.Key + "@" + p.Value).ToList()
                : new List<string>();

            if (useURP) packagesToAdd.Add("com.unity.render-pipelines.universal");
#if UNITY_6000_3_OR_NEWER
            if (addEssentials) packagesToAdd.Add("https://github.com/unitycoder/UnityEditorEssentials.git");
#endif

            var packagesToRemove = new List<string>();
            if (updatePackages)
            {
                foreach (var name in blackListedPackages)
                {
                    if (current.ContainsKey(name)) packagesToRemove.Add(name);
                    else Debug.Log("Skipping removal (not a direct dependency): " + name);
                }
            }

            // These expectations survive assembly reloads caused by installing URP or other packages.
            SessionState.SetString(PendingAddsKey, string.Join("\n", packagesToAdd));
            SessionState.SetString(PendingRemovalsKey, string.Join("\n", packagesToRemove));
            pendingManifestCheckStarted = -1;

            if (packagesToAdd.Count == 0 && packagesToRemove.Count == 0)
            {
                Debug.Log("No package changes needed.");
                CompletePendingSetup();
                return;
            }

            try
            {
                packageRequest = Client.AddAndRemove(packagesToAdd.ToArray(), packagesToRemove.ToArray());
                EditorApplication.update -= CheckPackageUpdate;
                EditorApplication.update += CheckPackageUpdate;
                Debug.Log("Updating packages. Add: " + string.Join(", ", packagesToAdd) + "; Remove: " + string.Join(", ", packagesToRemove));
            }
            catch (Exception ex)
            {
                Debug.LogError("Could not start package update: " + ex);
                packageRequest = null;
                CancelPendingSetup();
            }
        }

        static void CheckPackageUpdate()
        {
            if (packageRequest == null || !packageRequest.IsCompleted) return;

            EditorApplication.update -= CheckPackageUpdate;
            if (packageRequest.Status == StatusCode.Success)
            {
                Debug.Log("Project package update completed.");
                packageRequest = null;
                CompletePendingSetup();
            }
            else
            {
                Debug.LogError("Package update failed: " + (packageRequest.Error != null ? packageRequest.Error.message : "Unknown error"));
                packageRequest = null;
                CancelPendingSetup();
            }
        }

        static bool RequestedPackageChangesApplied()
        {
            Dictionary<string, string> current;
            try { current = GetDirectDependencies(); }
            catch { return false; }

            foreach (var add in SessionState.GetString(PendingAddsKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (add.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || add.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    if (!current.Values.Any(value => value.Contains(add))) return false;
                    continue;
                }

                int at = add.IndexOf('@');
                string name = at >= 0 ? add.Substring(0, at) : add;
                string expectedVersion = at >= 0 ? add.Substring(at + 1) : null;
                if (!current.TryGetValue(name, out string installedVersion)) return false;
                if (expectedVersion != null && installedVersion != expectedVersion) return false;
            }

            foreach (var remove in SessionState.GetString(PendingRemovalsKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.ContainsKey(remove)) return false;
            }
            return true;
        }

        static void CancelPendingSetup()
        {
            SessionState.SetBool(PendingPackagesKey, false);
            SessionState.SetBool(PendingURPKey, false);
            SessionState.SetBool(PendingDeleteKey, false);
            SessionState.SetBool(PendingURPConfiguredKey, false);
            SessionState.SetFloat(PendingURPMoveStartKey, -1f);
            SessionState.EraseString(PendingAddsKey);
            SessionState.EraseString(PendingRemovalsKey);
            EditorApplication.update -= CompletePendingSetup;
        }

        // Continue only after the manifest reflects the requested changes.
        // This also handles a domain reload before CheckPackageUpdate receives its callback.
        static void CompletePendingSetup()
        {
            if (!SessionState.GetBool(PendingPackagesKey, false))
            {
                EditorApplication.update -= CompletePendingSetup;
                return;
            }

            if (packageRequest != null && !packageRequest.IsCompleted) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!RequestedPackageChangesApplied())
            {
                if (packageRequest == null)
                {
                    if (pendingManifestCheckStarted < 0) pendingManifestCheckStarted = EditorApplication.timeSinceStartup;
                    else if (EditorApplication.timeSinceStartup - pendingManifestCheckStarted > 90)
                    {
                        Debug.LogError("Package setup did not reach the expected manifest state. Check Package Manager errors and Packages/manifest.json.");
                        CancelPendingSetup();
                    }
                }
                return;
            }

            pendingManifestCheckStarted = -1;
            if (SessionState.GetBool(PendingURPKey, false))
            {
                if (!SessionState.GetBool(PendingURPConfiguredKey, false))
                {
                    var urpType = FindURPType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset");
                    if (urpType == null) return;

                    try
                    {
                        SetupURP(urpType);
                        EnsureURPGlobalSettings();
                        SessionState.SetBool(PendingURPConfiguredKey, true);
                        SessionState.SetFloat(PendingURPMoveStartKey, (float)EditorApplication.timeSinceStartup);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("URP setup failed: " + ex);
                        CancelPendingSetup();
                        return;
                    }
                }

                // URP may create its Global Settings and default Volume Profile after import.
                // Keep the initializer alive until both have been moved, or a short grace period expires.
                if (!MoveGeneratedURPAssets())
                {
                    float elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(PendingURPMoveStartKey, (float)EditorApplication.timeSinceStartup);
                    if (elapsed < 10f) return;
                    Debug.LogWarning("URP default assets were not both available for relocation. Any missing assets were left unchanged.");
                }
            }

            FinishSetup();
        }

        static Type FindURPType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        // Ask URP to create its global settings (and default volume profile) before relocating them.
        // Reflection avoids a compile-time dependency on URP in freshly created Built-in RP projects.
        static void EnsureURPGlobalSettings()
        {
            var type = FindURPType("UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings");
            if (type == null) return;

            var ensure = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "Ensure" && (m.GetParameters().Length == 0 ||
                    (m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(bool))));
            if (ensure == null) return;

            try
            {
                ensure.Invoke(null, ensure.GetParameters().Length == 0 ? null : new object[] { true });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Could not ensure URP global settings: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
            }
        }

        static bool MoveGeneratedURPAssets()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");

            bool globalSettingsMoved = MoveGeneratedURPAsset("UniversalRenderPipelineGlobalSettings.asset");
            bool defaultVolumeMoved = MoveGeneratedURPAsset("DefaultVolumeProfile.asset");
            if (globalSettingsMoved && defaultVolumeMoved) AssetDatabase.SaveAssets();
            return globalSettingsMoved && defaultVolumeMoved;
        }

        static bool MoveGeneratedURPAsset(string fileName)
        {
            string sourcePath = "Assets/" + fileName;
            string destinationPath = "Assets/Settings/" + fileName;
            var existingDestination = AssetDatabase.LoadMainAssetAtPath(destinationPath);
            var existingSource = AssetDatabase.LoadMainAssetAtPath(sourcePath);

            if (existingDestination != null)
            {
                if (existingSource != null) Debug.LogWarning("URP asset move skipped because the destination already exists: " + destinationPath);
                return true;
            }

            if (existingSource == null) return false;

            string error = AssetDatabase.MoveAsset(sourcePath, destinationPath);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError("Failed to move URP asset " + sourcePath + ": " + error);
                return false;
            }

            Debug.Log("Moved URP asset to " + destinationPath);
            return true;
        }

        // Reflection avoids a hard dependency on URP while initializing a Built-in RP project.
        static void SetupURP(Type urpType)
        {
            const string pipelinePath = "Assets/Settings/ProjectURP.asset";
            const string rendererPath = "Assets/Settings/ProjectURP_Renderer.asset";
            if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");

            RenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(pipelinePath);
            if (pipeline != null && !urpType.IsInstanceOfType(pipeline))
                throw new InvalidOperationException("An asset at " + pipelinePath + " is not a URP pipeline asset.");

            if (pipeline == null)
            {
                // Preserve an already configured URP project.
                if (GraphicsSettings.defaultRenderPipeline != null && urpType.IsInstanceOfType(GraphicsSettings.defaultRenderPipeline))
                    pipeline = GraphicsSettings.defaultRenderPipeline;
                else
                {
                    var renderer = AssetDatabase.LoadAssetAtPath<ScriptableObject>(rendererPath);
                    if (renderer == null) renderer = CreateURPRenderer(urpType, pipelinePath, rendererPath);

                    var createMethod = urpType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsInstanceOfType(renderer));
                    pipeline = createMethod != null ? createMethod.Invoke(null, new object[] { renderer }) as RenderPipelineAsset : null;

                    if (pipeline == null)
                    {
                        // Fallback if the URP asset factory API changes.
                        pipeline = ScriptableObject.CreateInstance(urpType) as RenderPipelineAsset;
                        if (pipeline == null) throw new InvalidOperationException("Cannot instantiate URP pipeline asset.");
                        var serialized = new SerializedObject(pipeline);
                        var renderers = serialized.FindProperty("m_RendererDataList");
                        if (renderers == null) throw new InvalidOperationException("URP renderer list property not found.");
                        renderers.arraySize = 1;
                        renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }

                    AssetDatabase.CreateAsset(pipeline, pipelinePath);
                }
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;

            // Assign it to all quality levels, so none can fall back to Built-in RP.
            int originalLevel = QualitySettings.GetQualityLevel();
            try
            {
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    QualitySettings.renderPipeline = pipeline;
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(originalLevel, false);
            }

            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
            Debug.Log("URP enabled: " + AssetDatabase.GetAssetPath(pipeline));
        }

        static ScriptableObject CreateURPRenderer(Type urpType, string pipelinePath, string rendererPath)
        {
            // Prefer Unity's own factory, which sets up the renderer resources correctly.
            var factory = urpType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "CreateRendererAsset" && m.GetParameters().Length == 4 && m.GetParameters()[0].ParameterType == typeof(string));

            if (factory != null)
            {
                try
                {
                    var parameters = factory.GetParameters();
                    var rendererKind = Enum.Parse(parameters[1].ParameterType, "UniversalRenderer");
                    var result = factory.Invoke(null, new object[] { pipelinePath, rendererKind, true, "Renderer" }) as ScriptableObject;
                    if (result != null) return result;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("URP renderer factory failed, using fallback: " + ex.Message);
                    var existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(rendererPath);
                    if (existing != null) return existing;
                }
            }

            // Fallback for versions that no longer expose the internal factory.
            var rendererType = FindURPType("UnityEngine.Rendering.Universal.UniversalRendererData");
            if (rendererType == null) throw new InvalidOperationException("UniversalRendererData type not found.");
            var renderer = ScriptableObject.CreateInstance(rendererType);
            AssetDatabase.CreateAsset(renderer, rendererPath);

            // Older versions store post-processing resources directly on the renderer.
            var postProcessType = FindURPType("UnityEngine.Rendering.Universal.PostProcessData");
            if (postProcessType != null)
            {
                var getDefault = postProcessType.GetMethod("GetDefaultPostProcessData", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var defaultData = getDefault != null ? getDefault.Invoke(null, null) as UnityEngine.Object : null;
                var prop = rendererType.GetProperty("postProcessData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.CanWrite && defaultData != null) prop.SetValue(renderer, defaultData, null);
            }

            EditorUtility.SetDirty(renderer);
            return renderer;
        }

        static void FinishSetup()
        {
            bool shouldDelete = SessionState.GetBool(PendingDeleteKey, false);
            SessionState.SetBool(PendingPackagesKey, false);
            SessionState.SetBool(PendingURPKey, false);
            SessionState.SetBool(PendingDeleteKey, false);
            SessionState.SetBool(PendingURPConfiguredKey, false);
            SessionState.SetFloat(PendingURPMoveStartKey, -1f);
            SessionState.EraseString(PendingAddsKey);
            SessionState.EraseString(PendingRemovalsKey);
            EditorApplication.update -= CompletePendingSetup;

            if (!shouldDelete)
            {
                Debug.Log("Initialize Project complete. Script retained for manual testing.");
                return;
            }

            EditorApplication.delayCall += () =>
            {
                var scriptPath = Path.Combine(Application.dataPath, "Editor/InitializeProject.cs");
                if (File.Exists(scriptPath))
                {
                    File.Delete(scriptPath);
                    if (File.Exists(scriptPath + ".meta")) File.Delete(scriptPath + ".meta");
                    Debug.Log("Deleted initialization script: " + scriptPath);
                    AssetDatabase.Refresh();
                }
            };
        }

        static void StartNextPackageVersionSearch()
        {
            if (currentPackageIndex >= packagesToResolve.Count)
            {
                Debug.Log("Finished resolving all null-version packages.");
                return;
            }

            var packageName = packagesToResolve[currentPackageIndex];
            Debug.Log($"Searching for latest version of {packageName}...");
            currentSearch = Client.Search(packageName);
            EditorApplication.update += PackageVersionFetchProgress;
            isSearching = true;
        }

        static void PackageVersionFetchProgress()
        {
            if (!isSearching || currentSearch == null || !currentSearch.IsCompleted) return;

            var packageName = packagesToResolve[currentPackageIndex];

            if (currentSearch.Status == StatusCode.Success)
            {
                var latestVersion = currentSearch.Result
                    .OrderByDescending(p => p.version)
                    .FirstOrDefault()?.version;

                if (latestVersion != null)
                {
                    Debug.Log($"Resolved {packageName} to version {latestVersion}");
                    addPackages[packageName] = latestVersion; // <--- important
                }
                else
                {
                    Debug.LogWarning($"No version found for {packageName}");
                }
            }
            else
            {
                Debug.LogError($"Failed to resolve {packageName}: {currentSearch.Error.message}");
            }

            isSearching = false;
            EditorApplication.update -= PackageVersionFetchProgress;
            currentPackageIndex++;
            StartNextPackageVersionSearch();
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

#if UNITY_2021_2_OR_NEWER
        // NOTE doesnt work?
        public static void EnableParallelAssetImport(bool enabled)
        {
            var assembly = typeof(EditorSettings).Assembly;
            var type = assembly.GetType("UnityEditor.AssetPipelinePreferences");

            if (type != null)
            {
                var method = type.GetMethod("SetParallelImport", BindingFlags.Static | BindingFlags.NonPublic);
                if (method != null)
                {
                    method.Invoke(null, new object[] { enabled });
                    Debug.Log($"Parallel Import {(enabled ? "enabled" : "disabled")} via script.");
                }
                else
                {
                    Debug.LogWarning("SetParallelImport method not found.");
                }
            }
            else
            {
                Debug.LogWarning("AssetPipelinePreferences type not found.");
            }
        }
#endif

    }  // class InitializeProject

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

    public static class GitPackageInstaller
    {
        public static void Install(string gitUrl, Action<bool, string> onComplete = null)
        {
            var request = Client.Add(gitUrl);

            void Update()
            {
                if (!request.IsCompleted) return;

                EditorApplication.update -= Update;

                if (request.Status == StatusCode.Success)
                {
                    var packageInfo = request.Result;
                    var msg = $"Installed '{packageInfo.name}' v{packageInfo.version}";
                    Debug.Log($"[GitPackageInstaller] {msg}");
                    onComplete?.Invoke(true, msg);
                }
                else
                {
                    var err = request.Error != null
                        ? $"{request.Error.errorCode}: {request.Error.message}"
                        : "Unknown error";
                    Debug.LogError($"[GitPackageInstaller] Failed to install '{gitUrl}': {err}");
                    onComplete?.Invoke(false, err);
                }
            }

            EditorApplication.update += Update;
        }
    } // GitPackageInstaller

    public static class BatchGitInstaller
    {
        public static void InstallAll(List<string> gitPackages)
        {
            var remaining = new Queue<string>(gitPackages);
            var results = new List<(string name, bool success, string msg)>();
            int total = gitPackages.Count;

            void InstallNext()
            {
                if (remaining.Count == 0)
                {
                    Debug.Log($"[BatchGitInstaller] All done. " +
                        $"{results.Count(r => r.success)}/{total} succeeded.");
                    return;
                }

                var url = remaining.Dequeue();
                float progress = 1f - (float)remaining.Count / total;
                EditorUtility.DisplayProgressBar("Installing Packages",
                    $"Installing {url}...", progress);

                GitPackageInstaller.Install(url, (success, msg) =>
                {
                    results.Add((url, success, msg));

                    if (remaining.Count == 0)
                    {
                        EditorUtility.ClearProgressBar();
                    }

                    InstallNext();
                });
            }

            InstallNext();
        }
    }

} // namespace

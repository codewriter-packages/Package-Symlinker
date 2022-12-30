#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CodeWriter.PackageSymLinker
{
    public class PackageSymLinkerWindow : EditorWindow, IHasCustomMenu
    {
        private const string RecentPackagesPrefsKey = "PackageSymlinker_Recent";
        private const char RecentPackagesSeparator = '#';

        [MenuItem("Tools/Package Symlinker")]
        public static void OpenWindow()
        {
            var window = GetWindow<PackageSymLinkerWindow>();
            window.Show();
        }

        [SerializeField] private List<SymlinkedDirInfo> symLinks = new List<SymlinkedDirInfo>();
        [SerializeField] private Vector2 scroll;
        [SerializeField] private string[] recentPackagesPaths = Array.Empty<string>();
        [SerializeField] private Package[] recentPackageInfo = Array.Empty<Package>();

        private void OnEnable()
        {
            ReloadLinkedPackages();
            RefreshRecentPackages();
        }

        private void OnFocus()
        {
            ReloadLinkedPackages();
            RefreshRecentPackages();
        }

        private void OnGUI()
        {
            OnToolbarGUI();

            scroll = GUILayout.BeginScrollView(scroll, false, true);
            OnLinkedPackagesGUI();
            GUILayout.Space(50);
            OnRecentPackagesGUI();
            EditorGUILayout.EndScrollView();
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("Clear Recent"), false, ClearRecentPackages);
        }

        private void OnToolbarGUI()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Space(5);

            if (GUILayout.Button("Link Package", EditorStyles.toolbarButton))
            {
                AddPackage();
            }

            GUILayout.FlexibleSpace();

            GUILayout.EndHorizontal();
        }

        private void OnLinkedPackagesGUI()
        {
            var labelStyle = EditorStyles.boldLabel;
            labelStyle.richText = true;
            
            if (symLinks.Count == 0)
            {
                GUILayout.Space(20);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("No symbolic linked packages in project", EditorStyles.largeLabel);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(20);
                
                return;
            }

            GUILayout.Label("Linked packages", EditorStyles.largeLabel);

            foreach (var symLink in symLinks)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{symLink.package.name} : <i>{symLink.package.version}</i>", labelStyle);
                if (GUILayout.Button("Delete link", GUILayout.Width(120)))
                {
                    DeletePackage(symLink.path);
                }

                GUILayout.EndHorizontal();

                GUILayout.Label(symLink.path, EditorStyles.miniLabel);

                GUILayout.EndVertical();
            }
        }

        private void OnRecentPackagesGUI()
        {
            if (recentPackagesPaths.Length == 0)
            {
                return;
            }

            GUILayout.Label("Recent packages", EditorStyles.largeLabel);

            for (var index = 0; index < recentPackagesPaths.Length; index++)
            {
                var packagePath = recentPackagesPaths[index];
                var package = recentPackageInfo[index];

                if (string.IsNullOrEmpty(package.name))
                {
                    continue;
                }

                GUILayout.BeginVertical(EditorStyles.helpBox);

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{package.name} : <i>{package.version}</i>", EditorStyles.boldLabel);

                if (IsPackageLinked(package.name))
                {
                    GUILayout.Label("Linked", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(120));
                }
                else
                {
                    if (GUILayout.Button("Link", GUILayout.Width(120)))
                    {
                        AddPackage(packagePath);
                    }
                }

                GUILayout.EndHorizontal();

                GUILayout.Label(packagePath, EditorStyles.miniLabel);

                GUILayout.EndVertical();
            }
        }

        private void ReloadLinkedPackages()
        {
            var packagesFolderPath = GetPackagesFolderPath();

            var dirPaths = Directory.GetDirectories(packagesFolderPath);

            symLinks = dirPaths
                .Where(path =>
                {
                    const FileAttributes attrs = FileAttributes.Directory | FileAttributes.ReparsePoint;
                    var srcPackageJsonPath = Path.Combine(path, "package.json");
                    return (File.GetAttributes(path) & attrs) == attrs && File.Exists(srcPackageJsonPath);
                })
                .Select(path =>
                {
                    var srcPackageJsonPath = Path.Combine(path, "package.json");
                    var packageJsonString = File.ReadAllText(srcPackageJsonPath);
                    var packageInfo = JsonUtility.FromJson<Package>(packageJsonString);

                    return new SymlinkedDirInfo
                    {
                        path = path,
                        package = packageInfo
                    };
                })
                .ToList();

            if (symLinks.Count > 0)
            {
                titleContent = new GUIContent($"Package Symlinker ({symLinks.Count})");
            }
            else
            {
                titleContent = new GUIContent($"Package Symlinker");
            }
        }

        private void AddPackage()
        {
            var srcFolderPath = EditorUtility.OpenFolderPanel("Select Package", string.Empty, string.Empty);
            AddPackage(srcFolderPath);
        }

        private void AddPackage(string srcFolderPath)
        {
            if (string.IsNullOrEmpty(srcFolderPath))
            {
                Debug.LogError("No folder selected");
                return;
            }

            srcFolderPath = srcFolderPath.Replace('/', Path.DirectorySeparatorChar);

            var srcPackageJsonPath = Path.Combine(srcFolderPath, "package.json");
            if (!File.Exists(srcPackageJsonPath))
            {
                Debug.LogError("package.json file not found at " + srcPackageJsonPath);
                return;
            }

            var packageJsonString = File.ReadAllText(srcPackageJsonPath);
            var packageInfo = JsonUtility.FromJson<Package>(packageJsonString);

            if (string.IsNullOrEmpty(packageInfo.name))
            {
                Debug.LogError("package.json name is null or empty");
                return;
            }

            var dstPackagesFolderPath = GetPackagesFolderPath();

#if UNITY_EDITOR_WIN
            var dstPackagePath = Path.Combine(dstPackagesFolderPath, packageInfo.name);

            var fileExist = Directory.Exists(dstPackagePath);
            if (fileExist)
            {
                Debug.LogError($"Directory {dstPackagePath} already exist");
                return;
            }

            var command = $"mklink /j \"{dstPackagePath}\" \"{srcFolderPath}\"";
#else
            var command = $"ln -s {srcFolderPath} {dstPackagesFolderPath}";
#endif
            if (TryExecuteCmd(command, out _, out var error) != 0)
            {
                //on osx return code can be not 0
                //double check error
                if(string.IsNullOrEmpty(error) == false)
                {
                    Debug.LogError($"Failed to link package: {error}");
                    return;
                }
            }

            AddPackageToRecent(srcFolderPath);
            Client.Resolve();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            ReloadLinkedPackages();
        }

        private void DeletePackage(string path)
        {
#if UNITY_EDITOR_WIN
            var command = $"rd \"{path}\"";
#else
            var command = $"unlink {path}";
#endif
            if (TryExecuteCmd(command, out _, out var error) != 0)
            {
                //on osx return code can be not 0
                //double check error
                if(string.IsNullOrEmpty(error) == false)
                {
                    Debug.LogError($"Failed to delete package link: {error}");
                    return;
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            ReloadLinkedPackages();
        }

        public static string GetPackagesFolderPath()
        {
            var assetsFolderPath = Application.dataPath;
            var projectFolderPath = assetsFolderPath.Substring(0, assetsFolderPath.Length - "/Assets".Length);
            var packageFolderPath = projectFolderPath + "/Packages";
            return packageFolderPath.Replace('/', Path.DirectorySeparatorChar);
        }

        public static int TryExecuteCmd(string command, out string output, out string error)
        {
#if UNITY_EDITOR_WIN
            var cmd = "cmd.exe";
            var args = $"/c {command}";
#else
            var cmd = "/bin/bash";
            var args = $"-c \"{command}\"";
#endif
            var startInfo = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                StandardErrorEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            };

            var launchProcess = Process.Start(startInfo);
            if (launchProcess == null || launchProcess.HasExited || launchProcess.Id == 0)
            {
                output = error = string.Empty;
                return int.MinValue;
            }

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            launchProcess.OutputDataReceived += (sender, e) => outputBuilder.AppendLine(e.Data ?? "");
            launchProcess.ErrorDataReceived += (sender, e) => errorBuilder.AppendLine(e.Data ?? "");

            launchProcess.BeginOutputReadLine();
            launchProcess.BeginErrorReadLine();
            launchProcess.EnableRaisingEvents = true;

            launchProcess.WaitForExit();

            output = outputBuilder.ToString();
            error = errorBuilder.ToString();
            return launchProcess.ExitCode;
        }

        private void RefreshRecentPackages()
        {
            recentPackagesPaths = EditorPrefs.GetString(RecentPackagesPrefsKey, "")
                .Split(RecentPackagesSeparator, StringSplitOptions.RemoveEmptyEntries);

            recentPackageInfo = new Package[recentPackagesPaths.Length];

            for (var i = 0; i < recentPackageInfo.Length; i++)
            {
                var packageJsonPath = Path.Combine(recentPackagesPaths[i], "package.json");
                if (!File.Exists(packageJsonPath))
                {
                    continue;
                }

                var packageJsonString = File.ReadAllText(packageJsonPath);
                var packageInfo = JsonUtility.FromJson<Package>(packageJsonString);

                recentPackageInfo[i] = packageInfo;
            }
        }

        private void AddPackageToRecent(string path)
        {
            RefreshRecentPackages();

            var list = recentPackagesPaths.ToList();
            list.Remove(path);
            list.Add(path);
            recentPackagesPaths = list.ToArray();
            EditorPrefs.SetString(RecentPackagesPrefsKey, string.Join(RecentPackagesSeparator, recentPackagesPaths));

            RefreshRecentPackages();
        }

        private void ClearRecentPackages()
        {
            EditorPrefs.SetString(RecentPackagesPrefsKey, "");
            RefreshRecentPackages();
        }

        private bool IsPackageLinked(string packageName)
        {
            foreach (var dir in symLinks)
            {
                if (dir.package.name == packageName)
                {
                    return true;
                }
            }

            return false;
        }

        [Serializable]
        private struct Package
        {
            public string name;
            public string version;
        }

        [Serializable]
        private struct SymlinkedDirInfo
        {
            public string path;
            public Package package;
        }
    }
}
#endif
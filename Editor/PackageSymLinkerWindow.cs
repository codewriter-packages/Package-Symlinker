#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
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

        [SerializeField] private List<SymlinkedDirInfo> directories = new List<SymlinkedDirInfo>();
        [SerializeField] private Vector2 scroll;
        [SerializeField] private string[] recentPackages = Array.Empty<string>();
        [SerializeField] private string[] recentPackageNames = Array.Empty<string>();

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
            menu.AddItem(new GUIContent("Clear Recent"), false, () => ClearRecentPackages());
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
            if (directories.Count == 0)
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

            foreach (var folder in directories)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);

                GUILayout.BeginHorizontal();
                GUILayout.Label(folder.name, EditorStyles.boldLabel);
                if (GUILayout.Button("Delete link", GUILayout.Width(120)))
                {
                    DeletePackage(folder.path);
                }

                GUILayout.EndHorizontal();

                GUILayout.Label(folder.sourcePath, EditorStyles.miniLabel);

                GUILayout.EndVertical();
            }
        }

        private void OnRecentPackagesGUI()
        {
            if (recentPackages.Length == 0)
            {
                return;
            }

            GUILayout.Label("Recent packages", EditorStyles.largeLabel);

            for (var index = 0; index < recentPackages.Length; index++)
            {
                var packagePath = recentPackages[index];
                var packageName = recentPackageNames[index];

                if (string.IsNullOrEmpty(packageName))
                {
                    continue;
                }

                GUILayout.BeginVertical(EditorStyles.helpBox);

                GUILayout.BeginHorizontal();
                GUILayout.Label(packageName, EditorStyles.boldLabel);

                if (IsPackageLinked(packageName))
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

            if (TryExecuteCmd($"dir \"{packagesFolderPath}\"", out var result) != 0)
            {
                Debug.LogError($"Failed to list directories in {packagesFolderPath}");
                return;
            }

            var dirInfoLines = result
                .Split(new[] {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("<JUNCTION>"))
                .ToList();

            directories = Directory.EnumerateDirectories(packagesFolderPath)
                .Where(path =>
                {
                    const FileAttributes attrs = FileAttributes.Directory | FileAttributes.ReparsePoint;
                    return (File.GetAttributes(path) & attrs) == attrs;
                })
                .Select(path =>
                {
                    var folderName = Path.GetFileName(path);
                    var dirInfo = dirInfoLines.FirstOrDefault(line => line.Contains(folderName));
                    var sourcePath = dirInfo != null &&
                                     dirInfo.IndexOf('[') is int start &&
                                     dirInfo.IndexOf(']') is int end &&
                                     start != -1 && end != -1 && end > start
                        ? dirInfo.Substring(start + 1, end - start - 1)
                        : "INVALID";

                    return new SymlinkedDirInfo
                    {
                        path = path,
                        name = folderName,
                        sourcePath = sourcePath,
                    };
                })
                .ToList();

            if (this.directories.Count > 0)
            {
                titleContent = new GUIContent($"Package Symlinker ({directories.Count})");
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
            var dstPackagePath = Path.Combine(dstPackagesFolderPath, packageInfo.name);

            var fileExist = TryExecuteCmd($"dir \"{dstPackagePath}\"", out _) != 1;
            if (fileExist)
            {
                Debug.LogError($"Directory {dstPackagePath} already exist");
                return;
            }

            var command = $"mklink /j \"{dstPackagePath}\" \"{srcFolderPath}\"";
            if (TryExecuteCmd(command, out _) != 0)
            {
                Debug.LogError("Failed to link package");
                return;
            }

            AddPackageToRecent(srcFolderPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            ReloadLinkedPackages();
        }

        private void DeletePackage(string path)
        {
            var command = $"rd \"{path}\"";
            if (TryExecuteCmd(command, out _) != 0)
            {
                Debug.LogError("Failed to delete package link");
                return;
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

        public static int TryExecuteCmd(string command, out string result)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
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
                result = default;
                return int.MinValue;
            }

            var output = new StringBuilder();
            var error = new StringBuilder();

            launchProcess.OutputDataReceived += (sender, e) => output.AppendLine(e.Data ?? "");
            launchProcess.ErrorDataReceived += (sender, e) => error.AppendLine(e.Data ?? "");

            launchProcess.BeginOutputReadLine();
            launchProcess.BeginErrorReadLine();
            launchProcess.EnableRaisingEvents = true;

            launchProcess.WaitForExit();

            if (launchProcess.ExitCode != 0)
            {
                result = default;
                return launchProcess.ExitCode;
            }

            result = output.ToString();
            return 0;
        }

        private void RefreshRecentPackages()
        {
            recentPackages = EditorPrefs.GetString(RecentPackagesPrefsKey, "")
                .Split(RecentPackagesSeparator, StringSplitOptions.RemoveEmptyEntries);

            recentPackageNames = new string[recentPackages.Length];

            for (var i = 0; i < recentPackageNames.Length; i++)
            {
                var packageJsonPath = Path.Combine(recentPackages[i], "package.json");
                if (!File.Exists(packageJsonPath))
                {
                    continue;
                }

                var packageJsonString = File.ReadAllText(packageJsonPath);
                var packageInfo = JsonUtility.FromJson<Package>(packageJsonString);

                recentPackageNames[i] = packageInfo.name;
            }
        }

        private void AddPackageToRecent(string path)
        {
            RefreshRecentPackages();

            var list = recentPackages.ToList();
            list.Remove(path);
            list.Add(path);
            recentPackages = list.ToArray();
            EditorPrefs.SetString(RecentPackagesPrefsKey, string.Join(RecentPackagesSeparator, recentPackages));

            RefreshRecentPackages();
        }

        private void ClearRecentPackages()
        {
            EditorPrefs.SetString(RecentPackagesPrefsKey, "");
            RefreshRecentPackages();
        }

        private bool IsPackageLinked(string packageName)
        {
            foreach (var info in directories)
            {
                if (info.name == packageName)
                {
                    return true;
                }
            }

            return false;
        }

        [Serializable]
        private class Package
        {
            public string name;
        }

        [Serializable]
        private class SymlinkedDirInfo
        {
            public string path;
            public string name;
            public string sourcePath;
        }
    }
}
#endif
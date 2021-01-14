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
    public class PackageSymLinkerWindow : EditorWindow
    {
        [MenuItem("Tools/Package Symlinker")]
        public static void OpenWindow()
        {
            var window = GetWindow<PackageSymLinkerWindow>();
            window.Show();
        }

        [SerializeField] private List<SymlinkedDirInfo> directories = new List<SymlinkedDirInfo>();
        [SerializeField] private Vector2 scroll;

        private void OnEnable()
        {
            Reload();
        }

        private void OnFocus()
        {
            Reload();
        }

        private void OnGUI()
        {
            OnToolbarGUI();
            OnContentGUI();
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

        private void OnContentGUI()
        {
            if (directories.Count == 0)
            {
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("No symbolic linked packages in project", EditorStyles.largeLabel);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
            }

            scroll = GUILayout.BeginScrollView(scroll, false, true);
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

            EditorGUILayout.EndScrollView();
        }

        private void Reload()
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

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Reload();
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
            Reload();
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
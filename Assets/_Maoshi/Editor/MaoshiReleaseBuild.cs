using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Релизная сборка для Steam (меню Maoshi → Build):
    /// - Optimize Assets For Release — текстуры AnimX (мех 4K на каждую породу) не больше 2048 и сжаты: сборка заметно меньше,
    ///   на вид разницы нет (у котов свои мультяшные окраски 2048, мех по умолчанию выключен).
    /// - Release Build (Steam) — Windows x64 без Development, IL2CPP (если модуль установлен, иначе Mono), сжатие LZ4HC,
    ///   полноэкранный режим по умолчанию, отчёт о размере (Builds/Release_report.txt: что больше всего весит).
    ///   Служебные папки «…DoNotShip» / «…ButDontShipItWithYourGame» выносятся из папки игры — в Steam их заливать нельзя.
    /// Тестовая сборка (Maoshi → Test) остаётся как была — Development, Mono.
    /// </summary>
    public static class MaoshiReleaseBuild
    {
        private const string ReleaseFolder = "Builds/Release";
        private const string ExtrasFolder = "Builds/Release_DoNotShip";
        private const string ReportPath = "Builds/Release_report.txt";
        private const int MaxTextureSize = 2048;

        [MenuItem("Maoshi/Build/Optimize Assets For Release")]
        public static void OptimizeAssets()
        {
            int changed = 0;
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/AnimX Animals", "Assets/ThirdParty", "Assets/_Maoshi" });
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                    // Интерфейс (Resources/Maoshi/UI) не трогаем — там важна чёткость и размеры маленькие.
                    if (path.Contains("/Resources/Maoshi/UI") || path.Contains("/UI/")) continue;
                    if (EditorUtility.DisplayCancelableProgressBar("Оптимизация текстур", path, i / (float)guids.Length)) break;

                    bool dirty = false;
                    if (importer.maxTextureSize > MaxTextureSize)
                    {
                        importer.maxTextureSize = MaxTextureSize;
                        dirty = true;
                    }
                    if (importer.textureCompression == TextureImporterCompression.Uncompressed && importer.textureType != TextureImporterType.GUI)
                    {
                        importer.textureCompression = TextureImporterCompression.Compressed;
                        dirty = true;
                    }

                    if (!dirty) continue;
                    importer.SaveAndReimport();
                    changed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[Maoshi Build] Оптимизация текстур: изменено {changed} из {guids.Length} (не больше {MaxTextureSize}, сжатие).");
        }

        /// <summary>Поднять версию (0.1.0 → 0.1.1). Игроки с разными версиями не смогут зайти друг к другу — это нарочно.</summary>
        [MenuItem("Maoshi/Build/Bump Version (patch)")]
        public static void BumpVersion()
        {
            var parts = PlayerSettings.bundleVersion.Split('.');
            if (parts.Length < 3 || !int.TryParse(parts[2], out int patch))
            {
                Debug.LogError($"[Maoshi Build] Не понял версию «{PlayerSettings.bundleVersion}».");
                return;
            }
            PlayerSettings.bundleVersion = $"{parts[0]}.{parts[1]}.{patch + 1}";
            AssetDatabase.SaveAssets();
            Debug.Log($"[Maoshi Build] Версия: {PlayerSettings.bundleVersion}");
        }

        [MenuItem("Maoshi/Build/Release Build (Steam)")]
        public static void BuildRelease()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Maoshi Build] В Build Settings нет сцен.");
                return;
            }

            var target = NamedBuildTarget.Standalone;
            var previousBackend = PlayerSettings.GetScriptingBackend(target);
            var previousStripping = PlayerSettings.GetManagedStrippingLevel(target);
            bool il2cpp = Il2CppInstalled();

            // Настройки, которые нужны релизу (и не мешают тестовой сборке).
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.runInBackground = true;          // сетевая игра не должна замирать в фоне
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.usePlayerLog = true;             // Player.log нужен для разбора ошибок игроков
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.mipStripping = true;
            PlayerSettings.gcIncremental = true;

            try
            {
                // Low — безопасно для Netcode (код RPC генерируется при сборке, рефлексия почти не нужна).
                PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Low);

                var report = il2cpp ? BuildOnce(scenes, target, true) : null;
                if (report == null || report.summary.result != BuildResult.Succeeded)
                {
                    if (il2cpp)
                        Debug.LogWarning("[Maoshi Build] IL2CPP не собрался (нужны Visual Studio 2022 с «Разработка классических приложений на C++» и Windows SDK) — собираю на Mono.");
                    il2cpp = false;
                    report = BuildOnce(scenes, target, false);
                }

                if (report.summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"[Maoshi Build] Релизная сборка не удалась: {report.summary.result}, ошибок {report.summary.totalErrors}.");
                    return;
                }

                MoveDoNotShip();
                WriteSteamAppId(ReleaseFolder);
                WriteReport(report, il2cpp);
                EditorUtility.RevealInFinder(Path.Combine(ReleaseFolder, "Maoshi.exe"));
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, previousBackend);
                PlayerSettings.SetManagedStrippingLevel(target, previousStripping);
            }
        }

        private static BuildReport BuildOnce(string[] scenes, NamedBuildTarget target, bool il2cpp)
        {
            PlayerSettings.SetScriptingBackend(target, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            if (il2cpp) PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Release);

            if (Directory.Exists(ReleaseFolder)) Directory.Delete(ReleaseFolder, true);
            Directory.CreateDirectory(ReleaseFolder);

            return BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(ReleaseFolder, "Maoshi.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.CompressWithLz4HC,
            });
        }

        /// <summary>
        /// steam_appid.txt рядом с exe — для запуска игры из папки (не из Steam) при тестах: Steam узнаёт игру сразу.
        /// Только пока номер тестовый (480): у своей игры файл не нужен — её запускает Steam, а из папки игра
        /// перезапустится через Steam сама (RestartAppIfNecessary).
        /// </summary>
        public static void WriteSteamAppId(string folder)
        {
            string path = Path.Combine(folder, "steam_appid.txt");
            if (Maoshi.Network.SteamService.IsTestAppId)
                File.WriteAllText(path, Maoshi.Network.SteamService.AppId.ToString());
            else if (File.Exists(path))
                File.Delete(path);
        }

        private static bool Il2CppInstalled()
        {
            string root = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "windowsstandalonesupport", "Variations");
            return Directory.Exists(root) && Directory.GetDirectories(root).Any(d => d.IndexOf("il2cpp", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Отладочные символы IL2CPP/Burst — не для игроков: переносим рядом (пригодятся разбирать падения).</summary>
        private static void MoveDoNotShip()
        {
            if (Directory.Exists(ExtrasFolder)) Directory.Delete(ExtrasFolder, true);
            foreach (string dir in Directory.GetDirectories(ReleaseFolder))
            {
                string name = Path.GetFileName(dir);
                if (!name.Contains("DoNotShip") && !name.Contains("DontShip")) continue;
                Directory.CreateDirectory(ExtrasFolder);
                Directory.Move(dir, Path.Combine(ExtrasFolder, name));
            }
        }

        private static void WriteReport(BuildReport report, bool il2cpp)
        {
            long folderBytes = Directory.GetFiles(ReleaseFolder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

            var assets = new Dictionary<string, ulong>();
            foreach (var packed in report.packedAssets)
            foreach (var info in packed.contents)
            {
                string path = string.IsNullOrEmpty(info.sourceAssetPath) ? "(встроенное)" : info.sourceAssetPath;
                assets.TryGetValue(path, out ulong size);
                assets[path] = size + info.packedSize;
            }

            var byFolder = assets.GroupBy(a => TopFolder(a.Key)).Select(g => (g.Key, g.Aggregate(0UL, (s, a) => s + a.Value)))
                .OrderByDescending(x => x.Item2).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Maoshi {PlayerSettings.bundleVersion} — релизная сборка {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Бэкенд: {(il2cpp ? "IL2CPP" : "Mono (для IL2CPP нужен Visual Studio с C++)")}, сжатие LZ4HC, без Development");
            sb.AppendLine($"Папка игры: {Mb(folderBytes)}  (до сжатия Steam; загрузка для игроков будет меньше)");
            sb.AppendLine($"Время сборки: {report.summary.totalTime:mm\\:ss}");
            sb.AppendLine();
            sb.AppendLine("По папкам проекта:");
            foreach (var (folder, size) in byFolder.Take(12)) sb.AppendLine($"  {Mb((long)size),10}  {folder}");
            sb.AppendLine();
            sb.AppendLine("40 самых тяжёлых ассетов:");
            foreach (var pair in assets.OrderByDescending(a => a.Value).Take(40))
                sb.AppendLine($"  {Mb((long)pair.Value),10}  {pair.Key}");

            File.WriteAllText(ReportPath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[Maoshi Build] Готово: {ReleaseFolder}/Maoshi.exe, {Mb(folderBytes)}. Отчёт о размере — {ReportPath}.");
        }

        private static string TopFolder(string path)
        {
            var parts = path.Split('/');
            if (parts.Length >= 3 && parts[0] == "Assets") return parts[0] + "/" + parts[1] + "/" + parts[2];
            return parts.Length >= 2 ? parts[0] + "/" + parts[1] : path;
        }

        private static string Mb(long bytes) => $"{bytes / 1048576f:0.0} МБ";
    }
}

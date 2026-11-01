using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Тест на одном компьютере без «10 FPS во втором окне»:
    /// собираем обычную Windows-версию игры и запускаем её рядом с редактором.
    /// Билд — хост (или клиент), редактор — второй игрок (режим «Локальная сеть», адрес 127.0.0.1).
    /// Меню: Maoshi → Test → ...
    /// </summary>
    public static class MaoshiTestBuild
    {
        private const string BuildFolder = "Builds/Test";
        private static string ExePath => Path.GetFullPath(Path.Combine(BuildFolder, "Maoshi.exe"));

        [MenuItem("Maoshi/Test/Build And Run Test Copy")]
        public static void BuildAndRun()
        {
            if (Build()) RunCopy();
        }

        [MenuItem("Maoshi/Test/Build Test Copy")]
        public static void BuildOnly() => Build();

        [MenuItem("Maoshi/Test/Run Another Test Copy")]
        public static void RunCopy() => RunCopy("");

        /// <summary>Запустить копию с параметрами (например «-maoshiJoin КОД»).</summary>
        public static void RunCopy(string extraArgs)
        {
            if (!File.Exists(ExePath))
            {
                Debug.LogError("[Maoshi Test] Билда ещё нет. Сначала Maoshi → Test → Build Test Copy.");
                return;
            }

            // Окно, а не полный экран — удобно держать рядом с редактором.
            Process.Start(new ProcessStartInfo
            {
                FileName = ExePath,
                Arguments = "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 " + extraArgs,
                WorkingDirectory = Path.GetDirectoryName(ExePath),
                // Через оболочку: иначе копия наследует сокеты редактора (порт MCP 8090, порт поиска игр) и держит их.
                UseShellExecute = true,
            });
            Debug.Log("[Maoshi Test] Запущена тестовая копия: " + ExePath);
        }

        private static bool Build()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Maoshi Test] В Build Settings нет сцен. Запусти Maoshi → Setup → Stage 1.");
                return false;
            }

            Directory.CreateDirectory(BuildFolder);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = ExePath,
                target = BuildTarget.StandaloneWindows64,
                // Development: видна консоль ошибок в углу и можно подключить профайлер.
                options = BuildOptions.Development,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[Maoshi Test] Сборка не удалась: {report.summary.result}, ошибок: {report.summary.totalErrors}.");
                return false;
            }

            MaoshiReleaseBuild.WriteSteamAppId(Path.GetDirectoryName(ExePath));
            Debug.Log($"[Maoshi Test] Сборка готова за {report.summary.totalTime.TotalSeconds:F0} с: {ExePath}");
            return true;
        }
    }
}

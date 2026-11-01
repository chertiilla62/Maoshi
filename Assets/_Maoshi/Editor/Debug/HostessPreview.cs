using System.IO;
using Maoshi.EditorTools.Art;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>Отладка: хозяйка в сцене SausageKitchen с кадрами idle и run — снимки в Temp/Captures (сцена не сохраняется).</summary>
    public static class HostessPreview
    {
        [MenuItem("Maoshi/Art/Preview Hostess")]
        public static void Preview()
        {
            foreach (var clipName in new[] { "idle", "run" })
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene($"{MaoshiSetup.ScenesFolder}/SausageKitchen.unity");
                AnimationClip clip = null;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath($"{KenneyImportSettings.Root}/Characters/Animations/{clipName}.fbx"))
                    if (asset is AnimationClip c && !c.name.StartsWith("__preview__")) clip = c;
                var character = GameObject.Find("Hostess/Body/Character");
                if (character != null && clip != null) clip.SampleAnimation(character, 0.3f);
                Debug.Log($"[Maoshi Art] Хозяйка {clipName}: клип {(clip != null ? clip.name + " " + clip.length.ToString("0.00") : "нет")}, персонаж {(character != null ? "есть" : "нет")}");
                File.WriteAllLines("Temp/capture.txt", new[] { $"hostess_{clipName} 2.6 1.2 4.6 5 -90 45" });
                SceneCapture.Capture();
            }
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath);
        }

    }
}

using System.IO;
using Maoshi.EditorTools.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>Отладка: кадры клипов поворота на месте на префабе кота (новая пустая сцена, не сохраняется).</summary>
    public static class CatTurnPreview
    {
        [MenuItem("Maoshi/Art/Preview Cat Turn")]
        public static void Preview()
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaoshiSetupStage2.CatPrefabPath);
            string[] clips = { "Turn-90_L", "Turn-45_L", "Run_F" };
            float[] times = { 0f, 0.3f, 0.6f };
            var lines = new System.Collections.Generic.List<string>();
            int k = 0;
            foreach (var name in clips)
            {
                AnimationClip clip = null;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/AnimX Animals/Cats/Animations/A_Cat_Move.fbx"))
                    if (asset is AnimationClip c && c.name == name) clip = c;
                for (int i = 0; i < times.Length; i++)
                {
                    var cat = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    cat.transform.position = new Vector3(k * 2f, 0f, 0f);
                    var animator = cat.GetComponentInChildren<Animator>();
                    if (clip != null && animator != null) clip.SampleAnimation(animator.gameObject, times[i] * clip.length);
                    Debug.Log($"[Maoshi Art] {name}: длина {(clip != null ? clip.length.ToString("0.00") : "нет")}, loop {(clip != null && clip.isLooping)}");
                    k++;
                }
            }
            float mid = (k - 1) * 1f;
            lines.Add($"turn_side {mid} 1.2 -7 8 0 60");
            lines.Add($"turn_top {mid} 9 0 89 0 60");
            File.WriteAllLines("Temp/capture.txt", lines);
            SceneCapture.Capture();
        }
    }
}

using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools.Art
{
    /// <summary>
    /// Снимки открытой сцены с заданных точек — чтобы проверять оформление без ручного облёта.
    /// Точки берутся из Temp/capture.txt: «имя x y z pitch yaw fov» в строке. Картинки — Temp/Captures/имя.png (1280×720).
    /// </summary>
    public static class SceneCapture
    {
        /// <summary>Снимки нескольких сцен: строки «Сцена имя x y z pitch yaw fov» в Temp/capture_scenes.txt.</summary>
        [MenuItem("Maoshi/Art/Capture Scenes")]
        public static void CaptureScenes()
        {
            const string list = "Temp/capture_scenes.txt";
            if (!File.Exists(list)) return;
            var byScene = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
            foreach (var raw in File.ReadAllLines(list))
            {
                var parts = raw.Trim().Split(' ', 2, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[0].StartsWith("#")) continue;
                if (!byScene.TryGetValue(parts[0], out var lines)) byScene[parts[0]] = lines = new System.Collections.Generic.List<string>();
                lines.Add(parts[0] + "_" + parts[1]);
            }

            foreach (var pair in byScene)
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene($"{MaoshiSetup.ScenesFolder}/{pair.Key}.unity");
                File.WriteAllLines("Temp/capture.txt", pair.Value);
                Capture();
            }
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath);
        }

        /// <summary>Проверка анимаций дворника: в сцене JanitorEscape проиграть кадр бега и снять (сцена не сохраняется).</summary>
        [MenuItem("Maoshi/Art/Preview Janitor Run")]
        public static void PreviewJanitor()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene($"{MaoshiSetup.ScenesFolder}/JanitorEscape.unity");
            AnimationClip run = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(KenneyImportSettings.Root + "/Characters/Animations/run.fbx"))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) run = clip;
            // Три дворника — три фазы: бег с метлой, замах, удар.
            var states = new[] { Maoshi.MiniGames.JanitorEscape.Janitor.JanitorState.Chase, Maoshi.MiniGames.JanitorEscape.Janitor.JanitorState.Windup, Maoshi.MiniGames.JanitorEscape.Janitor.JanitorState.Recover };
            var poses = new[] { new Vector3(38f, -22f, -10f), new Vector3(-125f, -25f, -15f), new Vector3(75f, 50f, -10f) };
            int i = 0;
            foreach (var rig in Object.FindObjectsByType<Maoshi.MiniGames.JanitorEscape.JanitorRig>(FindObjectsSortMode.None))
            {
                var character = rig.transform.Find("Body/Character");
                if (character != null && run != null) run.SampleAnimation(character.gameObject, 0.15f + 0.2f * i);
                var broom = rig.transform.Find("Body/BroomPivot");
                if (broom != null) broom.localRotation = Quaternion.Euler(poses[i % 3]);
                rig.Apply(states[i % 3], 0.1f, 0f);
                i++;
            }
            File.WriteAllLines("Temp/capture.txt", new[] { "janitor_run -6.5 1.6 6 8 -60 45" });
            Capture();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath);
            Debug.Log($"[Maoshi Art] Клип бега: {(run != null ? run.name + " " + run.length.ToString("0.00") + " c" : "нет")}");
        }

        [MenuItem("Maoshi/Art/Debug Janitor Rig")]
        public static void DebugRig()
        {
            var sb = new System.Text.StringBuilder("[Maoshi Art] Кривые run:\n");
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(KenneyImportSettings.Root + "/Characters/Animations/run.fbx"))
            {
                if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
                foreach (var b in AnimationUtility.GetCurveBindings(clip)) if (b.propertyName.EndsWith(".x")) sb.AppendLine(b.path + " :: " + b.propertyName);
            }
            sb.AppendLine("Модель:");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(KenneyImportSettings.CharacterModel);
            foreach (var t in model.GetComponentsInChildren<Transform>())
                sb.AppendLine(AnimationUtility.CalculateTransformPath(t, model.transform) + "  rot " + t.localEulerAngles + " pos " + t.localPosition + " scale " + t.localScale);
            Debug.Log(sb.ToString());
        }

        /// <summary>В игре: снимок своего кота крупным планом сбоку и спереди (Temp/Captures/cat_side.png, cat_front.png).</summary>
        [MenuItem("Maoshi/Art/Capture Cat Closeup")]
        public static void CaptureCat()
        {
            var cat = Object.FindFirstObjectByType<Maoshi.Cat.CatController>();
            if (cat == null) { Debug.LogWarning("[Maoshi Art] Кота нет"); return; }
            Vector3 p = cat.transform.position + Vector3.up * 0.45f;
            Vector3 side = cat.transform.right * 2.2f + Vector3.up * 0.5f;
            Vector3 front = cat.transform.forward * 2.2f + cat.transform.right * 0.6f + Vector3.up * 0.4f;
            string Line(string name, Vector3 offset)
            {
                Vector3 c = p + offset;
                var rot = Quaternion.LookRotation(p - c).eulerAngles;
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4} {5} 40", name, c.x, c.y, c.z, rot.x, rot.y);
            }
            File.WriteAllLines("Temp/capture.txt", new[] { Line("cat_side", side), Line("cat_front", front) });
            Capture();
        }

        private static int _janitorShot;

        /// <summary>В Play: снимок дворников сбоку (номер кадра растёт, чтобы снять серию).</summary>
        [MenuItem("Maoshi/Art/Capture Janitor Live")]
        public static void CaptureJanitorLive()
        {
            var janitors = Object.FindObjectsByType<Maoshi.MiniGames.JanitorEscape.Janitor>(FindObjectsSortMode.None);
            if (janitors.Length == 0) { Debug.LogWarning("[Maoshi Art] Дворника нет"); return; }
            var lines = new System.Collections.Generic.List<string>();
            for (int i = 0; i < janitors.Length && i < 2; i++)
            {
                var j = janitors[i].transform;
                Vector3 p = j.position + Vector3.up * 1.0f;
                Vector3 c = p + j.right * 4f + j.forward * 1.5f + Vector3.up * 0.4f;
                var rot = Quaternion.LookRotation(p - c).eulerAngles;
                lines.Add(string.Format(CultureInfo.InvariantCulture, "jan{0}_{1}_{2} {3} {4} {5} {6} {7} 45",
                    _janitorShot, i, janitors[i].State.Value, c.x, c.y, c.z, rot.x, rot.y));
            }
            _janitorShot++;
            File.WriteAllLines("Temp/capture.txt", lines);
            Capture();
        }

        /// <summary>Все окраски котов рядом (в открытой сцене, на высоте 300 м над двором, потом убираются).</summary>
        [MenuItem("Maoshi/Art/Capture Cat Skins")]
        public static void CaptureCatSkins()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaoshiSetupStage2.CatPrefabPath);
            var appearance = prefab != null ? prefab.GetComponent<Maoshi.Cat.CatAppearance>() : null;
            if (appearance == null || appearance.Model == null) { Debug.LogWarning("[Maoshi Art] Нет префаба кота"); return; }

            var root = new GameObject("SkinLineup") { hideFlags = HideFlags.DontSave };
            Vector3 origin = new(0f, 300f, 0f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(root.transform);
            ground.transform.position = origin;
            ground.transform.localScale = new Vector3(3f, 1f, 3f);
            var groundMat = new Material(Shader.Find(MaoshiSetupStage18.ToonShader)) { color = new Color(0.78f, 0.80f, 0.74f) };
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            int count = appearance.SkinCount;
            const float spacing = 1.1f;
            for (int i = 0; i < count; i++)
            {
                int row = i / 6, col = i % 6;
                var model = Object.Instantiate(appearance.Model, root.transform);
                model.position = origin + new Vector3((col - 2.5f) * spacing, 0f, row * 1.4f);
                model.rotation = Quaternion.Euler(0f, 90f, 0f);
                appearance.ApplySkinTo(model, i);
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    if (r.name.Contains("_Fur_")) r.enabled = false;
            }

            string L(string name, Vector3 cam, Vector3 target, float fov)
            {
                var rot = Quaternion.LookRotation(target - cam).eulerAngles;
                return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4} {5} {6}", name, cam.x, cam.y, cam.z, rot.x, rot.y, fov);
            }
            var lines = new[]
            {
                L("skins_side", origin + new Vector3(0f, 1.6f, -5.2f), origin + new Vector3(0f, 0.2f, 0.7f), 42f),
                L("skins_top", origin + new Vector3(0f, 4.8f, 0.7f), origin + new Vector3(0f, 0f, 0.7f), 60f),
                L("skins_front", origin + new Vector3(5.8f, 1.2f, 0.7f), origin + new Vector3(0f, 0.3f, 0.7f), 34f),
                L("skins_back", origin + new Vector3(-5.8f, 1.2f, 0.7f), origin + new Vector3(0f, 0.3f, 0.7f), 34f),
            };
            File.WriteAllLines("Temp/capture.txt", lines);
            Capture();
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(groundMat);
        }

        [MenuItem("Maoshi/Art/Capture Views")]
        public static void Capture()
        {
            const string list = "Temp/capture.txt";
            if (!File.Exists(list))
            {
                Debug.LogWarning("[Maoshi Art] Нет Temp/capture.txt");
                return;
            }

            Directory.CreateDirectory("Temp/Captures");
            var go = new GameObject("CaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 600f;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = rt;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);

            int count = 0;
            foreach (var raw in File.ReadAllLines(list))
            {
                var parts = raw.Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 7 || parts[0].StartsWith("#")) continue;
                float F(int i) => float.Parse(parts[i], CultureInfo.InvariantCulture);
                go.transform.SetPositionAndRotation(new Vector3(F(1), F(2), F(3)), Quaternion.Euler(F(4), F(5), 0f));
                camera.fieldOfView = F(6);
                camera.Render();
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                texture.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes($"Temp/Captures/{parts[0]}.png", texture.EncodeToPNG());
                count++;
            }

            camera.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(go);
            Debug.Log($"[Maoshi Art] Снимков: {count} → Temp/Captures");
        }
    }
}

using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 20: дизайн интерфейса «двор-стикер».
    /// - Картинки интерфейса (Resources/Maoshi/UI): значки карт, лапки жизней, скотч, мел — настройки импорта для UI.
    /// - Иконка игры (лапка) в Player Settings.
    /// - Фон меню: снимок двора из сцены Hub (Resources/Maoshi/UI/MenuBackdrop.png).
    /// - Шрифты: если в Assets/_Maoshi/UI/Fonts лежат Dela Gothic One, Nunito и Neucha — собирает Resources/Maoshi/MaoshiFonts.uss.
    ///   Положил шрифты позже — просто запусти этап ещё раз.
    /// Запуск: Maoshi → Setup → Stage 20 - Sticker UI.
    /// </summary>
    public static class MaoshiSetupStage20
    {
        private const string UiImages = "Assets/_Maoshi/Resources/Maoshi/UI";
        private const string IconPath = "Assets/_Maoshi/Art/Icon/AppIcon.png";
        private const string IconSquarePath = "Assets/_Maoshi/Art/Icon/AppIconSquare.png";
        private const string FontsFolder = "Assets/_Maoshi/UI/Fonts";
        private const string FontSheetPath = "Assets/_Maoshi/Resources/Maoshi/MaoshiFonts.uss";

        [MenuItem("Maoshi/Setup/Stage 20 - Sticker UI")]
        public static void Setup()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogError("[Maoshi Setup] Есть несохранённые изменения в открытой сцене. Сохрани её и запусти снова.");
                    return;
                }
            }

            ConfigureImages();
            SetupIcon();
            CaptureMenuBackdrop();
            string fonts = SetupFonts();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Maoshi Setup] Этап 20 готов: дизайн «двор-стикер», иконка-лапка, фон меню. " + fonts);
        }

        private static void ConfigureImages()
        {
            if (!AssetDatabase.IsValidFolder(UiImages)) return;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { UiImages }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
        }

        private static void SetupIcon()
        {
            foreach (var path in new[] { IconPath, IconSquarePath })
            {
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogWarning("[Maoshi Setup] Нет иконки " + IconPath);
                return;
            }
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            int sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any).Length;
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, Enumerable.Repeat(icon, Mathf.Max(1, sizes)).ToArray(), IconKind.Any);
        }

        /// <summary>Снимок двора камерой сцены Hub — фон для меню и лобби.</summary>
        private static void CaptureMenuBackdrop()
        {
            string current = SceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.OpenScene(MaoshiSetup.HubScenePath, OpenSceneMode.Single);
            var source = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            if (source == null)
            {
                Debug.LogWarning("[Maoshi Setup] В Hub нет камеры для фона меню.");
                return;
            }

            var go = new GameObject("BackdropCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(source);
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            // Чуть выше и шире, чем игровая камера: видно двор и дома.
            go.transform.SetPositionAndRotation(source.transform.position + Vector3.up * 2f, Quaternion.Euler(Mathf.Max(18f, source.transform.eulerAngles.x), source.transform.eulerAngles.y, 0f));
            camera.fieldOfView = 55f;
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(UiImages);
            File.WriteAllBytes(UiImages + "/MenuBackdrop.jpg", texture.EncodeToJPG(88));
            camera.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(go);

            AssetDatabase.ImportAsset(UiImages + "/MenuBackdrop.jpg");
            if (AssetImporter.GetAtPath(UiImages + "/MenuBackdrop.jpg") is TextureImporter importer)
            {
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }

            if (!string.IsNullOrEmpty(current) && current != scene.path) EditorSceneManager.OpenScene(current, OpenSceneMode.Single);
        }

        /// <summary>Стили шрифтов по тем файлам, что лежат в UI/Fonts. Возвращает строку для лога.</summary>
        private static string SetupFonts()
        {
            string display = Find("dela");
            string body = Find("nunito-extrabold") ?? Find("nunito-black") ?? Find("nunito");
            string chalk = Find("neucha");

            if (display == null && body == null && chalk == null)
            {
                if (File.Exists(FontSheetPath)) AssetDatabase.DeleteAsset(FontSheetPath);
                return "Шрифтов в " + FontsFolder + " нет — интерфейс на системном шрифте.";
            }

            static string Url(string path) => $"url(\"project://database/{path}\")";
            var uss = new StringBuilder("/* Шрифты дизайна. Собирает Maoshi → Setup → Stage 20 по файлам в UI/Fonts. */\n\n");
            if (body != null)
                uss.Append(".maoshi-root {\n    -unity-font-definition: ").Append(Url(body)).Append(";\n}\n\n");
            if (display != null)
                uss.Append(".title, .h1, .h2, .btn--primary, .btn--secondary, .banner__title, .big-center, .brawl-title, .brawl-timer, ")
                   .Append(".score-value, .seat__name, .bcard__name, .bcard__badge, .skin-name, .skin-arrow, .selector__arrow, .keycap, .tab--active {\n")
                   .Append("    -unity-font-definition: ").Append(Url(display)).Append(";\n    -unity-font-style: normal;\n}\n\n");
            if (chalk != null)
                uss.Append(".chalk {\n    -unity-font-definition: ").Append(Url(chalk)).Append(";\n    -unity-font-style: normal;\n}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(FontSheetPath));
            File.WriteAllText(FontSheetPath, uss.ToString());
            AssetDatabase.ImportAsset(FontSheetPath);
            return $"Шрифты: заголовки {(display != null ? "Dela Gothic One" : "—")}, текст {(body != null ? Path.GetFileNameWithoutExtension(body) : "—")}, мел {(chalk != null ? "Neucha" : "—")}.";
        }

        private static string Find(string namePart)
        {
            if (!AssetDatabase.IsValidFolder(FontsFolder)) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { FontsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (name.Contains(namePart) && !name.Contains("italic")) return path;
            }
            return null;
        }
    }
}

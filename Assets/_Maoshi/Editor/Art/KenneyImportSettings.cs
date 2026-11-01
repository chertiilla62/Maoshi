using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools.Art
{
    /// <summary>
    /// Настройки импорта моделей Kenney (Assets/ThirdParty/Kenney):
    /// - декор: без анимаций и камер, материалы из FBX (цвета) — Unity сам переводит их на URP Lit;
    /// - персонаж: Generic-риг, анимации берут аватар у модели, idle/run зациклены.
    /// </summary>
    public class KenneyImportSettings : AssetPostprocessor
    {
        public const string Root = "Assets/ThirdParty/Kenney";
        public const string CharacterModel = Root + "/Characters/characterMedium.fbx";
        public const string PaletteFolder = "Assets/_Maoshi/Art/Materials/Kenney";

        /// <summary>
        /// Свои цвета вместо бирюзовой палитры Kenney: «пак/имя материала в FBX» → цвет.
        /// Материалы создаёт Maoshi → Art → Apply Kenney Palette, импортёр подменяет ими встроенные.
        /// </summary>
        public static readonly Dictionary<string, Color> Palette = new()
        {
            { "Nature/grass", new Color(0.42f, 0.66f, 0.30f) },
            { "Nature/leafsGreen", new Color(0.40f, 0.64f, 0.30f) },
            { "Nature/leafsDark", new Color(0.25f, 0.48f, 0.26f) },
            { "Nature/woodBark", new Color(0.47f, 0.34f, 0.24f) },
            { "Nature/woodBarkDark", new Color(0.36f, 0.26f, 0.19f) },
            { "Nature/wood", new Color(0.62f, 0.45f, 0.28f) },
            { "Nature/woodDark", new Color(0.44f, 0.31f, 0.20f) },
            { "Nature/woodInner", new Color(0.86f, 0.70f, 0.48f) },
            { "Nature/dirt", new Color(0.50f, 0.38f, 0.27f) },
            { "Graveyard/metal", new Color(0.22f, 0.23f, 0.26f) },
            { "Graveyard/stone", new Color(0.64f, 0.63f, 0.60f) },
            { "Graveyard/wood", new Color(0.60f, 0.42f, 0.26f) },
            { "Graveyard/woodDark", new Color(0.40f, 0.28f, 0.19f) },
            { "Graveyard/brick", new Color(0.66f, 0.36f, 0.28f) },
            { "Platformer/grass", new Color(0.42f, 0.66f, 0.30f) },
            { "Furniture/plant", new Color(0.36f, 0.62f, 0.30f) },
        };

        /// <summary>Светящиеся материалы (плафоны фонарей).</summary>
        public static readonly Dictionary<string, Color> Glow = new()
        {
            { "Graveyard/light", new Color(1f, 0.85f, 0.55f) },
        };

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (ModelImporter)assetImporter;

            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;

            if (assetPath.Contains("/Characters/"))
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                if (assetPath.Contains("/Animations/"))
                {
                    importer.importAnimation = true;
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModel);
                    var avatar = source != null ? AssetDatabase.LoadAllAssetsAtPath(CharacterModel).OfType<Avatar>().FirstOrDefault() : null;
                    if (avatar != null)
                    {
                        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                        importer.sourceAvatar = avatar;
                    }
                }
                else
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                }
                return;
            }

            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = false;

            // Подмена цветов палитры.
            string pack = assetPath.Substring(Root.Length + 1).Split('/')[0];
            foreach (var key in Palette.Keys.Concat(Glow.Keys))
            {
                if (!key.StartsWith(pack + "/")) continue;
                string materialName = key.Substring(pack.Length + 1);
                var material = AssetDatabase.LoadAssetAtPath<Material>(PalettePath(key));
                if (material != null) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), materialName), material);
            }
        }

        public static string PalettePath(string key) => $"{PaletteFolder}/K_{key.Replace('/', '_')}.mat";

        [MenuItem("Maoshi/Art/Apply Kenney Palette")]
        public static void ApplyPalette()
        {
            MaoshiSetup.EnsureFolder(PaletteFolder);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var pair in Palette.Concat(Glow))
            {
                string path = PalettePath(pair.Key);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(lit);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.SetColor("_BaseColor", pair.Value);
                material.SetFloat("_Smoothness", 0.1f);
                if (Glow.ContainsKey(pair.Key))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", pair.Value * 1.5f);
                }
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(Root, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("[Maoshi Art] Палитра Kenney применена.");
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Root + "/Characters/Animations/")) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            bool loop = !assetPath.EndsWith("jump.fbx");
            foreach (var clip in clips)
            {
                clip.loopTime = loop;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        /// <summary>
        /// В анимациях Kenney корень клипа — сам скелет «Root», а в модели кости лежат под Root/.
        /// Переносим кривые костей на путь «Root/…», а кривые самого корня (поворот −90° и масштаб ×100
        /// из Blender) выкидываем — иначе персонаж ложится лицом в асфальт или раздувается до размеров дома.
        /// </summary>
        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (!assetPath.StartsWith(Root + "/Characters/Animations/")) return;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                if (string.IsNullOrEmpty(binding.path) || binding.path.StartsWith("Root")) continue;
                var moved = binding;
                moved.path = "Root/" + binding.path;
                AnimationUtility.SetEditorCurve(clip, moved, curve);
            }
        }

        /// <summary>Встроенные материалы моделей — сразу на мультяшный шейдер (если он уже есть в проекте).</summary>
        private void OnPostprocessMaterial(Material material)
        {
            if (!assetPath.StartsWith(Root)) return;
            var toon = Shader.Find(MaoshiSetupStage18.ToonShader);
            if (toon == null || !MaoshiSetupStage18.IsLit(material.shader)) return;
            MaoshiSetupStage18.ConvertMaterial(material, toon);
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root) && !assetPath.StartsWith("Assets/_Maoshi/Resources/Maoshi") && !assetPath.StartsWith("Assets/_Maoshi/Art")) return;
            var importer = (TextureImporter)assetImporter;
            if (assetPath.Contains("/Resources/Maoshi/"))
            {
                // Иконки интерфейса: без мипмапов, с прозрачностью.
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
            }
        }

        [MenuItem("Maoshi/Art/Reimport Kenney")]
        public static void Reimport()
        {
            // Сначала модель персонажа (её аватар нужен анимациям), потом всё остальное.
            AssetDatabase.ImportAsset(CharacterModel, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(Root, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset("Assets/_Maoshi/Resources/Maoshi", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("[Maoshi Art] Kenney переимпортирован.");
        }

        [MenuItem("Maoshi/Art/Report Kenney Models")]
        public static void Report()
        {
            var report = new StringBuilder("[Maoshi Art] Размеры моделей Kenney (м):\n");
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) continue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                var bounds = ArtKit.RendererBounds(instance);
                var renderer = instance.GetComponentInChildren<Renderer>();
                string shader = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.shader.name : "-";
                report.AppendLine($"{path.Substring(Root.Length + 1)}: size {bounds.size.x:0.00}×{bounds.size.y:0.00}×{bounds.size.z:0.00} center {bounds.center.x:0.00},{bounds.center.y:0.00},{bounds.center.z:0.00} scale {instance.transform.localScale.x:0.##} shader {shader}");
                Object.DestroyImmediate(instance);
            }
            Debug.Log(report.ToString());
        }
    }
}

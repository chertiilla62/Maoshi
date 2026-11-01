using System.Collections.Generic;
using Maoshi.Cat;
using Maoshi.EditorTools.Art;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 19: окраски котов на выбор.
    /// - 12 мультяшных окрасов (Art/Textures/Cats/CatSkin_*.png, нарисованы по развёртке AnimX) → материалы Maoshi/Toon.
    /// - Массив окрасов в CatAppearance префаба кота в порядке CatSkins.All; глаза и мех — из ближайшей породы AnimX.
    /// - Обновляет настройки контуров (устойчивый к ряби шейдер).
    /// Запуск: Maoshi → Setup → Stage 19 - Cat Skins. Можно перезапускать.
    /// </summary>
    public static class MaoshiSetupStage19
    {
        public const string TexturesFolder = ArtKit.ArtFolder + "/Textures/Cats";
        private const string MaterialsFolder = ArtKit.MaterialsFolder + "/CatSkins";
        private const string AnimXMaterials = "Assets/AnimX Animals/Cats/Materials";

        // Порода AnimX, чьи глаза и мех подходят к окрасу (в порядке CatSkins.All).
        private static readonly Dictionary<string, string> Breed = new()
        {
            { "Ginger", "Red" }, { "Black", "Black" }, { "White", "White" }, { "GrayTabby", "Gray" },
            { "Tuxedo", "AmericanShorthair" }, { "Calico", "Somali" }, { "Siamese", "British" }, { "BlueBritish", "British" },
            { "Cream", "Somali" }, { "Chocolate", "Brown" }, { "Leopard", "Bengal" }, { "GingerWhite", "Red" },
        };

        private static readonly Dictionary<string, string> FurBreed = new()
        {
            { "Ginger", "Red" }, { "Black", "Black" }, { "White", "White" }, { "GrayTabby", "Gray" },
            { "Tuxedo", "Black" }, { "Calico", "White" }, { "Siamese", "Somali" }, { "BlueBritish", "British" },
            { "Cream", "Somali" }, { "Chocolate", "Brown" }, { "Leopard", "Bengal" }, { "GingerWhite", "Red" },
        };

        [MenuItem("Maoshi/Setup/Stage 19 - Cat Skins")]
        public static void Setup()
        {
            var toon = Shader.Find(MaoshiSetupStage18.ToonShader);
            if (toon == null)
            {
                Debug.LogError("[Maoshi Setup] Шейдер Maoshi/Toon не найден.");
                return;
            }

            MaoshiSetup.EnsureFolder(MaterialsFolder);
            var skins = new List<CatAppearance.BreedSkin>();
            foreach (var info in CatSkins.All)
            {
                string texturePath = $"{TexturesFolder}/CatSkin_{info.Id}.png";
                ConfigureTexture(texturePath);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null) Debug.LogWarning($"[Maoshi Setup] Нет текстуры {texturePath}");

                string materialPath = $"{MaterialsFolder}/M_CatSkin_{info.Id}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(toon);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                material.shader = toon;
                material.SetTexture("_BaseMap", texture);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Posterize", 0f);
                material.SetFloat("_Saturation", 1.1f);
                material.SetFloat("_RampThreshold", 0.45f);
                material.SetFloat("_RampSmooth", 0.04f);
                material.SetColor("_ShadeColor", new Color(0.66f, 0.62f, 0.82f));
                material.SetColor("_RimColor", new Color(1f, 0.95f, 0.85f, 0.45f));
                EditorUtility.SetDirty(material);

                skins.Add(new CatAppearance.BreedSkin
                {
                    name = info.Name,
                    body = material,
                    eye = FindAnimX("eye", Breed[info.Id]),
                    fur = FindAnimX("fur", FurBreed[info.Id]),
                });
            }
            AssetDatabase.SaveAssets();

            ApplyToPrefab(skins);
            MaoshiSetupStage18.SetupOutline();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Maoshi Setup] Этап 19 готов: окрасов котов {skins.Count}, контуры обновлены.");
        }

        private static void ConfigureTexture(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            bool changed = importer.maxTextureSize != 2048 || !importer.sRGBTexture || importer.textureCompression != TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = 2048;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.anisoLevel = 4;
            if (changed) importer.SaveAndReimport();
        }

        private static Material FindAnimX(string kind, string breed)
        {
            foreach (var guid in AssetDatabase.FindAssets($"M_Cat.{kind}_{breed} t:Material", new[] { AnimXMaterials }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material != null && material.name == $"M_Cat.{kind}_{breed}") return material;
            }
            Debug.LogWarning($"[Maoshi Setup] Не найден материал M_Cat.{kind}_{breed}");
            return null;
        }

        private static void ApplyToPrefab(List<CatAppearance.BreedSkin> skins)
        {
            var root = PrefabUtility.LoadPrefabContents(MaoshiSetupStage2.CatPrefabPath);
            try
            {
                var appearance = root.GetComponent<CatAppearance>();
                if (appearance == null)
                {
                    Debug.LogError("[Maoshi Setup] На префабе кота нет CatAppearance.");
                    return;
                }

                var so = new SerializedObject(appearance);
                var breeds = so.FindProperty("breeds");
                breeds.arraySize = skins.Count;
                for (int i = 0; i < skins.Count; i++)
                {
                    var element = breeds.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("name").stringValue = skins[i].name;
                    element.FindPropertyRelative("body").objectReferenceValue = skins[i].body;
                    element.FindPropertyRelative("fur").objectReferenceValue = skins[i].fur;
                    element.FindPropertyRelative("eye").objectReferenceValue = skins[i].eye;
                }
                so.ApplyModifiedPropertiesWithoutUndo();

                // Окрас по умолчанию прямо в модели (то, что видно до прихода данных игрока).
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null) continue;
                        string name = materials[i].name;
                        if (name.StartsWith("M_Cat.body") || name.StartsWith("M_CatSkin"))
                        {
                            materials[i] = skins[0].body;
                            changed = true;
                        }
                    }
                    if (changed) renderer.sharedMaterials = materials;
                }

                PrefabUtility.SaveAsPrefabAsset(root, MaoshiSetupStage2.CatPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}

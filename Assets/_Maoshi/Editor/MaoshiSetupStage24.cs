using Maoshi.Cat;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 24: живое движение кота.
    /// - Клипы поворота на месте (Turn-45/90 L/R) зацикливаются и встают в дерево бега: кот переступает лапами,
    ///   а не крутится статуей при быстрых нажатиях WASD.
    /// - Аниматор пересобирается этапом 9 (там же новые значения поворота и заноса).
    /// - Наклоны в повороте сильнее.
    /// Код движения (инерция, торможение, разворот юзом, бег «по корпусу») — в CatController/CatVisual.
    /// Запуск: Maoshi → Setup → Stage 24 - Cat Movement.
    /// </summary>
    public static class MaoshiSetupStage24
    {
        private const string MovePath = "Assets/AnimX Animals/Cats/Animations/A_Cat_Move.fbx";

        [MenuItem("Maoshi/Setup/Stage 24 - Cat Movement")]
        public static void Setup()
        {
            LoopTurnClips();
            MaoshiSetupStage9.SetupStage9();
            TunePrefab();
            Debug.Log("[Maoshi Setup] Этап 24 готов: поворот на месте с анимацией, инерция, разворот юзом.");
        }

        private static void LoopTurnClips()
        {
            if (AssetImporter.GetAtPath(MovePath) is not ModelImporter importer) return;
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            bool changed = false;
            foreach (var clip in clips)
            {
                if (!clip.name.StartsWith("Turn-")) continue;
                if (clip.loopTime) continue;
                clip.loopTime = true;
                changed = true;
            }
            if (!changed) return;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        /// <summary>Полупрозрачная мягкая частица пыли (URP Particles/Unlit, альфа-смешивание).</summary>
        private static Material DustMaterial()
        {
            const string texturePath = "Assets/_Maoshi/Art/Textures/DustPuff.png";
            if (AssetImporter.GetAtPath(texturePath) is TextureImporter importer && !importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            string path = "Assets/_Maoshi/Art/Materials/M_CatDust.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) return null;
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void TunePrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(MaoshiSetupStage2.CatPrefabPath);
            try
            {
                var visual = root.GetComponent<CatVisual>();
                if (visual != null)
                {
                    var so = new SerializedObject(visual);
                    var lean = so.FindProperty("maxLean");
                    if (lean != null) lean.floatValue = 18f;
                    var dust = so.FindProperty("dustMaterial");
                    if (dust != null) dust.objectReferenceValue = DustMaterial();
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                var controller = root.GetComponent<CatController>();
                if (controller != null)
                {
                    var so = new SerializedObject(controller);
                    void F(string name, float value) { var p = so.FindProperty(name); if (p != null) p.floatValue = value; }
                    F("skidTurnSpeed", 420f);
                    F("pivotAngle", 55f);
                    F("pivotTurnSpeed", 540f);
                    F("brakeDecelerationFast", 13f);
                    F("maxHeadingOffsetFast", 25f);
                    so.ApplyModifiedPropertiesWithoutUndo();
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

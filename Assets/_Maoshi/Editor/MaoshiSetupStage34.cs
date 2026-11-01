using Maoshi.EditorTools.Art;
using Maoshi.Hub;
using Maoshi.Session;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 34: зоны во дворе заметнее. Флажки пересобираются: выше столб, крупнее флаг и табличка,
    /// пунктирный круг зоны в цвет хозяина и полупрозрачный столб света над флажком (виден через весь двор,
    /// пульсирует, когда зона на аукционе). «Балкон бабушки» стал «Подъездом» — флажок у западного подъезда.
    /// Запуск: Maoshi → Setup → Stage 34 - Zone Markers. Этап 16 строит флажки этим же кодом.
    /// </summary>
    public static class MaoshiSetupStage34
    {
        private const float Floor = ArtLibrary.Floor;

        [MenuItem("Maoshi/Setup/Stage 34 - Zone Markers")]
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

            var scene = EditorSceneManager.OpenScene(MaoshiSetup.HubScenePath, OpenSceneMode.Single);
            var old = GameObject.Find("ZoneMarkers");
            Transform parent = old != null ? old.transform.parent : null;
            if (old != null) Object.DestroyImmediate(old);
            if (parent == null)
            {
                var env = GameObject.Find("Environment");
                parent = env != null ? env.transform : null;
            }

            var g = ArtKit.Group("ZoneMarkers", parent);
            BuildMarkers(g);

            MaoshiSetupStage22.SaveWithNetworkIds(scene, MaoshiSetup.HubScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Maoshi Setup] Этап 34 готов: зоны заметнее, «Подъезд».");
        }

        /// <summary>Все флажки зон (вызывается и из этапа 16).</summary>
        public static void BuildMarkers(Transform g)
        {
            float roof = Floor * 5f;
            Marker(g, 0, new Vector3(4f, roof, 25f), 3.2f);                  // Крыша
            Marker(g, 1, new Vector3(-12.2f, 0f, -12f), 3.6f);              // Помойка
            Marker(g, 2, new Vector3(23.8f, 0f, 21.5f), 3.4f);              // Подвал
            Marker(g, 3, new Vector3(-17.4f, 0f, 21.4f), 3.6f);             // Подъезд (западный)
            Marker(g, 4, new Vector3(25f, 2.72f, 0.5f), 2.8f);              // Гаражи
            Marker(g, 5, new Vector3(-11.8f, 0f, 4.2f), 3.4f);              // Песочница
            Marker(g, 6, new Vector3(3.3f, 0f, 21.3f), 3.4f);               // Лавочка у подъезда
            Marker(g, 7, new Vector3(12.6f, 0f, -12.2f), 3.8f);             // Голубятня
            Marker(g, 8, new Vector3(-30.6f, Floor * 4f + 1.45f, 6.4f), 2.6f); // Чердак
            Marker(g, 9, new Vector3(13.6f, 0f, 3.6f), 3.4f);               // Детская площадка
        }

        private static Material BeamMaterial()
        {
            string path = ArtKit.MaterialsFolder + "/M_ZoneBeam.mat";
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.3f));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.SetFloat("_Cull", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void Marker(Transform g, int zoneId, Vector3 basePoint, float poleHeight)
        {
            var white = ArtLibrary.White;
            var tint = ArtKit.Color("ZoneTint", Color.white, 0.2f);
            tint.EnableKeyword("_EMISSION");
            tint.SetColor("_EmissionColor", Color.black);
            tint.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(tint);

            var root = ArtKit.Group($"Zone{zoneId}", g);
            root.position = basePoint;
            ArtKit.Cylinder("Pole", basePoint, 0.07f, poleHeight, white, root, false);
            ArtKit.Box("PoleTop", basePoint + Vector3.up * (poleHeight + 0.06f), new Vector3(0.2f, 0.12f, 0.2f), ArtLibrary.MetalDark, root, false);

            var flag = ArtKit.Group("Flag", root);
            flag.position = basePoint + Vector3.up * (poleHeight - 0.55f);
            var cloth = ArtKit.Box("Cloth", flag.position + new Vector3(0.82f, 0f, 0f), new Vector3(1.55f, 0.95f, 0.04f), tint, flag, false);
            cloth.isStatic = false;
            flag.gameObject.isStatic = false;

            // Пунктирный круг зоны.
            var ring = new System.Collections.Generic.List<Renderer>();
            const int dashes = 22;
            const float radius = 2.4f;
            for (int k = 0; k < dashes; k++)
            {
                float a = k * Mathf.PI * 2f / dashes;
                var dash = ArtKit.Box("RingDash", basePoint + new Vector3(Mathf.Cos(a) * radius, 0.035f, Mathf.Sin(a) * radius),
                    new Vector3(0.18f, 0.05f, 0.42f), tint, root, false);
                dash.transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                dash.isStatic = false;
                var r = dash.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring.Add(r);
            }

            // Столб света над флажком.
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "Beam";
            Object.DestroyImmediate(beam.GetComponent<Collider>());
            beam.transform.SetParent(root, false);
            const float beamHeight = 16f;
            beam.transform.position = basePoint + Vector3.up * (poleHeight + 0.2f + beamHeight * 0.5f);
            beam.transform.localScale = new Vector3(0.55f, beamHeight * 0.5f, 0.55f);
            var beamRenderer = beam.GetComponent<Renderer>();
            beamRenderer.sharedMaterial = BeamMaterial();
            beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beamRenderer.receiveShadows = false;

            var label = ArtKit.Label("Label", YardZones.NameOf(zoneId), basePoint + Vector3.up * (poleHeight + 0.85f), 0f, 0.085f, Color.white, root);
            label.fontStyle = FontStyle.Bold;
            label.richText = true;
            label.gameObject.isStatic = false;
            label.gameObject.AddComponent<FaceCamera>();

            var marker = root.gameObject.AddComponent<ZoneMarker>();
            var so = new SerializedObject(marker);
            so.FindProperty("zoneId").intValue = zoneId;
            so.FindProperty("flag").objectReferenceValue = flag;
            so.FindProperty("cloth").objectReferenceValue = cloth.GetComponent<Renderer>();
            so.FindProperty("label").objectReferenceValue = label;
            so.FindProperty("beam").objectReferenceValue = beamRenderer;
            var ringProperty = so.FindProperty("ring");
            ringProperty.arraySize = ring.Count;
            for (int k = 0; k < ring.Count; k++) ringProperty.GetArrayElementAtIndex(k).objectReferenceValue = ring[k];
            so.ApplyModifiedPropertiesWithoutUndo();
            root.gameObject.isStatic = false;
        }
    }
}

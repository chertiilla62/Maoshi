using Maoshi.Hub;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 4: голосование во дворе. Добавляет в сцену Hub три круга и объект HubVoting.
    /// Запуск: Maoshi → Setup → Stage 4 - Hub Voting. Повторный запуск пересоздаёт зону голосования.
    /// </summary>
    public static class MaoshiSetupStage4
    {
        private const string VotingRootName = "VotingArea";
        private const string PadMaterialPath = MaoshiSetup.Root + "/Materials/M_VotePad.mat";

        [MenuItem("Maoshi/Setup/Stage 4 - Hub Voting")]
        public static void SetupStage4()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogError("[Maoshi Setup] Есть несохранённые изменения в открытой сцене. Сохрани её и запусти снова.");
                    return;
                }
            }

            var padMaterial = GetPadMaterial();
            var scene = EditorSceneManager.OpenScene(MaoshiSetup.HubScenePath, OpenSceneMode.Single);

            var old = GameObject.Find(VotingRootName);
            if (old != null) Object.DestroyImmediate(old);

            // Зона голосования у южного края двора, подальше от ящиков и ступеней.
            var root = new GameObject(VotingRootName);
            root.AddComponent<NetworkObject>();
            var voting = root.AddComponent<HubVoting>();

            var positions = new[]
            {
                new Vector3(-4.5f, 0f, -9.5f),
                new Vector3(0f, 0f, -9.5f),
                new Vector3(4.5f, 0f, -9.5f)
            };

            var pads = new VotePad[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                pads[i] = CreatePad(root.transform, $"VotePad{i}", positions[i], padMaterial);

            var serialized = new SerializedObject(voting);
            var padsProperty = serialized.FindProperty("pads");
            padsProperty.arraySize = pads.Length;
            for (int i = 0; i < pads.Length; i++)
                padsProperty.GetArrayElementAtIndex(i).objectReferenceValue = pads[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // Повторное открытие и сохранение: NGO генерирует ID сетевых объектов сцены при загрузке.
            var reopened = EditorSceneManager.OpenScene(MaoshiSetup.HubScenePath, OpenSceneMode.Single);
            foreach (var networkObject in Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
                EditorUtility.SetDirty(networkObject);
            EditorSceneManager.MarkSceneDirty(reopened);
            EditorSceneManager.SaveScene(reopened);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 4 готов: голосование во дворе.");
        }

        private static VotePad CreatePad(Transform parent, string name, Vector3 position, Material material)
        {
            var padGo = new GameObject(name);
            padGo.transform.SetParent(parent, false);
            padGo.transform.position = position;

            // Плоский диск без коллайдера: коты просто заходят на него.
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(padGo.transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            disc.transform.localScale = new Vector3(3f, 0.02f, 3f);
            var discRenderer = disc.GetComponent<Renderer>();
            discRenderer.sharedMaterial = material;

            // Надпись над кругом.
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(padGo.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            var text = labelGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            text.fontSize = 48;
            text.characterSize = 0.06f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.text = "Мини-игра";
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            var pad = padGo.AddComponent<VotePad>();
            var serialized = new SerializedObject(pad);
            serialized.FindProperty("disc").objectReferenceValue = discRenderer;
            serialized.FindProperty("label").objectReferenceValue = text;
            serialized.FindProperty("radius").floatValue = 1.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return pad;
        }

        private static Material GetPadMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(PadMaterialPath);
            if (material != null) return material;

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");
            material = new Material(lit);
            var color = new Color(0.95f, 0.85f, 0.45f);
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            AssetDatabase.CreateAsset(material, PadMaterialPath);
            return material;
        }
    }
}

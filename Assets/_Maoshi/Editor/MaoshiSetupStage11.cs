using Maoshi.UI;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 11: нормальный интерфейс на UI Toolkit и меню настроек.
    /// - PanelSettings: масштаб под экран (эталон 1920×1080), тема Unity по умолчанию.
    /// - В Bootstrap на объект NetworkManager: UIDocument + MaoshiUI + EventSystem (ввод — Input System).
    /// - Старый OnGUI-интерфейс (LobbyUI, GameHUD) убирается с объекта.
    /// Запуск: Maoshi → Setup → Stage 11 - UI And Settings. Можно перезапускать.
    /// </summary>
    public static class MaoshiSetupStage11
    {
        public const string UIFolder = MaoshiSetup.Root + "/UI";
        public const string PanelSettingsPath = UIFolder + "/MaoshiPanelSettings.asset";
        public const string ThemePath = UIFolder + "/MaoshiRuntimeTheme.tss";
        public const string StyleSheetPath = UIFolder + "/Maoshi.uss";

        [MenuItem("Maoshi/Setup/Stage 11 - UI And Settings")]
        public static void SetupStage11()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogError("[Maoshi Setup] Есть несохранённые изменения в открытой сцене. Сохрани её и запусти снова.");
                    return;
                }
            }

            if (GetPanelSettings() == null) return;

            var scene = EditorSceneManager.OpenScene(MaoshiSetup.BootstrapScenePath, OpenSceneMode.Single);
            var manager = Object.FindFirstObjectByType<NetworkManager>();
            if (manager == null)
            {
                Debug.LogError("[Maoshi Setup] В Bootstrap нет NetworkManager. Запусти Stage 1.");
                return;
            }

            MaoshiSetupStage3.ConfigureBootstrapObject(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 11 готов: интерфейс на UI Toolkit и настройки (Esc в игре — пауза).");
        }

        /// <summary>Поставить новый интерфейс на объект NetworkManager (вызывается и из Stage 1/3).</summary>
        public static void ConfigureUI(GameObject target)
        {
            // Старый временный OnGUI-интерфейс больше не нужен.
#pragma warning disable CS0618
            RemoveIfPresent<LobbyUI>(target);
            RemoveIfPresent<GameHUD>(target);
#pragma warning restore CS0618

            var panelSettings = GetPanelSettings();

            var document = target.GetComponent<UIDocument>();
            if (document == null) document = target.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;

            var ui = target.GetComponent<MaoshiUI>();
            if (ui == null) ui = target.AddComponent<MaoshiUI>();
            var serialized = new SerializedObject(ui);
            serialized.FindProperty("styleSheet").objectReferenceValue = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Ввод для интерфейса через Input System (старый Input Manager в проекте выключен).
            if (target.GetComponent<EventSystem>() == null) target.AddComponent<EventSystem>();
            var module = target.GetComponent<InputSystemUIInputModule>();
            if (module == null) module = target.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();

            if (target.GetComponent<NetStatsOverlay>() == null) target.AddComponent<NetStatsOverlay>();

            EditorUtility.SetDirty(target);
        }

        private static PanelSettings GetPanelSettings()
        {
            MaoshiSetup.EnsureFolder(UIFolder);

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null)
            {
                Debug.LogError("[Maoshi Setup] Нет темы " + ThemePath + " (файл должен лежать в проекте).");
                return null;
            }

            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }

            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 10;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static void RemoveIfPresent<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            if (component != null) Object.DestroyImmediate(component, true);
        }
    }
}

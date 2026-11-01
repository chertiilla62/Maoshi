using UnityEditor;
using UnityEditor.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Кнопка Play в редакторе всегда стартует со сцены Bootstrap, какая бы сцена ни была открыта.
    /// Без этого в хабе или мини-игре не будет NetworkManager и ConnectionManager.
    /// Отключается галочкой в меню Maoshi → Play From Bootstrap.
    /// </summary>
    [InitializeOnLoad]
    public static class MaoshiPlayModeBootstrap
    {
        private const string MenuPath = "Maoshi/Play From Bootstrap";
        private const string PrefKey = "Maoshi.PlayFromBootstrap";

        static MaoshiPlayModeBootstrap()
        {
            // Ассеты могут быть ещё не загружены в момент статического конструктора.
            EditorApplication.delayCall += Apply;
        }

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        public static void Apply()
        {
            EditorSceneManager.playModeStartScene = Enabled
                ? AssetDatabase.LoadAssetAtPath<SceneAsset>(MaoshiSetup.BootstrapScenePath)
                : null;
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            Apply();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }
    }
}

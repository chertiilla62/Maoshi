using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maoshi.EditorTools
{
    public static class UIDebug
    {
        [MenuItem("Maoshi/Debug/Report Audio")]
        public static void ReportAudio()
        {
            var manager = Object.FindFirstObjectByType<Maoshi.Audio.AudioManager>();
            if (manager == null) { Debug.Log("[AudioDebug] нет AudioManager"); return; }
            foreach (var source in manager.GetComponentsInChildren<AudioSource>(true))
                if (source.clip != null || source.isPlaying)
                    Debug.Log($"[AudioDebug] {source.name}: clip={(source.clip ? source.clip.name : "-")} playing={source.isPlaying} vol={source.volume:0.00} t={source.time:0.0} blend={source.spatialBlend}");
            int listeners = 0;
            foreach (var l in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) if (l.isActiveAndEnabled) listeners++;
            Debug.Log($"[AudioDebug] активных слушателей {listeners}, общая громкость {AudioListener.volume:0.00}, музыка {Maoshi.Settings.GameSettings.MusicVolume:0.00}, звуки {Maoshi.Settings.GameSettings.SfxVolume:0.00}");
        }

        [MenuItem("Maoshi/Debug/Report Sticker Shadows")]
        public static void Report()
        {
            var doc = Object.FindFirstObjectByType<UIDocument>();
            if (doc == null) { Debug.Log("[UIDebug] нет UIDocument"); return; }
            int n = 0;
            doc.rootVisualElement.Query<VisualElement>(className: "sticker-shadow").ForEach(s =>
            {
                n++;
                if (n > 8) return;
                var next = s.parent != null && s.parent.IndexOf(s) + 1 < s.parent.childCount ? s.parent[s.parent.IndexOf(s) + 1] : null;
                Debug.Log($"[UIDebug] shadow layout={s.layout} display={s.resolvedStyle.display} bg={s.resolvedStyle.backgroundColor} parent={s.parent?.GetClasses()?.GetEnumerator().Current} next={(next != null ? string.Join(",", next.GetClasses()) + " " + next.layout : "null")} world={s.worldBound}");
            });
            int panels = doc.rootVisualElement.Query<VisualElement>(className: "panel").ToList().Count;
            Debug.Log($"[UIDebug] теней {n}, панелей {panels}");
            foreach (var cls in new[] { "menu-backdrop__shade", "brawl-layer", "dim", "chalk-layer" })
                doc.rootVisualElement.Query<VisualElement>(className: cls).ForEach(e =>
                    Debug.Log($"[UIDebug] {cls}: layout={e.layout} bg={e.resolvedStyle.backgroundColor} display={e.resolvedStyle.display} opacity={e.resolvedStyle.opacity} inline={e.style.backgroundColor} parentDisplay={e.parent?.resolvedStyle.display}"));
        }
    }
}

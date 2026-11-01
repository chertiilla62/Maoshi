using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Maoshi.EditorTools.Art;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>Отладка (в Play): удар лапой своего кота и серия снимков сбоку — Temp/Captures/strike_*.png.</summary>
    public static class StrikePreview
    {
        private static readonly float[] Times = { 0.05f, 0.14f, 0.22f, 0.35f, 0.55f };
        private static double _start;
        private static int _next;
        private static Transform _cat;

        [MenuItem("Maoshi/Art/Capture Strike")]
        public static void Run()
        {
            var cat = Object.FindFirstObjectByType<Maoshi.Cat.CatController>();
            if (cat == null || !Application.isPlaying) { Debug.LogWarning("[Maoshi Art] Нужен Play и кот"); return; }
            var visual = cat.GetComponent<Maoshi.Cat.CatVisual>();
            typeof(Maoshi.Cat.CatVisual).GetMethod("OnPush", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(visual, null);
            _cat = cat.transform;
            _start = EditorApplication.timeSinceStartup;
            _next = 0;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (_cat == null || _next >= Times.Length) { EditorApplication.update -= Tick; return; }
            if (EditorApplication.timeSinceStartup - _start < Times[_next]) return;
            Vector3 p = _cat.position + Vector3.up * 0.4f;
            Vector3 c = p + _cat.right * 2.0f + _cat.forward * 0.4f + Vector3.up * 0.3f;
            var rot = Quaternion.LookRotation(p - c).eulerAngles;
            File.WriteAllLines("Temp/capture.txt", new[] { string.Format(CultureInfo.InvariantCulture, "strike_{0} {1} {2} {3} {4} {5} 40", _next, c.x, c.y, c.z, rot.x, rot.y) });
            SceneCapture.Capture();
            _next++;
        }
    }
}

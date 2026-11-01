using Maoshi.Cat;
using Maoshi.Network;
using Maoshi.Session;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Быстрый прогон мини-игр из меню (в Play): создать локальную игру и начать, запустить игру по id,
    /// поставить своего кота в нужную точку, посмотреть очки и подсказку.
    /// </summary>
    public static class MaoshiGameTest
    {
        [MenuItem("Maoshi/Test/Game - Host And Start (Play)")]
        public static void HostAndStart()
        {
            var c = ConnectionManager.Instance;
            if (c == null) return;
            if (c.State == ConnectionState.Offline) c.HostGame(ConnectionMode.Local, "Тест");
            else c.StartGame();
        }

        [MenuItem("Maoshi/Test/Game - Sun Spot (Play)")]
        public static void SunSpot() => Start("sun_spot");

        [MenuItem("Maoshi/Test/Game - Hot Pie (Play)")]
        public static void HotPie() => Start("hot_pie");

        [MenuItem("Maoshi/Test/Game - Sour Cream (Play)")]
        public static void SourCream() => Start("sour_cream");

        private static void Start(string id)
        {
            if (SessionManager.Instance == null) { Debug.LogError("[Maoshi] Нет SessionManager — сначала Host And Start (дважды)."); return; }
            SessionManager.Instance.StartMiniGame(id);
        }

        [MenuItem("Maoshi/Test/Game - Cat To Sun Spot (Play)")]
        public static void CatToSun()
        {
            var mode = Maoshi.MiniGames.SunSpot.SunSpotMode.Instance;
            var cat = LocalCat();
            if (mode == null || cat == null) return;
            Vector3 spot = mode.SpotPoint(mode.NowTime + 0.5);
            cat.SetRespawnAndTeleportRpc(spot + Vector3.up * 0.3f, cat.transform.rotation);
        }

        [MenuItem("Maoshi/Test/Game - Sun Debug (Play)")]
        public static void SunDebug()
        {
            var mode = Maoshi.MiniGames.SunSpot.SunSpotMode.Instance;
            if (mode == null) return;
            mode.Beam(mode.NowTime, out var w, out var d, out var s);
            int hits = 0;
            if (mode.EnvironmentRaycast(w - d * 0.8f, d, 20f, out var hit)) hits++;
            Debug.Log($"[Maoshi] Солнце: окно {w:0.00}, напр {d:0.00}, пятно {s:0.00}, облако {mode.Cloudy}, луч попал: {(hits > 0 ? hit.collider.name + " " + hit.point.ToString("0.00") : "нет")}");
            foreach (var f in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (f.name != "Patch" && f.name != "Shaft") continue;
                var m = f.sharedMesh;
                if (m == null) { Debug.Log($"[Maoshi] {f.name}: нет меша"); continue; }
                int nonzero = 0;
                var tris = m.triangles;
                for (int i = 0; i < tris.Length; i += 3) if (tris[i] != 0 || tris[i + 1] != 0 || tris[i + 2] != 0) nonzero++;
                var r = f.GetComponent<MeshRenderer>();
                Debug.Log($"[Maoshi] {f.name}: вершин {m.vertexCount}, треугольников {nonzero}, цвет[центр] {(m.colors.Length > 0 ? m.colors[m.colors.Length / 2].ToString() : "-")}, материал {r.sharedMaterial?.name} {r.sharedMaterial?.shader?.name}, видим {r.isVisible}");
            }
        }

        [MenuItem("Maoshi/Test/Game - Sun Capture (Play)")]
        public static void SunCapture()
        {
            var mode = Maoshi.MiniGames.SunSpot.SunSpotMode.Instance;
            if (mode == null) return;
            Vector3 s = mode.SpotPoint(mode.NowTime);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string L(string n, Vector3 p, float pitch, float yaw, float fov) =>
                string.Format(inv, "{0} {1} {2} {3} {4} {5} {6}", n, p.x, p.y, p.z, pitch, yaw, fov);
            System.IO.File.WriteAllLines("Temp/capture.txt", new[]
            {
                L("sun_close", s + new Vector3(0f, 2.3f, -1.9f), 50f, 0f, 70f),
                L("sun_room", new Vector3(0f, 2.6f, -4.6f), 18f, 0f, 75f),
            });
            Maoshi.EditorTools.Art.SceneCapture.Capture();
        }

        [MenuItem("Maoshi/Test/Game - Cat To Bowl And Lick (Play)")]
        public static void CatToBowl()
        {
            var mode = Maoshi.MiniGames.SourCream.SourCreamMode.Instance;
            var cat = LocalCat();
            if (mode == null || cat == null) return;
            if (!mode.LocalNearBowl)
            {
                cat.SetRespawnAndTeleportRpc(mode.BowlPosition + new Vector3(0.9f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
                return;
            }
            mode.Lick();
        }

        [MenuItem("Maoshi/Test/Game - Cat To Rug (Play)")]
        public static void CatToRug()
        {
            var cat = LocalCat();
            var rug = GameObject.Find("WashRug");
            if (cat != null && rug != null) cat.SetRespawnAndTeleportRpc(rug.transform.position + Vector3.up * 0.2f, cat.transform.rotation);
        }

        [MenuItem("Maoshi/Test/Game - Log (Play)")]
        public static void Log()
        {
            var mode = MiniGameMode.Current;
            if (mode == null) { Debug.Log("[Maoshi] Нет мини-игры"); return; }
            var cat = LocalCat();
            string scores = "";
            foreach (var s in mode.Scores) scores += $"{s.ClientId}:{s.Score} ";
            Debug.Log($"[Maoshi] {mode.MiniGameId} фаза {mode.Phase}, осталось {mode.TimeLeft:0}, живых {mode.AliveCount}, очки [{scores}], " +
                      $"подсказка «{mode.StatusText}», кот {(cat != null ? cat.transform.position.ToString("0.0") : "-")}");
            var pie = Maoshi.MiniGames.HotPie.HotPieMode.Instance;
            if (pie != null)
            {
                var go = GameObject.Find("HotPie");
                Debug.Log($"[Maoshi] Пирожок: у {pie.Holder.Value}, прилёт через {pie.ArriveTime.Value - pie.NowTime:0.00}, до пуфа {pie.RoundEnd.Value - pie.NowTime:0.0}, жар {pie.Heat:0.00}, объект {(go != null ? go.transform.position.ToString("0.00") + (go.activeSelf ? " виден" : " скрыт") : "нет")}");
            }
        }

        private static CatController LocalCat() =>
            NetworkManager.Singleton != null ? CatController.FindByOwner(NetworkManager.Singleton.LocalClientId) : null;
    }
}

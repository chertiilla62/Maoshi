using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Maoshi.EditorTools
{
    /// <summary>Отладка: кости префаба кота (позиции и оси в мировых координатах префаба) → Temp/cat_bones.txt.</summary>
    public static class CatBonesDump
    {
        [MenuItem("Maoshi/Debug/Dump Cat Bones")]
        public static void Dump()
        {
            var root = PrefabUtility.LoadPrefabContents(MaoshiSetupStage2.CatPrefabPath);
            try
            {
                var sb = new StringBuilder();
                var ci = CultureInfo.InvariantCulture;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.GetComponent<Renderer>() != null) continue;
                    string path = t.name;
                    for (var p = t.parent; p != null && p != root.transform; p = p.parent) path = p.name + "/" + path;
                    Vector3 pos = t.position, f = t.forward, u = t.up, r = t.right, s = t.lossyScale;
                    sb.AppendLine(string.Format(ci, "{0} | pos {1:F3} {2:F3} {3:F3} | fwd {4:F2} {5:F2} {6:F2} | up {7:F2} {8:F2} {9:F2} | right {10:F2} {11:F2} {12:F2} | scale {13:F3}",
                        path, pos.x, pos.y, pos.z, f.x, f.y, f.z, u.x, u.y, u.z, r.x, r.y, r.z, s.x));
                }
                File.WriteAllText("Temp/cat_bones.txt", sb.ToString());
                Debug.Log("[Maoshi] Кости кота → Temp/cat_bones.txt");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}

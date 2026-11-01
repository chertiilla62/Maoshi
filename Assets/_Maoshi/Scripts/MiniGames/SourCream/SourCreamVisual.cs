using System.Collections.Generic;
using Maoshi.Cat;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.MiniGames.SourCream
{
    /// <summary>
    /// Картинка «Кто съел сметану?» (у всех):
    /// - «усы в сметане» — белые капли на морде: у всех испачканных после возвращения хозяйки, у себя — сразу
    ///   после лизания (другие не видят);
    /// - хозяйка в дверях, когда вернулась;
    /// - уровень сметаны в миске убывает (по съеденному за игру, открывается при возвращении хозяйки);
    /// - подсветка миски, когда свой кот рядом.
    /// </summary>
    public class SourCreamVisual : MonoBehaviour
    {
        [SerializeField] private Transform hostess;
        [SerializeField] private Transform cream;
        [SerializeField] private float creamFullY = 0.09f;
        [SerializeField] private float creamEmptyY = 0.02f;
        [SerializeField] private int creamCapacity = 30;
        [SerializeField] private Transform bowlGlow;
        [SerializeField] private Material creamMaterial;
        [SerializeField] private Mesh blobMesh;

        private class Whiskers
        {
            public Transform Root;
            public CatController Cat;
        }

        private readonly Dictionary<ulong, Whiskers> _whiskers = new();
        private readonly List<ulong> _remove = new();
        private Vector3 _hostessShown, _hostessHidden;
        private float _creamY;

        private void Awake()
        {
            if (hostess != null)
            {
                _hostessShown = hostess.position;
                _hostessHidden = hostess.position + hostess.forward * -2.5f;
                hostess.position = _hostessHidden;
            }
            _creamY = creamFullY;
        }

        private void Update()
        {
            var mode = SourCreamMode.Instance;
            if (mode == null || !mode.IsSpawned) return;
            var stage = mode.GameStage.Value;
            bool playing = mode.Phase == Maoshi.Session.MiniGamePhase.Playing;
            bool hostessHome = playing && stage != SourCreamMode.Stage.Prowl;

            // Хозяйка входит/выходит.
            if (hostess != null)
            {
                Vector3 target = hostessHome ? _hostessShown : _hostessHidden;
                hostess.position = Vector3.MoveTowards(hostess.position, target, 3f * Time.deltaTime);
                if (hostess.gameObject.activeSelf != (Vector3.Distance(hostess.position, _hostessHidden) > 0.05f))
                    hostess.gameObject.SetActive(Vector3.Distance(hostess.position, _hostessHidden) > 0.05f || hostessHome);
            }

            // Сметана в миске.
            if (cream != null)
            {
                float k = Mathf.Clamp01(mode.EatenTotal.Value / (float)creamCapacity);
                float y = Mathf.Lerp(creamFullY, creamEmptyY, k);
                _creamY = Mathf.MoveTowards(_creamY, y, Time.deltaTime * 0.05f);
                var p = cream.localPosition;
                cream.localPosition = new Vector3(p.x, _creamY, p.z);
            }

            if (bowlGlow != null)
            {
                bool near = playing && stage == SourCreamMode.Stage.Prowl && mode.LocalNearBowl;
                if (bowlGlow.gameObject.activeSelf != near) bowlGlow.gameObject.SetActive(near);
                if (near) bowlGlow.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(Time.time * 6f));
            }

            // Кому рисовать усы.
            ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            bool shown = playing && stage != SourCreamMode.Stage.Prowl;
            foreach (var cat in CatController.All)
            {
                if (cat == null) continue;
                ulong id = cat.OwnerClientId;
                bool smeared = shown ? mode.SmearedShown.Contains(id) : (id == me && mode.LocalKnowsSmeared && playing);
                bool has = _whiskers.ContainsKey(id);
                if (smeared && !has) _whiskers[id] = Create(cat);
                else if (!smeared && has)
                {
                    if (_whiskers[id].Root != null) Destroy(_whiskers[id].Root.gameObject);
                    _whiskers.Remove(id);
                }
            }
            _remove.Clear();
            foreach (var pair in _whiskers) if (pair.Value.Cat == null) _remove.Add(pair.Key);
            foreach (var id in _remove) { if (_whiskers[id].Root != null) Destroy(_whiskers[id].Root.gameObject); _whiskers.Remove(id); }
        }

        /// <summary>Капли сметаны на морде: ставим по позе префаба (морда кота ≈ (0, 0.54, 0.69) от корня) и цепляем к голове.</summary>
        private Whiskers Create(CatController cat)
        {
            var root = new GameObject("CreamWhiskers").transform;
            Transform head = null;
            foreach (var t in cat.GetComponentsInChildren<Transform>())
                if (t.name == "Head") { head = t; break; }
            root.SetPositionAndRotation(cat.transform.TransformPoint(new Vector3(0f, 0.54f, 0.68f)), cat.transform.rotation);
            Vector3[] blobs =
            {
                new(0f, 0.012f, 0.012f), new(-0.035f, -0.008f, 0f), new(0.035f, -0.008f, 0f),
                new(-0.018f, -0.03f, 0.004f), new(0.02f, -0.032f, 0.004f), new(0f, -0.05f, -0.004f),
            };
            float[] sizes = { 0.03f, 0.028f, 0.028f, 0.022f, 0.024f, 0.02f };
            for (int i = 0; i < blobs.Length; i++)
            {
                var go = new GameObject("Blob");
                go.transform.SetParent(root, false);
                go.transform.localPosition = blobs[i];
                go.transform.localScale = new Vector3(sizes[i] * 1.3f, sizes[i], sizes[i]);
                go.AddComponent<MeshFilter>().sharedMesh = blobMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = creamMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (head != null) root.SetParent(head, true);
            else root.SetParent(cat.transform, true);
            return new Whiskers { Root = root, Cat = cat };
        }
    }
}

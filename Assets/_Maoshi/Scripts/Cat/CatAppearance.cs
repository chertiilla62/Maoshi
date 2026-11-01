using System;
using System.Collections.Generic;
using Maoshi.Network;
using Unity.Netcode;
using UnityEngine;

namespace Maoshi.Cat
{
    /// <summary>
    /// Внешний вид кота: окраска, выбранная игроком (или по его цвету), и табличка с именем над головой.
    /// Модель AnimX одна на всех; окраски отличаются только материалами (тело, мех, глаза),
    /// поэтому при спавне подменяем материалы в нужных слотах. Индекс окраски = NetworkPlayer.EffectiveSkin (см. CatSkins).
    /// Данные берёт из NetworkPlayer с тем же владельцем, сам ничего по сети не шлёт.
    /// </summary>
    public class CatAppearance : NetworkBehaviour
    {
        /// <summary>Набор материалов одной окраски.</summary>
        [Serializable]
        public struct BreedSkin
        {
            public string name;
            public Material body;
            public Material fur;
            public Material eye;
        }

        [SerializeField] private Transform model;
        [SerializeField] private BreedSkin[] breeds;
        [SerializeField] private TextMesh nameplate;
        [SerializeField] private float nameplateHeight = 1.0f;

        /// <summary>Модель (для превью в меню).</summary>
        public Transform Model => model;
        public int SkinCount => breeds?.Length ?? 0;

        // Слоты материалов модели, которые надо подменять: какой рендерер, какой слот, что в нём (тело/мех/глаз).
        private enum SlotKind { Body, Fur, Eye }
        private struct Slot
        {
            public Renderer Renderer;
            public int Index;
            public SlotKind Kind;
        }

        private readonly List<Slot> _slots = new();
        private readonly Dictionary<string, GameObject> _accessories = new();
        private int _appliedBreed = -1;

        private void Awake()
        {
            CollectSlots(model, _slots);
        }

        /// <summary>Находит в модели слоты с материалами тела, меха и глаз (по имени материала AnimX или окраски).</summary>
        private static void CollectSlots(Transform root, List<Slot> slots)
        {
            slots.Clear();
            if (root == null) return;

            foreach (var modelRenderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = modelRenderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    string materialName = materials[i].name;

                    if (materialName.StartsWith("M_Cat.body") || materialName.StartsWith("M_CatSkin")) slots.Add(new Slot { Renderer = modelRenderer, Index = i, Kind = SlotKind.Body });
                    else if (materialName.StartsWith("M_Cat.fur")) slots.Add(new Slot { Renderer = modelRenderer, Index = i, Kind = SlotKind.Fur });
                    else if (materialName.StartsWith("M_Cat.eye")) slots.Add(new Slot { Renderer = modelRenderer, Index = i, Kind = SlotKind.Eye });
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            NetworkPlayer.ListChanged += Apply;
            Apply();
        }

        public override void OnNetworkDespawn()
        {
            NetworkPlayer.ListChanged -= Apply;
        }

        private void Apply()
        {
            var owner = NetworkPlayer.FindByClientId(OwnerClientId);
            if (owner == null) return; // паспорт игрока ещё не пришёл — применим при ListChanged

            ApplyBreed(owner.EffectiveSkin);
            CatAccessories.ApplyTo(model, _accessories, owner.HeadItem.Value, owner.NeckItem.Value, owner.FaceItem.Value);

            if (nameplate != null)
            {
                nameplate.text = owner.DisplayName;
                nameplate.color = Color.Lerp(owner.Color, Color.white, 0.4f);
                // Своё имя над головой не нужно — его и так знаешь.
                nameplate.gameObject.SetActive(!IsOwner);
            }
        }

        private void ApplyBreed(int skinIndex)
        {
            if (breeds == null || breeds.Length == 0 || skinIndex < 0) return;

            int breed = skinIndex % breeds.Length;
            if (breed == _appliedBreed) return;
            _appliedBreed = breed;
            ApplySlots(_slots, breeds[breed]);
        }

        /// <summary>Надеть окраску на любую копию модели кота (превью в главном меню).</summary>
        public void ApplySkinTo(Transform otherModel, int skinIndex)
        {
            if (otherModel == null || breeds == null || breeds.Length == 0) return;
            var slots = new List<Slot>();
            CollectSlots(otherModel, slots);
            ApplySlots(slots, breeds[Mathf.Abs(skinIndex) % breeds.Length]);
        }

        private static void ApplySlots(List<Slot> slots, BreedSkin skin)
        {
            // Меняем sharedMaterials целиком по рендереру — без создания копий материалов.
            var byRenderer = new Dictionary<Renderer, Material[]>();
            foreach (var slot in slots)
            {
                if (slot.Renderer == null) continue;
                if (!byRenderer.TryGetValue(slot.Renderer, out var materials))
                {
                    materials = slot.Renderer.sharedMaterials;
                    byRenderer[slot.Renderer] = materials;
                }

                Material replacement = slot.Kind switch
                {
                    SlotKind.Body => skin.body,
                    SlotKind.Fur => skin.fur,
                    _ => skin.eye
                };

                if (replacement != null) materials[slot.Index] = replacement;
            }

            foreach (var pair in byRenderer) pair.Key.sharedMaterials = pair.Value;
        }

        private void LateUpdate()
        {
            if (nameplate == null || !nameplate.gameObject.activeSelf) return;

            // Табличка следует за (сглаженной) моделью, не кувыркается и смотрит в камеру.
            Vector3 anchor = model != null ? model.position : transform.position;
            nameplate.transform.position = anchor + Vector3.up * nameplateHeight;
            var main = Camera.main;
            if (main != null) nameplate.transform.rotation = main.transform.rotation;
        }
    }
}

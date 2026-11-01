using Maoshi.EditorTools.Art;
using Maoshi.MiniGames.PigeonRace;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 31: «Гонка за голубем» сложнее — паркур и двигающиеся ловушки поверх трассы этапов 15/17.
    /// - Крыша 2: бельё на верёвке качается поперёк крыши и сносит котов к краю.
    /// - Крыша 3: вертушка — планка крутится над крышей, перепрыгивай.
    /// - Доска над широким провалом разбита на три куска, они по очереди трясутся и проваливаются.
    /// - Крыша 4: тележка ездит поперёк, вместо низкого барьера — брандмауэр 1.55 м, на него — по ящикам.
    /// - Узкая крыша 5: две вентиляционные трубы по очереди бьют паром вверх и подкидывают кота.
    /// - Голубь пугливее (улетает раньше), времени на забег 90 с.
    /// Запуск: Maoshi → Setup → Stage 31 - Pigeon Race Traps. Повторный запуск пересобирает ловушки.
    /// После пересборки этапом 15 (и 17) этот этап запустить снова.
    /// </summary>
    public static class MaoshiSetupStage31
    {
        private const string ScenePath = MaoshiSetup.ScenesFolder + "/PigeonRace.unity";
        private const string GroupName = "Traps";

        // Крыши — как в этапе 15 (x, верх, начало/конец по Z).
        private const float R1X = 0.5f, R1Top = 8.6f;
        private const float R2X = -0.5f, R2Top = 8f;
        private const float R3X = 0f, R3Top = 9f;
        private const float R4X = 1f, R4Top = 8.4f;
        private const float R0Top = 8f;
        private const float R5X = 0f, R5Top = 8.4f;

        [MenuItem("Maoshi/Setup/Stage 31 - Pigeon Race Traps")]
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

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var old = GameObject.Find(GroupName);
            if (old != null) Object.DestroyImmediate(old);
            foreach (var name in new[] { "Plank", "Hurdle" })
            {
                var go = GameObject.Find(name);
                if (go != null) Object.DestroyImmediate(go);
            }

            var g = ArtKit.Group(GroupName);
            Laundry(g);
            Spinner(g);
            FallingPlanks(g);
            Cart(g);
            Firewall(g);
            Vent(g, "VentA", new Vector3(R4X - 0.8f, R4Top, 53.4f), 0f);
            Vent(g, "VentB", new Vector3(R4X - 0.6f, R4Top, 56.9f), 0.5f);
            StartHurdles(g);
            Parapet(g);
            Cart(g, "RoofCart2", 40.4f, 0.5f);
            FinalSpinner(g);
            Pot(g, "PotA", new Vector3(R2X - 1.2f, R2Top, 27.6f), 3.4f, 0f);
            Pot(g, "PotB", new Vector3(R2X + 1.1f, R2Top, 28.9f), 3.4f, 0.5f);
            Pot(g, "PotC", new Vector3(R5X - 1.7f, R5Top, 64.4f), 3.0f, 0f);
            Pot(g, "PotD", new Vector3(R5X + 1.7f, R5Top, 64.7f), 3.0f, 0.33f);
            Pot(g, "PotE", new Vector3(R5X, R5Top, 61.2f), 3.0f, 0.66f);
            Pot(g, "PotF", new Vector3(R1X + 1.5f, R1Top, 23.0f), 3.6f, 0.2f);
            TuneMode();

            MaoshiSetupStage22.SaveWithNetworkIds(scene, ScenePath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 31 готов: ловушки и паркур в «Гонке за голубем».");
        }

        private static RoofTrap Trap(GameObject host, RoofTrap.Kind kind, Transform pivot, Transform hit, float period, float phase, float amount,
            float force, float lift, bool knockDown = false)
        {
            var trap = host.AddComponent<RoofTrap>();
            var so = new SerializedObject(trap);
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("pivot").objectReferenceValue = pivot;
            so.FindProperty("hitVolume").objectReferenceValue = hit;
            so.FindProperty("period").floatValue = period;
            so.FindProperty("phase").floatValue = phase;
            so.FindProperty("amount").floatValue = amount;
            so.FindProperty("hitForce").floatValue = force;
            so.FindProperty("hitLift").floatValue = lift;
            so.FindProperty("knockDown").boolValue = knockDown;
            so.ApplyModifiedPropertiesWithoutUndo();
            return trap;
        }

        private static void Dynamic(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
        }

        // Бельё: рама из двух столбов с верёвкой, два полотна качаются поперёк крыши.
        private static void Laundry(Transform g)
        {
            var group = ArtKit.Group("Laundry", g);
            const float z = 20.1f;
            float ropeY = R1Top + 2.75f;
            foreach (float x in new[] { R1X - 2.8f, R1X + 2.8f })
                ArtKit.Box("LaundryPost", new Vector3(x, R1Top + 1.4f, z), new Vector3(0.1f, 2.8f, 0.1f), ArtLibrary.MetalDark, group);
            ArtKit.Box("LaundryRope", new Vector3(R1X, ropeY, z), new Vector3(5.7f, 0.03f, 0.03f), ArtLibrary.White, group, false);

            string[] cloths = { "Cloth_1", "Cloth_3" };
            float[] xs = { R1X - 1.05f, R1X + 1.05f };
            for (int i = 0; i < 2; i++)
            {
                var pivot = new GameObject($"SheetPivot{i}").transform;
                pivot.SetParent(group, false);
                pivot.position = new Vector3(xs[i], ropeY, z);
                var sheet = ArtKit.Box($"Sheet{i}", Vector3.zero, Vector3.one, ArtLibrary.Tex(cloths[i]), pivot, false);
                sheet.transform.localPosition = new Vector3(0f, -1.2f, 0f);
                sheet.transform.localScale = new Vector3(1.0f, 2.35f, 0.04f);
                Dynamic(pivot);
                Trap(pivot.gameObject, RoofTrap.Kind.Swing, pivot, sheet.transform, 2.6f, i * 0.12f, 55f, 6.5f, 3f);
            }
        }

        // Вертушка: стойка и крутящаяся планка на высоте лап — надо перепрыгнуть.
        private static void Spinner(Transform g)
        {
            var group = ArtKit.Group("Spinner", g);
            Vector3 c = new(R2X, R2Top, 32.3f);
            ArtKit.Cylinder("SpinnerPost", c, 0.13f, 0.55f, ArtLibrary.MetalDark, group);
            var pivot = new GameObject("SpinnerPivot").transform;
            pivot.SetParent(group, false);
            pivot.position = c + Vector3.up * 0.32f;
            var red = ArtKit.Color("TrapRed", new Color(0.85f, 0.22f, 0.18f), 0.3f);
            var arm = ArtKit.Box("SpinnerArm", Vector3.zero, Vector3.one, red, pivot, false);
            arm.transform.localPosition = Vector3.zero;
            arm.transform.localScale = new Vector3(3.3f, 0.14f, 0.16f);
            var tipA = ArtKit.Box("SpinnerTip", Vector3.zero, Vector3.one, ArtLibrary.White, pivot, false);
            tipA.transform.localPosition = new Vector3(1.45f, 0f, 0f);
            tipA.transform.localScale = new Vector3(0.4f, 0.16f, 0.18f);
            var tipB = ArtKit.Box("SpinnerTip", Vector3.zero, Vector3.one, ArtLibrary.White, pivot, false);
            tipB.transform.localPosition = new Vector3(-1.45f, 0f, 0f);
            tipB.transform.localScale = new Vector3(0.4f, 0.16f, 0.18f);
            Dynamic(pivot);
            Trap(pivot.gameObject, RoofTrap.Kind.Spin, pivot, arm.transform, 3f, 0f, 125f, 5f, 3.5f);
        }

        // Доска над провалом — три куска, проваливаются по очереди.
        private static void FallingPlanks(Transform g)
        {
            var group = ArtKit.Group("FallingPlanks", g);
            const float z0 = 33.7f, z1 = 39.3f;
            float len = (z1 - z0) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float z = z0 + len * (i + 0.5f);
                var plank = ArtKit.Box($"PlankPiece{i}", new Vector3(R2X, R2Top - 0.08f, z), new Vector3(0.8f, 0.16f, len - 0.06f), ArtLibrary.Planks, group);
                var body = plank.AddComponent<Rigidbody>();
                body.isKinematic = true;
                plank.isStatic = false;
                var trap = Trap(plank, RoofTrap.Kind.Sink, plank.transform, null, 4.2f, i / 3f, 1.8f, 0f, 0f);
                var so = new SerializedObject(trap);
                so.FindProperty("activeFraction").floatValue = 0.28f;
                so.FindProperty("warnSeconds").floatValue = 0.9f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // Тележка кровельщика ездит поперёк крыши 4.
        private static void Cart(Transform g) => Cart(g, "RoofCart", 43.8f, 0f);

        private static void Cart(Transform g, string name, float z, float phase)
        {
            var group = ArtKit.Group(name, g);
            var cart = ArtKit.Box(name, new Vector3(R3X, R3Top + 0.45f, z), new Vector3(1.1f, 0.7f, 1.0f), ArtLibrary.ShedPlanks, group);
            cart.AddComponent<Rigidbody>().isKinematic = true;
            Dynamic(cart.transform);
            Trap(cart, RoofTrap.Kind.Slide, cart.transform, cart.transform, 3.2f, phase, 2.55f, 7f, 2.5f);
        }

        // Брандмауэр вместо низкого барьера: на 1.55 м с места не запрыгнуть — только с ящика.
        private static void Firewall(Transform g)
        {
            var group = ArtKit.Group("Firewall", g);
            ArtKit.UvBox("Firewall", new Vector3(R3X, R3Top, 47.6f), new Vector3(7.2f, 1.55f, 0.5f), ArtLibrary.BrickPlain, ArtLibrary.Concrete, new Vector2(1.5f, 1.5f), 1f, group);
            foreach (float x in new[] { -2.3f, 2.3f })
                ArtKit.PlaceFit("Platformer/crate", new Vector3(R3X + x, R3Top, 46.8f), 0f, new Vector3(1.0f, 0.8f, 1.0f), group, true);
            // Ящик-ступенька пониже посередине со стороны финиша — спуститься.
            ArtKit.PlaceFit("Platformer/crate", new Vector3(R3X, R3Top, 48.35f), 0f, new Vector3(0.9f, 0.7f, 0.8f), group, true);
        }

        // Вентиляция: труба с решёткой, выброс пара подкидывает кота вверх и в сторону.
        private static void Vent(Transform g, string name, Vector3 at, float phase)
        {
            var group = ArtKit.Group(name, g);
            ArtKit.Cylinder("VentPipe", at, 0.28f, 0.14f, ArtLibrary.MetalDark, group, false);
            ArtKit.Cylinder("VentGrille", at + Vector3.up * 0.14f, 0.22f, 0.02f, ArtLibrary.Rubber, group, false);

            var hit = new GameObject("SteamZone").transform;
            hit.SetParent(group, false);
            hit.position = at + Vector3.up * 0.7f;
            hit.localScale = new Vector3(1.1f, 1.4f, 1.1f);

            var psGo = new GameObject("Steam");
            psGo.transform.SetParent(group, false);
            psGo.transform.position = at + Vector3.up * 0.15f;
            psGo.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = psGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startColor = new Color(1f, 1f, 1f, 0.75f);
            main.gravityModifier = -0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.15f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var renderer = psGo.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Maoshi/Art/Materials/M_CatDust.mat");

            Dynamic(group);
            var trap = Trap(group.gameObject, RoofTrap.Kind.Steam, group, hit, 3.4f, phase, 0f, 3f, 8.5f);
            var so = new SerializedObject(trap);
            so.FindProperty("steam").objectReferenceValue = ps;
            so.FindProperty("activeFraction").floatValue = 0.2f;
            so.FindProperty("warnSeconds").floatValue = 0.9f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Старт: две трубы поперёк крыши — разбег через барьеры.
        private static void StartHurdles(Transform g)
        {
            var group = ArtKit.Group("StartHurdles", g);
            foreach (float z in new[] { 6f, 9.2f })
            {
                ArtKit.Box("HurdlePipe", new Vector3(0f, R0Top + 0.3f, z), new Vector3(7.8f, 0.3f, 0.3f), ArtLibrary.MetalDark, group);
                foreach (float x in new[] { -3.2f, 0f, 3.2f })
                    ArtKit.Box("HurdleLeg", new Vector3(x, R0Top + 0.08f, z), new Vector3(0.12f, 0.16f, 0.12f), ArtLibrary.MetalDark, group, false);
            }
        }

        // Крыша 2: сразу после прыжка — парапет 0.9 м, его надо перепрыгнуть, под качающимся бельём не разгонишься.
        private static void Parapet(Transform g)
        {
            var group = ArtKit.Group("Parapet", g);
            ArtKit.UvBox("ParapetWall", new Vector3(R1X, R1Top, 15.6f), new Vector3(6f, 0.9f, 0.35f), ArtLibrary.BrickPlain, ArtLibrary.Concrete, new Vector2(1.5f, 1.5f), 1f, group);
        }

        // Финиш: большая вертушка перед голубятней.
        private static void FinalSpinner(Transform g)
        {
            var group = ArtKit.Group("FinalSpinner", g);
            Vector3 c = new(R5X, R5Top, 63.0f);
            ArtKit.Cylinder("SpinnerPost", c, 0.16f, 0.6f, ArtLibrary.MetalDark, group);
            var pivot = new GameObject("FinalSpinnerPivot").transform;
            pivot.SetParent(group, false);
            pivot.position = c + Vector3.up * 0.33f;
            var red = ArtKit.Color("TrapRed", new Color(0.85f, 0.22f, 0.18f), 0.3f);
            var arm = ArtKit.Box("FinalSpinnerArm", Vector3.zero, Vector3.one, red, pivot, false);
            arm.transform.localPosition = Vector3.zero;
            arm.transform.localScale = new Vector3(4.8f, 0.15f, 0.18f);
            var cross = ArtKit.Box("FinalSpinnerCross", Vector3.zero, Vector3.one, ArtLibrary.White, pivot, false);
            cross.transform.localPosition = Vector3.zero;
            cross.transform.localScale = new Vector3(0.18f, 0.15f, 4.8f);
            Dynamic(pivot);
            // Крестовина: две планки — два удара за оборот с каждой стороны.
            Trap(pivot.gameObject, RoofTrap.Kind.Spin, pivot, arm.transform, 3f, 0f, 95f, 5.5f, 3.5f);
            Trap(group.gameObject, RoofTrap.Kind.Spin, pivot, cross.transform, 3f, 0f, 95f, 5.5f, 3.5f);
        }

        // Горшок с балкона: растёт тень на крыше, потом горшок падает и оглушает.
        private static void Pot(Transform g, string name, Vector3 at, float period, float phase)
        {
            var group = ArtKit.Group(name, g);
            var shadowMat = ArtKit.Color("PotShadow", new Color(0.08f, 0.06f, 0.08f));
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "PotShadow";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(group, false);
            marker.transform.position = at + Vector3.up * 0.015f;
            marker.transform.localScale = new Vector3(1.0f, 0.005f, 1.0f);
            var markerRenderer = marker.GetComponent<Renderer>();
            markerRenderer.sharedMaterial = shadowMat;
            markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var pivot = new GameObject("PotPivot").transform;
            pivot.SetParent(group, false);
            pivot.position = at;
            var clay = ArtKit.Color("PotClay", new Color(0.78f, 0.38f, 0.22f), 0.2f);
            var leaves = ArtKit.Color("PotLeaves", new Color(0.30f, 0.62f, 0.26f));
            var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pot.name = "Pot";
            Object.DestroyImmediate(pot.GetComponent<Collider>());
            pot.transform.SetParent(pivot, false);
            pot.transform.localPosition = new Vector3(0f, 0.17f, 0f);
            pot.transform.localScale = new Vector3(0.42f, 0.17f, 0.42f);
            pot.GetComponent<Renderer>().sharedMaterial = clay;
            var bush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bush.name = "Plant";
            Object.DestroyImmediate(bush.GetComponent<Collider>());
            bush.transform.SetParent(pivot, false);
            bush.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            bush.transform.localScale = new Vector3(0.45f, 0.35f, 0.45f);
            bush.GetComponent<Renderer>().sharedMaterial = leaves;
            var hit = new GameObject("PotHit").transform;
            hit.SetParent(pivot, false);
            hit.localPosition = new Vector3(0f, 0.2f, 0f);
            hit.localScale = new Vector3(0.6f, 0.6f, 0.6f);

            Dynamic(group);
            var trap = Trap(group.gameObject, RoofTrap.Kind.Drop, pivot, hit, period, phase, 7f, 2.5f, 2f, true);
            var so = new SerializedObject(trap);
            so.FindProperty("marker").objectReferenceValue = marker.transform;
            so.FindProperty("warnSeconds").floatValue = 1.1f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void TuneMode()
        {
            var mode = Object.FindFirstObjectByType<PigeonRaceMode>();
            if (mode == null) return;
            var so = new SerializedObject(mode);
            so.FindProperty("scareRadius").floatValue = 4.2f;
            so.FindProperty("flySpeed").floatValue = 10f;
            so.FindProperty("finishZ").floatValue = 65.2f;
            so.FindProperty("afterCatchTime").floatValue = 20f;
            so.FindProperty("timeLimitSeconds").floatValue = 100f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

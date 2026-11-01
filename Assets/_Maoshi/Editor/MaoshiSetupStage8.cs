using System.Collections.Generic;
using Maoshi.Cat;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 8: модели и анимации котов AnimX + плавное движение по сети.
    /// - Собирает Animator Controller (бег, прыжок, падение, лапа, удар, «нокаут»).
    /// - Ставит модель AnimX в префаб Cat вместо кубиков: поворачивает мордой вперёд, подгоняет размер, ставит на землю.
    /// - Породы по цвету игрока (материалы тела/меха/глаз).
    /// - NetworkTransform: интерполяция Lerp; тик сети 60.
    /// - Переносит редакторский скрипт ассета в папку Editor (иначе не соберётся билд).
    /// Запуск: Maoshi → Setup → Stage 8 - Cat Models And Smooth Network. Можно перезапускать.
    /// </summary>
    public static class MaoshiSetupStage8
    {
        private const string AnimX = "Assets/AnimX Animals";
        private const string CatsFolder = AnimX + "/Cats";
        private const string SourcePrefabPath = CatsFolder + "/Prefabs/Cat_R_Red.prefab";
        private const string MeshPath = CatsFolder + "/Meshes/Cat_R.fbx";
        private const string AnimationFolder = MaoshiSetup.Root + "/Animation";
        private const string ControllerPath = AnimationFolder + "/AC_MaoshiCat.controller";

        // Порядок = ColorIndex игрока (см. NetworkPlayer.Palette).
        private static readonly string[] Breeds =
            { "Red", "Black", "White", "Gray", "Somali", "Brown", "British", "AmericanShorthair" };

        /// <summary>Высота модели до кончиков ушей, м. Кот чуть «мультяшно» крупный, чтобы хорошо читался.</summary>
        private const float TargetModelHeight = 0.75f;

        [MenuItem("Maoshi/Setup/Stage 8 - Cat Models And Smooth Network")]
        public static void SetupStage8()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogError("[Maoshi Setup] Есть несохранённые изменения в открытой сцене. Сохрани её и запусти снова.");
                    return;
                }
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath) == null)
            {
                Debug.LogError("[Maoshi Setup] Не найден ассет AnimX: " + SourcePrefabPath);
                return;
            }

            MoveAssetEditorScript();
            MaoshiSetup.EnsureFolder(AnimationFolder);

            var controller = BuildController();
            if (controller == null) return;

            if (!SetupCatPrefab(controller)) return;
            SetTickRate(60);

            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 8 готов: модели и анимации котов, плавная сеть (тик 60, Lerp).");
        }

        // ───────────────────────── Скрипт ассета ─────────────────────────

        /// <summary>QuickAnimSetting использует UnityEditor, но лежит не в Editor — билд с ним не соберётся.</summary>
        private static void MoveAssetEditorScript()
        {
            const string from = AnimX + "/_Common/Scripts/QuickAnimSetting.cs";
            const string folder = AnimX + "/_Common/Scripts/Editor";
            if (AssetDatabase.LoadAssetAtPath<MonoScript>(from) == null) return;

            MaoshiSetup.EnsureFolder(folder);
            string error = AssetDatabase.MoveAsset(from, folder + "/QuickAnimSetting.cs");
            if (!string.IsNullOrEmpty(error)) Debug.LogWarning("[Maoshi Setup] Не удалось перенести QuickAnimSetting: " + error);
        }

        // ───────────────────────── Animator Controller ─────────────────────────

        private static AnimatorController BuildController()
        {
            var idle = Clip("A_Cat_Idle", "Base");
            var walk = Clip("A_Cat_Move", "Walk_F");
            var trot = Clip("A_Cat_Move", "Trot_F");
            var run = Clip("A_Cat_Move", "Run_F");
            var jump = Clip("A_Cat_Jump", "Run");
            var fall = Clip("A_Cat_Jump", "Fall_low");
            var paw = Clip("A_Cat_Combat", "Paw_R");
            var damage = Clip("A_Cat_Combat", "Damage.FL");
            var knocked = Clip("A_Cat_Combat", "Death_R");

            if (idle == null || walk == null || trot == null || run == null || jump == null ||
                fall == null || paw == null || damage == null || knocked == null)
            {
                Debug.LogError("[Maoshi Setup] Не все анимации найдены — контроллер не собран.");
                return null;
            }

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Knocked", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Push", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

            // Grounded по умолчанию true, чтобы в первый кадр не мелькнуло падение.
            var parameters = controller.parameters;
            foreach (var parameter in parameters)
                if (parameter.name == "Grounded") parameter.defaultBool = true;
            controller.parameters = parameters;

            var machine = controller.layers[0].stateMachine;

            // Бег: смесь «стоит → шаг → рысь → бег» по скорости (м/с).
            var locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1.2f);
            tree.AddChild(trot, 3f);
            tree.AddChild(run, 5.5f);
            machine.defaultState = locomotion;

            var jumpState = machine.AddState("Jump");
            jumpState.motion = jump;
            var fallState = machine.AddState("Fall");
            fallState.motion = fall;
            var pushState = machine.AddState("Push");
            pushState.motion = paw;
            var hitState = machine.AddState("Hit");
            hitState.motion = damage;
            var knockedState = machine.AddState("Knocked");
            knockedState.motion = knocked;

            // Нокаут — из любого состояния, пока Knocked = true.
            var toKnocked = machine.AddAnyStateTransition(knockedState);
            toKnocked.AddCondition(AnimatorConditionMode.If, 0, "Knocked");
            Setup(toKnocked, 0.1f, false);
            toKnocked.canTransitionToSelf = false;
            var fromKnocked = knockedState.AddTransition(locomotion);
            fromKnocked.AddCondition(AnimatorConditionMode.IfNot, 0, "Knocked");
            Setup(fromKnocked, 0.25f, false);

            // Удар и толчок лапой — из любого состояния (кроме нокаута), потом обратно к бегу.
            AnyTrigger(machine, hitState, "Hit");
            AnyTrigger(machine, pushState, "Push");
            Setup(hitState.AddTransition(locomotion), 0.15f, true, 0.85f);
            Setup(pushState.AddTransition(locomotion), 0.15f, true, 0.8f);

            // Прыжок и падение.
            var toJump = locomotion.AddTransition(jumpState);
            toJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");
            Setup(toJump, 0.05f, false);
            Setup(jumpState.AddTransition(fallState), 0.15f, true, 0.7f);
            var jumpLand = jumpState.AddTransition(locomotion);
            jumpLand.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            Setup(jumpLand, 0.15f, true, 0.35f);

            var toFall = locomotion.AddTransition(fallState);
            toFall.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            Setup(toFall, 0.2f, false);
            var land = fallState.AddTransition(locomotion);
            land.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            Setup(land, 0.15f, false);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void AnyTrigger(AnimatorStateMachine machine, AnimatorState state, string trigger)
        {
            var transition = machine.AddAnyStateTransition(state);
            transition.AddCondition(AnimatorConditionMode.If, 0, trigger);
            transition.AddCondition(AnimatorConditionMode.IfNot, 0, "Knocked");
            transition.canTransitionToSelf = false;
            Setup(transition, 0.05f, false);
        }

        private static void Setup(AnimatorStateTransition transition, float duration, bool exitTime, float exitAt = 0f)
        {
            transition.duration = duration;
            transition.hasExitTime = exitTime;
            transition.exitTime = exitAt;
            transition.hasFixedDuration = true;
        }

        private static AnimationClip Clip(string fbx, string clipName)
        {
            string path = $"{CatsFolder}/Animations/{fbx}.fbx";
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip clip && clip.name == clipName) return clip;
            }

            Debug.LogError($"[Maoshi Setup] Анимация '{clipName}' не найдена в {path}");
            return null;
        }

        // ───────────────────────── Префаб кота ─────────────────────────

        private static bool SetupCatPrefab(AnimatorController controller)
        {
            string catPath = MaoshiSetupStage2.CatPrefabPath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(catPath) == null)
            {
                Debug.LogError("[Maoshi Setup] Нет префаба Cat. Сначала запусти Stage 2.");
                return false;
            }

            Avatar avatar = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(MeshPath))
                if (asset is Avatar found) avatar = found;

            var root = PrefabUtility.LoadPrefabContents(catPath);
            try
            {
                // Старая модель (кубики или прошлая версия) — долой.
                var old = root.transform.Find("Model");
                if (old != null) Object.DestroyImmediate(old.gameObject);

                var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                model.name = "Model";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                FitModel(root.transform, model.transform);

                // В префабе AnimX есть старый компонент Animation (сам крутит клип Base) — он спорит с Animator.
                var legacy = model.GetComponent<Animation>();
                if (legacy != null) Object.DestroyImmediate(legacy, true);

                // Аниматор.
                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                if (avatar != null) animator.avatar = avatar;
                animator.applyRootMotion = true; // root motion «поглощает» CatRootMotionSink
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                if (model.GetComponent<CatRootMotionSink>() == null) model.AddComponent<CatRootMotionSink>();

                // Породы.
                var appearance = root.GetComponent<CatAppearance>();
                var appearanceSerialized = new SerializedObject(appearance);
                appearanceSerialized.FindProperty("model").objectReferenceValue = model.transform;
                appearanceSerialized.FindProperty("nameplateHeight").floatValue = TargetModelHeight + 0.3f;
                var breeds = appearanceSerialized.FindProperty("breeds");
                breeds.arraySize = Breeds.Length;
                for (int i = 0; i < Breeds.Length; i++)
                {
                    var element = breeds.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("name").stringValue = Breeds[i];
                    element.FindPropertyRelative("body").objectReferenceValue = Material("Body", "body", Breeds[i]);
                    element.FindPropertyRelative("fur").objectReferenceValue = Material("Fur", "fur", Breeds[i]);
                    element.FindPropertyRelative("eye").objectReferenceValue = Material("Eye", "eye", Breeds[i]);
                }
                appearanceSerialized.ApplyModifiedPropertiesWithoutUndo();

                // Сглаживание и анимация.
                var visual = root.GetComponent<CatVisual>();
                if (visual == null) visual = root.AddComponent<CatVisual>();
                var visualSerialized = new SerializedObject(visual);
                visualSerialized.FindProperty("model").objectReferenceValue = model.transform;
                visualSerialized.FindProperty("animator").objectReferenceValue = animator;
                visualSerialized.ApplyModifiedPropertiesWithoutUndo();

                // Сеть: современная интерполяция вместо старой.
                var netTransform = root.GetComponent<NetworkTransform>();
                netTransform.PositionInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
                netTransform.RotationInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
                netTransform.PositionMaxInterpolationTime = 0.1f;
                netTransform.RotationMaxInterpolationTime = 0.1f;

                PrefabUtility.SaveAsPrefabAsset(root, catPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return true;
        }

        /// <summary>Поворачивает модель мордой по +Z, масштабирует до нужной высоты и ставит лапами на опорную точку кота.</summary>
        private static void FitModel(Transform catRoot, Transform model)
        {
            // Направление «вперёд» модели — от корня скелета к голове.
            var head = FindDeep(model, "Head");
            var spine = FindDeep(model, "Spine") ?? FindDeep(model, "Root");
            if (head != null && spine != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(head.position - spine.position, Vector3.up);
                if (forward.sqrMagnitude > 0.000001f)
                {
                    float yaw = Vector3.SignedAngle(forward, Vector3.forward, Vector3.up);
                    model.localRotation = Quaternion.Euler(0f, yaw, 0f);
                }
            }

            Bounds bounds = ModelBounds(model);
            if (bounds.size.y > 0.0001f)
                model.localScale *= TargetModelHeight / bounds.size.y;

            // Лапы на землю (y = 0 у кота), центр модели — над центром капсулы.
            bounds = ModelBounds(model);
            Vector3 anchor = catRoot.position;
            model.position += new Vector3(
                anchor.x - bounds.center.x,
                anchor.y - bounds.min.y,
                anchor.z + 0.05f - bounds.center.z);
        }

        private static Bounds ModelBounds(Transform model)
        {
            bool has = false;
            var bounds = new Bounds(model.position, Vector3.zero);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                // Берём самый подробный LOD тела — он точнее всего.
                if (!renderer.name.Contains("Body_LOD0")) continue;
                if (!has) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                has = true;
            }

            if (!has)
            {
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (!has) bounds = renderer.bounds;
                    else bounds.Encapsulate(renderer.bounds);
                    has = true;
                }
            }

            return bounds;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (var child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        private static Material Material(string folder, string kind, string breed)
        {
            string path = $"{CatsFolder}/Materials/{folder}/M_Cat.{kind}_{breed}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) Debug.LogWarning("[Maoshi Setup] Нет материала " + path);
            return material;
        }

        // ───────────────────────── Сеть ─────────────────────────

        private static void SetTickRate(uint tickRate)
        {
            var scene = EditorSceneManager.OpenScene(MaoshiSetup.BootstrapScenePath, OpenSceneMode.Single);
            var networkManager = Object.FindFirstObjectByType<NetworkManager>();
            if (networkManager == null)
            {
                Debug.LogError("[Maoshi Setup] В Bootstrap нет NetworkManager.");
                return;
            }

            networkManager.NetworkConfig.TickRate = tickRate;
            EditorUtility.SetDirty(networkManager);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}

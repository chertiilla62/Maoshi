using Maoshi.Cat;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maoshi.EditorTools
{
    /// <summary>
    /// Этап 9: аккуратные анимации и плавная сеть.
    /// - Аниматор пересобирается: толчок и удар — на отдельном слое с маской (передние лапы, шея, голова),
    ///   поэтому задние лапы продолжают бежать и анимация не «обрывает» бег.
    /// - Лапа выбрасывается ровно в момент удара (подгонка начала клипа под pushImpactDelay).
    /// - Прыжок: клип «в беге», без приседания. Полёт: низкий/высокий по вертикальной скорости.
    ///   Приземление — плавный переход в бег. Оглушение: вздрогнул и трясёт головой (без падения).
    /// - Сеть: чужих котов двигает NetworkTransform каждый кадр (а не физика по FixedUpdate).
    /// - Новые значения толчка и поворота в префабе.
    /// Запуск: Maoshi → Setup → Stage 9 - Animation Polish And Net Smoothing. Можно перезапускать.
    /// </summary>
    public static class MaoshiSetupStage9
    {
        private const string CatsFolder = "Assets/AnimX Animals/Cats";
        private const string SourcePrefabPath = CatsFolder + "/Prefabs/Cat_R_Red.prefab";
        private const string AnimationFolder = MaoshiSetup.Root + "/Animation";
        private const string ControllerPath = AnimationFolder + "/AC_MaoshiCat.controller";
        private const string MaskPath = AnimationFolder + "/AM_CatFrontBody.mask";

        /// <summary>Через сколько секунд после нажатия лапа достаёт до цели (равно CatController.pushImpactDelay).</summary>
        private const float PushImpactDelay = 0.12f;
        private const float PushSpeed = 1.3f;
        /// <summary>Удар с места (всё тело): весомее. На бегу (только перед): резче.</summary>
        private const float StandPushSpeed = 1.25f;
        private const float RunPushSpeed = 1.75f;

        [MenuItem("Maoshi/Setup/Stage 9 - Animation Polish And Net Smoothing")]
        public static void SetupStage9()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogError("[Maoshi Setup] Есть несохранённые изменения в открытой сцене. Сохрани её и запусти снова.");
                    return;
                }
            }

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (source == null)
            {
                Debug.LogError("[Maoshi Setup] Не найден ассет AnimX: " + SourcePrefabPath);
                return;
            }

            MaoshiSetup.EnsureFolder(AnimationFolder);

            var mask = BuildMask(source);
            var controller = BuildController(source, mask);
            if (controller == null) return;

            if (!SetupCatPrefab(controller)) return;

            // Bootstrap: оверлей F3/F4.
            var scene = EditorSceneManager.OpenScene(MaoshiSetup.BootstrapScenePath, OpenSceneMode.Single);
            var manager = Object.FindFirstObjectByType<Unity.Netcode.NetworkManager>();
            if (manager != null)
            {
                MaoshiSetupStage3.ConfigureBootstrapObject(manager.gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            AssetDatabase.SaveAssets();
            // Перечитываем с диска, чтобы в памяти точно не осталось старых ссылок.
            AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(MaoshiSetupStage2.CatPrefabPath, ImportAssetOptions.ForceUpdate);
            EditorSceneManager.OpenScene(MaoshiSetup.MainMenuScenePath, OpenSceneMode.Single);
            Debug.Log("[Maoshi Setup] Этап 9 готов: слой действий с маской, прыжки/падения/приземления, плавная сеть.");
        }

        // ───────────────────────── Маска ─────────────────────────

        /// <summary>Передняя половина тела: плечи, передние лапы, шея, голова (со всем, что на ней).</summary>
        private static AvatarMask BuildMask(GameObject source)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, MaskPath);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                var transforms = instance.GetComponentsInChildren<Transform>(true);
                mask.transformCount = transforms.Length;
                for (int i = 0; i < transforms.Length; i++)
                {
                    string path = AnimationUtility.CalculateTransformPath(transforms[i], instance.transform);
                    mask.SetTransformPath(i, path);
                    mask.SetTransformActive(i, IsFrontBody(path));
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static bool IsFrontBody(string path)
        {
            string[] parts = { "Spine.3", "Shoulder", "UpperArm", "Forearm", "Hand", "Neck", "Head" };
            foreach (var part in parts)
                if (path.Contains(part)) return true;
            return false;
        }

        // ───────────────────────── Контроллер ─────────────────────────

        private static AnimatorController BuildController(GameObject source, AvatarMask mask)
        {
            var idle = Clip("A_Cat_Idle", "Base");
            var walk = Clip("A_Cat_Move", "Walk_F");
            var trot = Clip("A_Cat_Move", "Trot_F");
            var run = Clip("A_Cat_Move", "Run_F");
            var jumpRun = Clip("A_Cat_Jump", "Run");
            var fallLow = Clip("A_Cat_Jump", "Fall_low");
            var fallHigh = Clip("A_Cat_Jump", "Fall_high");
            var stunned = Clip("A_Cat_Combat", "Damage.SL");
            var shaking = Clip("A_Cat_Action", "Shaking");
            var paw = Clip("A_Cat_Combat", "Paw_R");
            var damage = Clip("A_Cat_Combat", "Damage.FL");
            
            if (idle == null || walk == null || trot == null || run == null || jumpRun == null ||
                fallLow == null || fallHigh == null || stunned == null || shaking == null ||
                paw == null || damage == null)
            {
                Debug.LogError("[Maoshi Setup] Не все анимации найдены — контроллер не собран.");
                return null;
            }

            // Тайминги из самих клипов.
            float pawPeak = FindPawPeak(source, paw);
            float pushOffset = Mathf.Clamp01((pawPeak - PushImpactDelay * PushSpeed) / paw.length);
            float jumpOffset = Mathf.Clamp01(FindTakeoff(source, jumpRun) / jumpRun.length);
            Debug.Log($"[Maoshi Setup] Лапа: пик {pawPeak:F2} c из {paw.length:F2}, старт с {pushOffset:P0}. Прыжок: старт с {jumpOffset:P0}.");

            // Клипы для «живого» кота (этап 18): бег с поворотом, подкрадывание, безделье, отдых.
            AnimationClip M(string name) => Clip("A_Cat_Move", name);
            var walkL = M("Walk_L"); var walkR = M("Walk_R");
            var trotL = M("Trot_L"); var trotR = M("Trot_R");
            var runL = M("Run_L"); var runR = M("Run_R");
            var crouchIdle = M("Crouch_idle"); var crouchF = M("Crouch_F"); var crouchL = M("Crouch_L"); var crouchR = M("Crouch_R");
            var crouchTurnL = M("Crouch_Turn.L"); var crouchTurnR = M("Crouch_Turn.R");
            AnimationClip[] idleActions = { Clip("A_Cat_Idle", "Look"), Clip("A_Cat_Idle", "LookDown"), Clip("A_Cat_Idle", "LookSite"), Clip("A_Cat_Idle", "Yaw"), Clip("A_Cat_Idle", "Stretching") };
            var sitTo = Clip("A_Cat_Rest", "Sit_to"); var sitIdle = Clip("A_Cat_Rest", "Sit_Idle");
            var lieTo = Clip("A_Cat_Rest", "Lie_to"); var lieIdle = Clip("A_Cat_Rest", "Lie_idle");

            var controller = GetCleanController();
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Turn", AnimatorControllerParameterType.Float);
            controller.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Knocked", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Push", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("PushStand", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("PushSide", AnimatorControllerParameterType.Int);
            controller.AddParameter("StrideMul", AnimatorControllerParameterType.Float);
            controller.AddParameter("IdleAction", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("IdleVariant", AnimatorControllerParameterType.Int);
            controller.AddParameter("Rest", AnimatorControllerParameterType.Int);

            var parameters = controller.parameters;
            foreach (var parameter in parameters)
            {
                if (parameter.name == "Grounded") parameter.defaultBool = true;
                if (parameter.name == "StrideMul") parameter.defaultFloat = 1f;
            }
            controller.parameters = parameters;

            // ── Базовый слой: передвижение ──
            var machine = controller.layers[0].stateMachine;

            // Бег по дуге: по горизонтали поворот (−1 влево … +1 вправо), по вертикали скорость.
            var locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.FreeformCartesian2D;
            tree.blendParameter = "Turn";
            tree.blendParameterY = "Speed";
            tree.AddChild(idle, new Vector2(0f, 0f));
            tree.AddChild(walk, new Vector2(0f, 1.2f));
            if (walkL != null) tree.AddChild(walkL, new Vector2(-1f, 1.1f));
            if (walkR != null) tree.AddChild(walkR, new Vector2(1f, 1.1f));
            tree.AddChild(trot, new Vector2(0f, 3f));
            if (trotL != null) tree.AddChild(trotL, new Vector2(-1f, 3f));
            if (trotR != null) tree.AddChild(trotR, new Vector2(1f, 3f));
            tree.AddChild(run, new Vector2(0f, 5.5f));
            if (runL != null) tree.AddChild(runL, new Vector2(-1f, 5.5f));
            if (runR != null) tree.AddChild(runR, new Vector2(1f, 5.5f));
            // Поворот на месте (этап 24): кот переступает лапами, а не крутится статуей.
            var turnL45 = M("Turn-45_L"); var turnR45 = M("Turn-45_R");
            var turnL90 = M("Turn-90_L"); var turnR90 = M("Turn-90_R");
            if (turnL45 != null) tree.AddChild(turnL45, new Vector2(-0.45f, 0f));
            if (turnR45 != null) tree.AddChild(turnR45, new Vector2(0.45f, 0f));
            if (turnL90 != null) tree.AddChild(turnL90, new Vector2(-1f, 0f));
            if (turnR90 != null) tree.AddChild(turnR90, new Vector2(1f, 0f));
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                var motion = children[i].motion;
                if (motion == turnL45 || motion == turnR45 || motion == turnL90 || motion == turnR90) children[i].timeScale = 2.6f;
            }
            tree.children = children;
            // Шаг подгоняется под настоящую скорость (CatVisual ставит StrideMul) — лапы не скользят по земле.
            locomotion.speedParameterActive = true;
            locomotion.speedParameter = "StrideMul";
            machine.defaultState = locomotion;

            // Подкрадывание (Ctrl): низко прижавшись к земле.
            AnimatorState crouchState = null;
            if (crouchIdle != null && crouchF != null)
            {
                crouchState = controller.CreateBlendTreeInController("Crouch", out BlendTree crouchTree, 0);
                crouchTree.blendType = BlendTreeType.FreeformCartesian2D;
                crouchTree.blendParameter = "Turn";
                crouchTree.blendParameterY = "Speed";
                crouchTree.AddChild(crouchIdle, new Vector2(0f, 0f));
                if (crouchTurnL != null) crouchTree.AddChild(crouchTurnL, new Vector2(-1f, 0f));
                if (crouchTurnR != null) crouchTree.AddChild(crouchTurnR, new Vector2(1f, 0f));
                crouchTree.AddChild(crouchF, new Vector2(0f, 1.8f));
                if (crouchL != null) crouchTree.AddChild(crouchL, new Vector2(-1f, 1.8f));
                if (crouchR != null) crouchTree.AddChild(crouchR, new Vector2(1f, 1.8f));

                var toCrouch = locomotion.AddTransition(crouchState);
                toCrouch.AddCondition(AnimatorConditionMode.If, 0, "Crouch");
                Setup(toCrouch, 0.2f, false);
                var fromCrouch = crouchState.AddTransition(locomotion);
                fromCrouch.AddCondition(AnimatorConditionMode.IfNot, 0, "Crouch");
                Setup(fromCrouch, 0.2f, false);
            }

            // Безделье: оглядывается, смотрит вниз, в сторону, зевает, тянется.
            var idleStates = new System.Collections.Generic.List<AnimatorState>();
            for (int i = 0; i < idleActions.Length; i++)
            {
                if (idleActions[i] == null) continue;
                var state = machine.AddState("Idle_" + idleActions[i].name);
                state.motion = idleActions[i];
                idleStates.Add(state);
                var toIdle = locomotion.AddTransition(state);
                toIdle.AddCondition(AnimatorConditionMode.If, 0, "IdleAction");
                toIdle.AddCondition(AnimatorConditionMode.Equals, i, "IdleVariant");
                toIdle.AddCondition(AnimatorConditionMode.Less, 0.2f, "Speed");
                Setup(toIdle, 0.3f, false);
                Setup(state.AddTransition(locomotion), 0.35f, true, 0.9f);
                var interrupt = state.AddTransition(locomotion);
                interrupt.AddCondition(AnimatorConditionMode.Greater, 0.4f, "Speed");
                Setup(interrupt, 0.15f, false);
            }

            // Отдых: садится, потом ложится. Любое движение — сразу встаёт (без долгой анимации подъёма).
            var restStates = new System.Collections.Generic.List<AnimatorState>();
            if (sitTo != null && sitIdle != null)
            {
                var sitDown = machine.AddState("SitDown"); sitDown.motion = sitTo;
                var sitting = machine.AddState("Sitting"); sitting.motion = sitIdle;
                restStates.Add(sitDown); restStates.Add(sitting);
                var toSit = locomotion.AddTransition(sitDown);
                toSit.AddCondition(AnimatorConditionMode.Greater, 0, "Rest");
                toSit.AddCondition(AnimatorConditionMode.Less, 0.2f, "Speed");
                Setup(toSit, 0.35f, false);
                Setup(sitDown.AddTransition(sitting), 0.2f, true, 0.95f);

                if (lieTo != null && lieIdle != null)
                {
                    var lieDown = machine.AddState("LieDown"); lieDown.motion = lieTo;
                    var lying = machine.AddState("Lying"); lying.motion = lieIdle;
                    restStates.Add(lieDown); restStates.Add(lying);
                    var toLie = sitting.AddTransition(lieDown);
                    toLie.AddCondition(AnimatorConditionMode.Equals, 2, "Rest");
                    Setup(toLie, 0.35f, false);
                    Setup(lieDown.AddTransition(lying), 0.2f, true, 0.95f);
                }

                foreach (var state in restStates)
                {
                    var stand = state.AddTransition(locomotion);
                    stand.AddCondition(AnimatorConditionMode.Equals, 0, "Rest");
                    Setup(stand, 0.25f, false);
                }
            }

            // Прыжок — один клип «в беге» для любой скорости (вариант «на месте» выглядел плохо).
            var jump = machine.AddState("Jump");
            jump.motion = jumpRun;
            jump.speed = 1.15f;
            var airborne = BlendState(controller, machine, "Airborne", "VerticalSpeed", (fallHigh, -9f), (fallLow, -2f));

            // Оглушение: кот не падает, а вздрагивает и трясёт головой.
            var stunState = machine.AddState("Stunned");
            stunState.motion = stunned;
            var dazedState = machine.AddState("Dazed");
            dazedState.motion = shaking;

            var toStun = machine.AddAnyStateTransition(stunState);
            toStun.AddCondition(AnimatorConditionMode.If, 0, "Knocked");
            toStun.canTransitionToSelf = false;
            Setup(toStun, 0.1f, false);
            Setup(stunState.AddTransition(dazedState), 0.2f, true, 0.85f);
            foreach (var state in new[] { stunState, dazedState })
            {
                var recover = state.AddTransition(locomotion);
                recover.AddCondition(AnimatorConditionMode.IfNot, 0, "Knocked");
                Setup(recover, 0.25f, false);
            }

            // Прыжок: стартуем сразу с толчка задними лапами, без приседания.
            var toJump = locomotion.AddTransition(jump);
            toJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");
            Setup(toJump, 0.1f, false);
            toJump.offset = jumpOffset;

            // Прыжок → полёт; низкий прыжок (на ступеньку) — сразу обратно в бег.
            Setup(jump.AddTransition(airborne), 0.25f, true, 0.8f);
            var jumpToRun = jump.AddTransition(locomotion);
            jumpToRun.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            // Без ожидания клипа: приземлились (и уже не летим вверх) — сразу в бег/стойку.
            jumpToRun.AddCondition(AnimatorConditionMode.Less, 0.5f, "VerticalSpeed");
            Setup(jumpToRun, 0.08f, false);

            // Сошёл с края — плавно в полёт; коснулся земли — мягко в бег, без отдельной анимации приземления.
            var toAir = locomotion.AddTransition(airborne);
            toAir.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            Setup(toAir, 0.25f, false);
            var airToRun = airborne.AddTransition(locomotion);
            airToRun.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            Setup(airToRun, 0.08f, false);

            // Из подкрадывания, безделья и отдыха — тоже можно прыгнуть или сорваться вниз.
            var extra = new System.Collections.Generic.List<AnimatorState>(idleStates);
            extra.AddRange(restStates);
            if (crouchState != null) extra.Add(crouchState);
            foreach (var state in extra)
            {
                var j = state.AddTransition(jump);
                j.AddCondition(AnimatorConditionMode.If, 0, "Jump");
                Setup(j, 0.1f, false);
                j.offset = jumpOffset;
                var fall = state.AddTransition(airborne);
                fall.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
                Setup(fall, 0.25f, false);
            }

            // ── Удары (этап 28) ──
            // Лапы чередуются (правая/левая). На бегу удар — только передняя часть тела поверх бега (слой Actions с маской),
            // с места — всё тело (слой ActionsFull): кот упирается задними лапами и разворачивает корпус.
            var pawL = Clip("A_Cat_Combat", "Paw_L") ?? paw;
            float peakR = FindPawPeak(source, paw, ".R");
            float peakL = pawL == paw ? peakR : FindPawPeak(source, pawL, ".L");
            float Offset(AnimationClip clip, float peak, float speed) => Mathf.Clamp01((peak - PushImpactDelay * speed) / clip.length);

            controller.AddLayer("Actions");
            controller.AddLayer("ActionsFull");
            var layers = controller.layers;
            layers[1].avatarMask = mask;
            layers[1].defaultWeight = 0f; // вес плавно включает CatVisual
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[2].avatarMask = null;
            layers[2].defaultWeight = 0f;
            layers[2].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            // Слой 1: на бегу (перед тела) + реакция на удар.
            var actions = controller.layers[1].stateMachine;
            var empty = actions.AddState("Empty");
            empty.writeDefaultValues = false;
            actions.defaultState = empty;
            var hit = actions.AddState("Hit");
            hit.motion = damage;
            hit.tag = "Action";
            var toHit = actions.AddAnyStateTransition(hit);
            toHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
            toHit.AddCondition(AnimatorConditionMode.IfNot, 0, "Knocked");
            toHit.canTransitionToSelf = false;
            Setup(toHit, 0.05f, false);
            Setup(hit.AddTransition(empty), 0.2f, true, 0.8f);
            var cancelHit = hit.AddTransition(empty);
            cancelHit.AddCondition(AnimatorConditionMode.If, 0, "Knocked");
            Setup(cancelHit, 0.1f, false);
            AddPushStates(actions, empty, "Push", "PushRun", RunPushSpeed, 0.03f, 0.62f,
                (paw, Offset(paw, peakR, RunPushSpeed)), (pawL, Offset(pawL, peakL, RunPushSpeed)));

            // Слой 2: с места (всё тело).
            var full = controller.layers[2].stateMachine;
            var fullEmpty = full.AddState("Empty");
            fullEmpty.writeDefaultValues = false;
            full.defaultState = fullEmpty;
            AddPushStates(full, fullEmpty, "PushStand", "PushStand", StandPushSpeed, 0.06f, 0.72f,
                (paw, Offset(paw, peakR, StandPushSpeed)), (pawL, Offset(pawL, peakL, StandPushSpeed)));

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>
        /// Контроллер НЕ удаляем: на него ссылается префаб кота, а пересозданный файл Unity может не подхватить
        /// (в игре аниматор оставался без контроллера). Вместо этого чистим содержимое и собираем заново.
        /// </summary>
        private static AnimatorController GetCleanController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) return AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            // Убираем старые слои, состояния, деревья смешивания и переходы (всё это — вложенные ассеты).
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            {
                if (asset != null && asset != controller && !(asset is AnimatorController))
                    Object.DestroyImmediate(asset, true);
            }

            controller.layers = new AnimatorControllerLayer[0];
            controller.parameters = new AnimatorControllerParameter[0];
            controller.AddLayer("Base Layer");
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorState BlendState(AnimatorController controller, AnimatorStateMachine machine, string name,
            string parameter, params (AnimationClip clip, float threshold)[] children)
        {
            var state = controller.CreateBlendTreeInController(name, out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = parameter;
            tree.useAutomaticThresholds = false;
            foreach (var (clip, threshold) in children) tree.AddChild(clip, threshold);
            return state;
        }

        private static void Setup(AnimatorStateTransition transition, float duration, bool exitTime, float exitAt = 0f)
        {
            transition.duration = duration;
            transition.hasExitTime = exitTime;
            transition.exitTime = exitAt;
            transition.hasFixedDuration = true;
            transition.interruptionSource = TransitionInterruptionSource.Destination;
        }

        // ───────────────────────── Анализ клипов ─────────────────────────

        /// <summary>Момент клипа, когда правая передняя лапа дальше всего вытянута вперёд.</summary>
        /// <summary>Два состояния удара (правая и левая лапа) по триггеру и PushSide; выход — по времени, отмена — при оглушении.</summary>
        private static void AddPushStates(AnimatorStateMachine machine, AnimatorState empty, string trigger, string name, float speed,
            float blendIn, float exitAt, params (AnimationClip clip, float offset)[] sides)
        {
            for (int side = 0; side < sides.Length; side++)
            {
                var state = machine.AddState(name + (side == 0 ? "R" : "L"));
                state.motion = sides[side].clip;
                state.speed = speed;
                state.tag = "Action";
                var to = machine.AddAnyStateTransition(state);
                to.AddCondition(AnimatorConditionMode.If, 0, trigger);
                to.AddCondition(AnimatorConditionMode.Equals, side, "PushSide");
                to.AddCondition(AnimatorConditionMode.IfNot, 0, "Knocked");
                to.canTransitionToSelf = true; // быстрые повторные толчки перезапускают лапу
                Setup(to, blendIn, false);
                to.offset = sides[side].offset;
                Setup(state.AddTransition(empty), 0.22f, true, exitAt);
                var cancel = state.AddTransition(empty);
                cancel.AddCondition(AnimatorConditionMode.If, 0, "Knocked");
                Setup(cancel, 0.1f, false);
            }
        }

        private static float FindPawPeak(GameObject source, AnimationClip clip, string side = ".R")
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                var hand = FindBone(instance.transform, "Hand", side) ?? FindBone(instance.transform, "Forearm", side);
                var spine = FindBone(instance.transform, "Spine", "");
                var head = FindBone(instance.transform, "Head", "");
                if (hand == null || spine == null || head == null) return clip.length * 0.4f;

                clip.SampleAnimation(instance, 0f);
                Vector3 forward = Vector3.ProjectOnPlane(head.position - spine.position, Vector3.up).normalized;

                float bestTime = clip.length * 0.4f, best = float.MinValue;
                for (float t = 0f; t <= clip.length; t += 1f / 60f)
                {
                    clip.SampleAnimation(instance, t);
                    float reach = Vector3.Dot(hand.position - spine.position, forward);
                    if (reach > best) { best = reach; bestTime = t; }
                }

                return bestTime;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Нижняя точка приседания перед прыжком (дальше кот уже отталкивается).</summary>
        private static float FindTakeoff(GameObject source, AnimationClip clip)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                var spine = FindBone(instance.transform, "Spine", "");
                if (spine == null) return 0f;

                float bestTime = 0f, lowest = float.MaxValue;
                for (float t = 0f; t <= clip.length * 0.6f; t += 1f / 60f)
                {
                    clip.SampleAnimation(instance, t);
                    if (spine.position.y < lowest) { lowest = spine.position.y; bestTime = t; }
                }

                return bestTime;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static Transform FindBone(Transform root, string name, string suffix)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name + suffix) return child;
            }

            return null;
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

        // ───────────────────────── Префаб ─────────────────────────

        private static bool SetupCatPrefab(AnimatorController controller)
        {
            string catPath = MaoshiSetupStage2.CatPrefabPath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(catPath) == null)
            {
                Debug.LogError("[Maoshi Setup] Нет префаба Cat. Сначала запусти Stage 2 и Stage 8.");
                return false;
            }

            var root = PrefabUtility.LoadPrefabContents(catPath);
            try
            {
                var animator = root.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    Debug.LogError("[Maoshi Setup] В префабе Cat нет Animator. Сначала запусти Stage 8.");
                    return false;
                }

                animator.runtimeAnimatorController = controller;

                // Сеть: чужого кота двигает NetworkTransform каждый кадр. Через физику (FixedUpdate) — рывки.
                var netRigidbody = root.GetComponent<NetworkRigidbody>();
                if (netRigidbody != null) netRigidbody.UseRigidBodyForMotion = false;

                var netTransform = root.GetComponent<NetworkTransform>();
                netTransform.PositionInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
                netTransform.RotationInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
                netTransform.PositionLerpSmoothing = true;
                netTransform.RotationLerpSmoothing = true;
                netTransform.PositionMaxInterpolationTime = 0.05f;
                netTransform.RotationMaxInterpolationTime = 0.05f;

                // Толчок и повороты.
                var cat = new SerializedObject(root.GetComponent<CatController>());
                Set(cat, "pushForce", 3.5f);
                Set(cat, "pushUpForce", 1f);
                Set(cat, "pushLunge", 1.5f);
                Set(cat, "pushImpactDelay", PushImpactDelay);
                Set(cat, "propPushMultiplier", 0.5f);
                Set(cat, "turnSharpness", 6f);
                Set(cat, "acceleration", 32f);
                Set(cat, "deceleration", 26f);
                Set(cat, "skidDeceleration", 30f);
                Set(cat, "maxTurnSpeedSlow", 540f);
                Set(cat, "maxTurnSpeedFast", 210f);
                Set(cat, "crouchSpeed", 2.2f);
                Set(cat, "maxSeparationSpeed", 4f);
                Set(cat, "knockdownDuration", 1f);
                Set(cat, "hitImmunityAfterKnock", 0.6f);
                Set(cat, "maxHitSpeed", 5f);
                cat.ApplyModifiedPropertiesWithoutUndo();

                // Визуал: своё сглаживание выключено — хватает NetworkTransform.
                var visual = new SerializedObject(root.GetComponent<CatVisual>());
                var smooth = visual.FindProperty("smoothRemoteModel");
                if (smooth != null) smooth.boolValue = false;
                visual.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, catPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return true;
        }

        private static void Set(SerializedObject serialized, string name, float value)
        {
            var property = serialized.FindProperty(name);
            if (property != null) property.floatValue = value;
            else Debug.LogWarning("[Maoshi Setup] Нет поля " + name);
        }
    }
}

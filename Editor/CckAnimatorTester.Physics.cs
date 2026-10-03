#if CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ABI.CCK.Components;
using MagicaCloth2;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    // The physics card: moves the avatar the way a player would, lets a chain
    // be dragged and let go, and draws every chain at once. Play mode only,
    // so nothing it changes outlives Play mode. MagicaCloth2 members newer
    // than its first release are reached by name, so an older install still
    // compiles and the control that needs them just does nothing.
    public partial class CckAnimatorTester
    {
        internal enum Moves { None, Walk, Run, Turn, Jump, Shake, Sit }

        Moves _motion;
        Transform _moved;
        Vector3 _motionOrigin;
        Quaternion _motionFacing;
        float _motionStart;
        bool _airborne;

        bool _grab;
        MagicaCloth _held;
        Transform _heldBone;
        Vector3 _heldPoint;
        Vector3 _flingVelocity;
        double _lastDrag;

        bool _overlay;
        readonly Dictionary<MagicaCloth, (bool always, bool enable)> _gizmosBefore =
            new Dictionary<MagicaCloth, (bool, bool)>();
        // Each chain bone's local pose before Play, by full scene path and for
        // every avatar, so one picked mid-Play is measured from its own.
        // The swing bound is measured from where a bone hangs at rest, and
        // Play mode has already moved them by the time anyone looks.
        // Serialized, so the domain reload on entering Play keeps them.
        [SerializeField] List<string> _restPaths = new List<string>();
        [SerializeField] List<Vector3> _restPositions = new List<Vector3>();
        [SerializeField] List<Quaternion> _restRotations = new List<Quaternion>();
        Dictionary<string, (Vector3 position, Quaternion rotation)> _rest;
        // Each drawn bone's entry in Rest, found by path once instead of on
        // every repaint. Null for a bone with none: that one reads its live
        // pose each time, so the miss is what gets kept, never the pose.
        readonly Dictionary<Transform, (Vector3, Quaternion)?> _restByBone =
            new Dictionary<Transform, (Vector3, Quaternion)?>();

        Dictionary<string, (Vector3 position, Quaternion rotation)> Rest
        {
            get
            {
                if (_rest == null)
                {
                    _rest = new Dictionary<string, (Vector3, Quaternion)>();
                    for (int i = 0; i < _restPaths.Count; i++)
                    {
                        _rest[_restPaths[i]] = (_restPositions[i], _restRotations[i]);
                    }
                }
                return _rest;
            }
        }

        // onBeforeRender fires only while a game camera renders, so with the
        // Game view hidden behind a tab nothing moved and no grab pulled. The
        // editor tick steps too; whichever comes first in a frame does it.
        partial void EnablePhysics()
        {
            Application.onBeforeRender += StepPhysics;
            EditorApplication.update += StepPhysics;
            SceneView.duringSceneGui += PhysicsSceneGui;
        }

        partial void DisablePhysics()
        {
            Application.onBeforeRender -= StepPhysics;
            EditorApplication.update -= StepPhysics;
            SceneView.duringSceneGui -= PhysicsSceneGui;
        }

        int _steppedFrame = -1;

        partial void PhysicsPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                CaptureRest();
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
            {
                _motion = Moves.None;
                _moved = null;
                _held = null;
                _gizmosBefore.Clear();
                _restByBone.Clear();
            }
        }

        partial void AddPhysicsCard(VisualElement scroll, CVRAvatar avatar, bool live)
        {
            scroll.Add(SafeCard("Physics", () => BuildPhysicsCard(avatar, live)));
        }

        VisualElement BuildPhysicsCard(CVRAvatar avatar, bool live)
        {
            var card = new BridgeElements.Card("Physics  (MagicaCloth)");
            var cloths = Cloths(avatar);
            card.Body.Add(BridgeElements.Hint(cloths.Length == 0
                ? "No MagicaCloth on this avatar, so nothing here moves."
                : $"{cloths.Length} cloth component(s). Move the avatar and watch them swing, drag one in the " +
                  "Scene view, or draw them all. Play mode only; everything resets when it ends."));

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 4;
            foreach (var (motion, tip) in new[]
            {
                (Moves.Walk, "Walks a circle 1.5 m across at walking pace, the locomotion parameters along with it."),
                (Moves.Run, "Runs a wider circle at running pace."),
                (Moves.Turn, "Spins on the spot, half a turn a second."),
                (Moves.Jump, "Hops in place, Grounded off while in the air."),
                (Moves.Shake, "Twists side to side three times a second, as a head shake drives hair."),
                (Moves.Sit, "Sits still, Sitting on, so chains settle in the seated pose."),
            })
            {
                var captured = motion;
                // Resolved at the click, as the animator it drives is.
                var button = new Button(() => StartMotion(ResolveAvatar(), captured)) { text = motion.ToString(), tooltip = tip };
                button.style.marginBottom = 2;
                row.Add(button);
            }
            row.Add(new Button(() => StopMotion()) { text = "Stop", tooltip = "Back to where it started, standing." });
            card.Body.Add(row);

            var grab = new Toggle("Grab chains in the Scene view")
            {
                value = _grab,
                tooltip = "Left-drag near a chain to pull it; let go to fling it. Pulls the whole cloth component " +
                          "the bone belongs to, through MagicaCloth's own force.",
            };
            grab.RegisterValueChangedCallback(e => { _grab = e.newValue; SceneView.RepaintAll(); });
            card.Body.Add(grab);

            var overlay = new Toggle("Draw every chain")
            {
                value = _overlay,
                tooltip = "MagicaCloth's own particles, radius and colliders for every chain at once, with each " +
                          "chain's name and its swing bound: the sphere a bone may not leave, around where it hangs at rest.",
            };
            overlay.RegisterValueChangedCallback(e => SetOverlay(ResolveAvatar(), e.newValue));
            card.Body.Add(overlay);
            // The gizmos belong to the avatar ticked for. Another one resolved
            // since, or a new Play session, needs them to match the box.
            if (live && _overlay) SetOverlay(avatar, true);

            card.SetEnabled(live && cloths.Length > 0);
            return card;
        }

        static MagicaCloth[] Cloths(CVRAvatar avatar) =>
            avatar != null ? avatar.GetComponentsInChildren<MagicaCloth>(true) : new MagicaCloth[0];

        // ---- motion ------------------------------------------------------------------

        void StartMotion(CVRAvatar avatar, Moves motion)
        {
            if (avatar == null)
            {
                Debug.LogWarning("[AvatarBridge] No avatar to move: select it, or have one CVRAvatar active in the scene.");
                return;
            }
            if (_moved != avatar.transform || _motion == Moves.None)
            {
                _moved = avatar.transform;
                _motionOrigin = _moved.position;
                _motionFacing = Quaternion.Euler(0f, _moved.eulerAngles.y, 0f);
            }
            _motion = motion;
            _motionStart = Time.time;
            _airborne = false;
            var a = LiveAnimator();
            var (speed, forward) = MotionSpeed(motion);
            Drive(a, "MovementY", forward);
            Drive(a, "VelocityZ", speed);
            Drive(a, "Grounded", 1f);
            Drive(a, "Sitting", motion == Moves.Sit ? 1f : 0f);
        }

        void StopMotion()
        {
            if (_moved != null)
            {
                _moved.SetPositionAndRotation(_motionOrigin, _motionFacing);
            }
            _motion = Moves.None;
            var a = LiveAnimator();
            Drive(a, "MovementY", 0f);
            Drive(a, "VelocityZ", 0f);
            Drive(a, "Grounded", 1f);
            Drive(a, "Sitting", 0f);
        }

        static (float speed, float forward) MotionSpeed(Moves motion) =>
            motion == Moves.Walk ? (1.4f, 0.5f) : motion == Moves.Run ? (4f, 1f) : (0f, 0f);

        // Where the avatar is, t seconds into a motion, relative to where it
        // started and the way it faced. Circles, not back and forth: an
        // instant about-turn is a teleport to the cloth, which a player never does.
        internal static (Vector3 offset, float yaw) MotionPose(Moves motion, float t)
        {
            switch (motion)
            {
                case Moves.Walk:
                case Moves.Run:
                {
                    float radius = motion == Moves.Walk ? 0.75f : 1.5f;
                    float angle = MotionSpeed(motion).speed / radius * t;
                    var offset = new Vector3(radius * (1f - Mathf.Cos(angle)), 0f, radius * Mathf.Sin(angle));
                    return (offset, angle * Mathf.Rad2Deg);
                }
                case Moves.Turn:
                    return (Vector3.zero, 180f * t);
                case Moves.Jump:
                {
                    float phase = Mathf.Repeat(t, 1.2f) / 0.6f;
                    return (new Vector3(0f, phase < 1f ? 1.8f * phase * (1f - phase) : 0f, 0f), 0f);
                }
                case Moves.Shake:
                    return (Vector3.zero, 25f * Mathf.Sin(t * 2f * Mathf.PI * 3f));
                default:
                    return (Vector3.zero, 0f);
            }
        }

        // Inside the player loop, as the face weights are: an editor-update
        // write lands somewhere around the frame instead of before it.
        void StepPhysics()
        {
            if (!Application.isPlaying || Time.frameCount == _steppedFrame) return;
            _steppedFrame = Time.frameCount;
            if (_motion != Moves.None && _motion != Moves.Sit && _moved != null)
            {
                var (offset, yaw) = MotionPose(_motion, Time.time - _motionStart);
                _moved.SetPositionAndRotation(_motionOrigin + _motionFacing * offset,
                    _motionFacing * Quaternion.Euler(0f, yaw, 0f));
                bool airborne = offset.y > 0.001f;
                if (airborne != _airborne)
                {
                    _airborne = airborne;
                    Drive(LiveAnimator(), "Grounded", airborne ? 0f : 1f);
                }
            }
            if (_held != null && _heldBone != null)
            {
                var pull = _heldPoint - _heldBone.position;
                Push(_held, pull, Mathf.Min(pull.magnitude * 10f, 6f));
            }
        }

        // ---- grab and fling ------------------------------------------------------------

        void PhysicsSceneGui(SceneView view)
        {
            if (!Application.isPlaying || (!_overlay && !_grab)) return;
            // The overlay draws on Repaint alone, so without grabbing no other
            // event needs the avatar, and resolving it can search the scene.
            if (!_grab && Event.current.type != EventType.Repaint) return;
            var avatar = ResolveAvatar();
            if (_overlay) DrawChains(avatar);
            if (!_grab || avatar == null) return;

            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(id);
                    break;
                case EventType.MouseDown when e.button == 0 && !e.alt:
                    if (PickBone(avatar, e.mousePosition, out _held, out _heldBone))
                    {
                        _heldPoint = _heldBone.position;
                        _flingVelocity = Vector3.zero;
                        _lastDrag = EditorApplication.timeSinceStartup;
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                {
                    var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                    if (new Plane(-view.camera.transform.forward, _heldPoint).Raycast(ray, out float enter))
                    {
                        var next = ray.GetPoint(enter);
                        double now = EditorApplication.timeSinceStartup;
                        float dt = Mathf.Max(0.005f, (float)(now - _lastDrag));
                        _flingVelocity = Vector3.Lerp(_flingVelocity, (next - _heldPoint) / dt, 0.5f);
                        _heldPoint = next;
                        _lastDrag = now;
                    }
                    e.Use();
                    break;
                }
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    if (_held != null)
                    {
                        Push(_held, _flingVelocity, Mathf.Min(_flingVelocity.magnitude, 10f));
                    }
                    _held = null;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.Repaint when _held != null && _heldBone != null:
                    Handles.color = Color.yellow;
                    Handles.DrawLine(_heldBone.position, _heldPoint);
                    Handles.DrawWireDisc(_heldPoint, view.camera.transform.forward, 0.02f);
                    break;
            }
        }

        // The chain bone nearest the cursor, within a reach that does not
        // steal clicks meant for the rest of the Scene view.
        static bool PickBone(CVRAvatar avatar, Vector2 mouse, out MagicaCloth cloth, out Transform bone)
        {
            cloth = null;
            bone = null;
            float best = 24f;
            foreach (var c in Cloths(avatar))
            {
                foreach (var t in ChainBones(c))
                {
                    float d = Vector2.Distance(HandleUtility.WorldToGUIPoint(t.position), mouse);
                    if (d < best)
                    {
                        best = d;
                        cloth = c;
                        bone = t;
                    }
                }
            }
            return cloth != null;
        }

        // MagicaCloth's AddForce, by name: it came after the first 2.x
        // releases. A velocity change, weighted toward the tip as a hand is.
        internal static bool Push(MagicaCloth cloth, Vector3 direction, float speed)
        {
            if (cloth == null || speed <= 0f || direction.sqrMagnitude < 1e-8f) return false;
            var add = cloth.GetType().GetMethod("AddForce", BindingFlags.Public | BindingFlags.Instance);
            var parameters = add?.GetParameters();
            if (parameters == null || parameters.Length != 3 || !parameters[2].ParameterType.IsEnum) return false;
            var mode = System.Enum.Parse(parameters[2].ParameterType, "VelocityAdd");
            add.Invoke(cloth, new object[] { direction.normalized, speed, mode });
            return true;
        }

        // Every bone a cloth simulates: its roots and everything under them.
        static IEnumerable<Transform> ChainBones(MagicaCloth cloth)
        {
            foreach (var root in cloth.SerializeData.rootBones.Where(r => r != null))
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    yield return t;
                }
            }
        }

        // ---- overlay ------------------------------------------------------------------

        void SetOverlay(CVRAvatar avatar, bool on)
        {
            _overlay = on;
            _restByBone.Clear();
            foreach (var cloth in Cloths(avatar))
            {
                var gizmos = Member(cloth, "GizmoSerializeData");
                var debug = Member(gizmos, "clothDebugSettings");
                if (gizmos == null || debug == null) continue;
                if (on)
                {
                    if (!_gizmosBefore.ContainsKey(cloth))
                    {
                        _gizmosBefore[cloth] = ((bool)(Member(gizmos, "always") ?? false),
                            (bool)(Member(debug, "enable") ?? false));
                    }
                    SetMember(gizmos, "always", true);
                    SetMember(debug, "enable", true);
                    SetMember(debug, "position", true);
                    SetMember(debug, "radius", true);
                    SetMember(debug, "collider", true);
                }
                else if (_gizmosBefore.TryGetValue(cloth, out var before))
                {
                    SetMember(gizmos, "always", before.always);
                    SetMember(debug, "enable", before.enable);
                }
            }
            if (!on) _gizmosBefore.Clear();
            if (_restPaths.Count == 0) CaptureRest();
            SceneView.RepaintAll();
        }

        void CaptureRest()
        {
            _restPaths.Clear();
            _restPositions.Clear();
            _restRotations.Clear();
            _rest = null;
            _restByBone.Clear();
            foreach (var cloth in FindObjectsOfType<CVRAvatar>(true).SelectMany(a => Cloths(a)))
            {
                foreach (var t in ChainBones(cloth))
                {
                    _restPaths.Add(PathUnder(null, t));
                    _restPositions.Add(t.localPosition);
                    _restRotations.Add(t.localRotation);
                }
            }
        }

        static string PathUnder(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var at = t; at != null && at != root; at = at.parent) parts.Add(at.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // Names, and each bone's swing bound: MagicaCloth draws the rest.
        void DrawChains(CVRAvatar avatar)
        {
            if (Event.current.type != EventType.Repaint || avatar == null) return;
            foreach (var cloth in Cloths(avatar))
            {
                var data = cloth.SerializeData;
                foreach (var root in data.rootBones.Where(r => r != null))
                {
                    Handles.color = Color.white;
                    Handles.Label(root.position, cloth.name.Replace("MagicaCloth_", ""), EditorStyles.whiteMiniLabel);
                }
                var motion = Member(data, "motionConstraint");
                if (!(Member(motion, "useMaxDistance") is bool use) || !use) continue;
                var maxDistance = Member(motion, "maxDistance");
                foreach (var root in data.rootBones.Where(r => r != null))
                {
                    int deepest = Mathf.Max(1, Deepest(root));
                    DrawBounds(root, root.localToWorldMatrix, 0, deepest, maxDistance);
                }
            }
        }

        void DrawBounds(Transform bone, Matrix4x4 restMatrix, int depth, int deepest, object maxDistance)
        {
            if (depth > 0)
            {
                Vector3 rest = restMatrix.GetColumn(3);
                float bound = Curve(maxDistance, depth / (float)deepest);
                float off = Vector3.Distance(rest, bone.position);
                Handles.color = off > bound * 0.95f ? new Color(1f, 0.55f, 0.1f) : new Color(0.3f, 0.9f, 0.5f, 0.8f);
                Handles.DrawLine(rest, bone.position);
                foreach (var axis in Axes)
                {
                    Handles.DrawWireDisc(rest, axis, bound);
                }
            }
            // Indexed, not foreach: Transform's enumerator allocates, per bone per repaint.
            for (int i = 0; i < bone.childCount; i++)
            {
                var child = bone.GetChild(i);
                var restLocal = RestOf(child) ?? (child.localPosition, child.localRotation);
                var childMatrix = restMatrix * Matrix4x4.TRS(restLocal.Item1, restLocal.Item2, child.localScale);
                DrawBounds(child, childMatrix, depth + 1, deepest, maxDistance);
            }
        }

        static readonly Vector3[] Axes = { Vector3.up, Vector3.right, Vector3.forward };

        (Vector3, Quaternion)? RestOf(Transform bone)
        {
            if (!_restByBone.TryGetValue(bone, out var pose))
            {
                _restByBone[bone] = pose = Rest.TryGetValue(PathUnder(null, bone), out var r)
                    ? r : ((Vector3, Quaternion)?)null;
            }
            return pose;
        }

        static int Deepest(Transform t)
        {
            int deepest = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                deepest = Mathf.Max(deepest, 1 + Deepest(t.GetChild(i)));
            }
            return deepest;
        }

        // A MagicaCloth curve setting: a value, scaled by a curve over depth when it has one.
        static float Curve(object curve, float depth)
        {
            float value = Member(curve, "value") is float v ? v : 0f;
            return Member(curve, "useCurve") is bool use && use && Member(curve, "curve") is AnimationCurve c
                ? value * c.Evaluate(depth)
                : value;
        }

        static object Member(object o, string name)
        {
            if (o == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            var type = o.GetType();
            return type.GetField(name, flags)?.GetValue(o) ?? type.GetProperty(name, flags)?.GetValue(o);
        }

        static void SetMember(object o, string name, object value) =>
            o?.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)?.SetValue(o, value);
    }
}
#endif

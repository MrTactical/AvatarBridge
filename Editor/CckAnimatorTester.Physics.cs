#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
#if AVATARBRIDGE_MAGICA
using MagicaCloth2;
#endif
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    // The physics card: moves the avatar the way a player would, lets a chain
    // be dragged and let go, draws every chain at once with its settings, and
    // carries an Inspector edit made in Play mode out of it on request.
    // MagicaCloth2 and DynamicBone each sit behind their own define, so the
    // card builds with either, both or neither. Members newer than a solver's
    // first release are reached by name, so an older install still compiles
    // and the control that needs them just does nothing.
    public partial class CckAnimatorTester
    {
        internal enum Moves { None, Walk, Run, Turn, Jump, Shake, Sit }

        // Named in the card's text for whichever solvers this project has.
        static readonly string Solvers =
#if AVATARBRIDGE_MAGICA && AVATARBRIDGE_DYNBONE
            "MagicaCloth or DynamicBone";
#elif AVATARBRIDGE_MAGICA
            "MagicaCloth";
#elif AVATARBRIDGE_DYNBONE
            "DynamicBone";
#else
            null;
#endif

        Moves _motion;
        Transform _moved;
        Vector3 _motionOrigin;
        Quaternion _motionFacing;
        float _motionStart;
        bool _airborne;

        bool _grab;
        Component _held;
        Transform _heldBone;
        Vector3 _heldPoint;
        Vector3 _flingVelocity;
        double _lastDrag;
#if AVATARBRIDGE_DYNBONE
        // DynamicBone has no impulse, only m_Force, added to every particle
        // each step: a hold is a force while dragging, a fling one that fades.
        // It already carries the converter's gravity, put back exactly after.
        DynamicBone _forced;
        Vector3 _forceBefore;
        Vector3 _fling;
        float _flungAt;
        const float FlingTime = 0.2f;
#endif

        bool _overlay;
        // MagicaCloth's own gizmo switches, as they were before the overlay.
        readonly Dictionary<Component, (bool always, bool enable)> _gizmosBefore =
            new Dictionary<Component, (bool, bool)>();
        // Each label the last repaint drew, per Scene view, so a click can find
        // its chain. Every open view repaints, each with its own rectangles.
        readonly Dictionary<SceneView, List<(Rect rect, Component chain)>> _labels =
            new Dictionary<SceneView, List<(Rect, Component)>>();

        // Inspector edits this Play session. Every physics component the
        // selection has shown is hashed a few times a second; a change is an
        // edit, handed to the solver and listed with a Keep button.
        readonly Dictionary<Component, int> _seen = new Dictionary<Component, int>();
        readonly List<Component> _edited = new List<Component>();
        double _nextEditPoll;
        VisualElement _editedRows;

        // What Keep recorded, written into the scene once Play mode ends. In
        // SessionState and applied by a static hook, so neither the domain
        // reload around Play mode nor closing the tester first loses it.
        [System.Serializable]
        class KeptRecords
        {
            public List<string> ids = new List<string>();
            public List<int> instances = new List<int>();
            public List<string> json = new List<string>();
        }

        const string KeptKey = "AvatarBridge.KeptPhysics";

        static KeptRecords LoadKept() =>
            JsonUtility.FromJson<KeptRecords>(SessionState.GetString(KeptKey, "{}")) ?? new KeptRecords();

        static void SaveKept(KeptRecords kept) => SessionState.SetString(KeptKey, JsonUtility.ToJson(kept));

        [InitializeOnLoadMethod]
        static void HookKept() =>
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredEditMode) ApplyKept();
            };

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
#if AVATARBRIDGE_DYNBONE
                _forced = null;
#endif
                _gizmosBefore.Clear();
                _restByBone.Clear();
                _labels.Clear();
                _seen.Clear();
                _edited.Clear();
            }
        }

        partial void AddPhysicsCard(VisualElement scroll, CVRAvatar avatar, bool live)
        {
            scroll.Add(SafeCard("Physics", () => BuildPhysicsCard(avatar, live)));
        }

        VisualElement BuildPhysicsCard(CVRAvatar avatar, bool live)
        {
            var card = new BridgeElements.Card("Physics");
            if (Solvers == null)
            {
                card.Body.Add(BridgeElements.Hint(
                    "No supported physics package is installed: this card reads MagicaCloth2 and DynamicBone."));
                card.SetEnabled(false);
                return card;
            }
            var chains = Chains(avatar);
            card.SetSummary(chains.Count == 0
                ? null
                : string.Join(", ", chains.GroupBy(c => c.GetType().Name).Select(g => $"{g.Count()} {g.Key}")));
            card.Body.Add(BridgeElements.Hint(chains.Count == 0
                ? $"No {Solvers} on this avatar, so nothing here moves."
                : "Move the avatar and watch its chains swing, drag one in the Scene view, or draw them all with " +
                  "their settings and click one to tune it. Play mode only; everything resets when it ends " +
                  "unless you keep an edit."));

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
                tooltip = "Dots mark the bones you can take hold of; rings mark the roots the solver holds still, " +
                          "which cannot be pulled. Left-drag near a dot to pull it; let go to fling it. Ctrl-click " +
                          "(Cmd on a Mac) selects its component instead. The force moves a whole component, so the " +
                          "bones of the one under the cursor light up: every chain in it moves together.",
            };
            grab.RegisterValueChangedCallback(e => { _grab = e.newValue; SceneView.RepaintAll(); });
            card.Body.Add(grab);

            var overlay = new Toggle("Draw every chain")
            {
                value = _overlay,
                tooltip = "Every chain at once: its bones, radius, colliders and swing bound (the sphere a bone may " +
                          "not leave, around where it hangs at rest), and a label with its solver and the settings " +
                          "that decide how it moves. Green runs normally; amber is switched off or hidden, not running, or " +
                          "has a bone stretched past half its length again; red has a bone flown off or invalid. " +
                          "Click a label to select that component and tune it live.",
            };
            overlay.RegisterValueChangedCallback(e => SetOverlay(ResolveAvatar(), e.newValue));
            card.Body.Add(overlay);
            // The gizmos belong to the avatar ticked for. Another one resolved
            // since, or a new Play session, needs them to match the box.
            if (live && _overlay) SetOverlay(avatar, true);

            _editedRows = new VisualElement();
            card.Body.Add(_editedRows);
            ShowEdited();

            card.SetEnabled(live && chains.Count > 0);
            return card;
        }

        // ---- the solvers --------------------------------------------------------------

        static List<Component> Chains(CVRAvatar avatar)
        {
            var found = new List<Component>();
            if (avatar == null) return found;
#if AVATARBRIDGE_MAGICA
            found.AddRange(avatar.GetComponentsInChildren<MagicaCloth>(true));
#endif
#if AVATARBRIDGE_DYNBONE
            found.AddRange(avatar.GetComponentsInChildren<DynamicBone>(true));
#endif
            return found;
        }

        static bool IsChain(Component c) =>
#if AVATARBRIDGE_MAGICA
            c is MagicaCloth ||
#endif
#if AVATARBRIDGE_DYNBONE
            c is DynamicBone ||
#endif
            false;

        static bool Running(Component c) => c is Behaviour b && b.isActiveAndEnabled;

        // What the solver holds still and hangs the rest of the chain from.
        static List<Transform> Roots(Component c)
        {
            var roots = new List<Transform>();
#if AVATARBRIDGE_MAGICA
            if (c is MagicaCloth cloth) roots.AddRange(cloth.SerializeData.rootBones);
#endif
#if AVATARBRIDGE_DYNBONE
            if (c is DynamicBone db)
            {
                roots.Add(db.m_Root);
                // m_Roots came with DynamicBone 1.3.
                if (Member(db, "m_Roots") is IEnumerable<Transform> more) roots.AddRange(more);
            }
#endif
            roots.RemoveAll(r => r == null);
            return roots.Distinct().ToList();
        }

        static readonly HashSet<Transform> NoExclusions = new HashSet<Transform>();

        static HashSet<Transform> Excluded(Component c)
        {
#if AVATARBRIDGE_DYNBONE
            if (c is DynamicBone db && db.m_Exclusions != null)
            {
                return new HashSet<Transform>(db.m_Exclusions.Where(t => t != null));
            }
#endif
            return NoExclusions;
        }

        // Every bone a chain simulates: its roots and everything under them it does not exclude.
        static List<Transform> ChainBones(Component c)
        {
            var bones = new List<Transform>();
            var excluded = Excluded(c);
            foreach (var root in Roots(c)) Collect(root, excluded, bones);
            return bones;
        }

        static void Collect(Transform t, HashSet<Transform> excluded, List<Transform> into)
        {
            into.Add(t);
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (!excluded.Contains(child)) Collect(child, excluded, into);
            }
        }

        // One line of what decides how a chain moves, read off the component.
        static string Readout(Component c)
        {
#if AVATARBRIDGE_MAGICA
            if (c is MagicaCloth cloth)
            {
                var data = cloth.SerializeData;
                var restore = Member(data, "angleRestorationConstraint");
                var limit = Member(data, "angleLimitConstraint");
                int colliders = (Member(Member(data, "colliderCollisionConstraint"), "colliderList") as System.Collections.IList)?
                    .Cast<Object>().Count(o => o != null) ?? 0;
                string restoring = Member(restore, "useAngleRestoration") is true
                    ? $"{Member(Member(restore, "stiffness"), "value"):0.##}" : "off";
                string limited = Member(limit, "useAngleLimit") is true
                    ? $"{Member(Member(limit, "limitAngle"), "value"):0}°" : "off";
                return $"{Member(data, "clothType")}  grav {Member(data, "gravity"):0.##}  " +
                       $"damp {Member(Member(data, "damping"), "value"):0.###}  restore {restoring}  limit {limited}  " +
                       $"r {Member(Member(data, "radius"), "value"):0.###}  col {colliders}";
            }
#endif
#if AVATARBRIDGE_DYNBONE
            if (c is DynamicBone db)
            {
                int colliders = db.m_Colliders != null ? db.m_Colliders.Count(x => x != null) : 0;
                return $"DynamicBone  damp {db.m_Damping:0.###}  elast {db.m_Elasticity:0.###}  " +
                       $"stiff {db.m_Stiffness:0.##}  inert {db.m_Inert:0.##}  r {db.m_Radius:0.###}  col {colliders}";
            }
#endif
            return "";
        }

        // What an edit is and what Keep carries. MagicaCloth's SerializeData is
        // the part its own docs call rewritable at runtime, which leaves out the
        // gizmo switches the overlay flips and the build data. A DynamicBone whole.
        static object Tunable(Component c)
        {
#if AVATARBRIDGE_MAGICA
            if (c is MagicaCloth cloth) return cloth.SerializeData;
#endif
            return c;
        }

        // Toggles animate a chain component's m_Enabled, so it is left out: a
        // toggle is not an edit, and a Keep must not write the Play-mode on/off
        // state over the scene's default. FromJsonOverwrite leaves a missing field alone.
        static readonly Regex EnabledField = new Regex("\"m_Enabled\":[^,}]*,?");

        static string Json(Component c) => EnabledField.Replace(EditorJsonUtility.ToJson(Tunable(c)), "", 1);

        // MagicaCloth2 takes new settings into its solver on SetParameterChange,
        // DynamicBone into its particles on UpdateParameters. By name: neither
        // is in every version. Each solver's OnValidate does the same for an
        // Inspector edit in the versions here; this covers the rest.
        static void Refresh(Component c)
        {
            foreach (var name in new[] { "SetParameterChange", "UpdateParameters" })
            {
                c.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, System.Type.EmptyTypes, null)
                    ?.Invoke(c, null);
            }
        }

        static void Select(Component c)
        {
            Selection.activeObject = c;
            EditorGUIUtility.PingObject(c);
        }

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
            if (!Application.isPlaying) return;
            // Before the frame check: a paused game is when edits get made.
            PollEdits();
            if (Time.frameCount == _steppedFrame) return;
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
                Hold(_heldPoint - _heldBone.position);
            }
#if AVATARBRIDGE_MAGICA
            if (_flung != null)
            {
                if (Time.time < _flungUntil) Push(_flung, _flungVelocity, Mathf.Min(_flungVelocity.magnitude, 25f));
                else _flung = null;
            }
#endif
#if AVATARBRIDGE_DYNBONE
            else if (_forced != null)
            {
                Fade();
            }
#endif
        }

        // ---- grab and fling ------------------------------------------------------------

        void PhysicsSceneGui(SceneView view)
        {
            if (!Application.isPlaying || (!_overlay && !_grab)) return;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var type = e.GetTypeForControl(id);
            // From the rectangles this view's last repaint left: no avatar needed.
            var label = type == EventType.Layout || type == EventType.MouseDown ? LabelAt(view, e.mousePosition) : null;
            switch (type)
            {
                case EventType.Layout:
                    // The default control while grabbing or over a label, so the
                    // click is the card's and not the Scene view's selection.
                    if (_grab || label != null) HandleUtility.AddDefaultControl(id);
                    return;
                // A label lies over the bones beside its root; a bone in reach wins.
                case EventType.MouseDown when e.button == 0 && !e.alt && label != null
                                              && !(_grab && PickBone(ResolveAvatar(), e.mousePosition, out _, out _)):
                    Select(label);
                    GUIUtility.hotControl = id;
                    e.Use();
                    return;
                case EventType.MouseUp when GUIUtility.hotControl == id && _held == null:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    return;
            }
            // The overlay draws on Repaint alone, so without grabbing no other
            // event needs the avatar, and resolving it can search the scene.
            if (!_grab && type != EventType.Repaint) return;
            var avatar = ResolveAvatar();
            if (type == EventType.Repaint && _overlay) DrawChains(avatar, view);
            if (!_grab || avatar == null) return;

            switch (type)
            {
                case EventType.MouseMove:
                    view.Repaint();
                    break;
                case EventType.MouseDown when e.button == 0 && !e.alt:
                    if (PickBone(avatar, e.mousePosition, out var chain, out var bone))
                    {
                        // Ctrl, Cmd on a Mac, hands the component to the Inspector instead of pulling it.
                        if (EditorGUI.actionKey) Select(chain);
                        else Take(chain, bone);
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                {
                    var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                    if (_held != null && new Plane(-view.camera.transform.forward, _heldPoint).Raycast(ray, out float enter))
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
                    LetGo();
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.Repaint:
                    if (_held == null)
                    {
                        DrawGrabbable(avatar, e.mousePosition, view.camera);
                    }
                    else if (_heldBone != null)
                    {
                        Handles.color = Color.yellow;
                        Handles.DrawLine(_heldBone.position, _heldPoint);
                        Handles.DrawWireDisc(_heldPoint, view.camera.transform.forward, 0.02f);
                    }
                    break;
            }
        }

        Component LabelAt(SceneView view, Vector2 mouse)
        {
            if (!_overlay || !_labels.TryGetValue(view, out var labels)) return null;
            foreach (var (rect, chain) in labels)
            {
                if (chain != null && rect.Contains(mouse)) return chain;
            }
            return null;
        }

        // Where a drag can take hold, and what it would pull. The force goes to
        // a whole component, so every bone of the one under the cursor lights
        // up: two chains sharing a component move together.
        static void DrawGrabbable(CVRAvatar avatar, Vector2 mouse, Camera camera)
        {
            PickBone(avatar, mouse, out var hovered, out var hoveredBone);
            foreach (var chain in Chains(avatar).Where(Running))
            {
                bool lit = chain == hovered;
                Handles.color = lit ? Color.yellow : new Color(1f, 1f, 1f, 0.4f);
                var roots = Roots(chain);
                var bones = ChainBones(chain);
                var inChain = new HashSet<Transform>(bones);
                foreach (var t in bones)
                {
                    float size = HandleUtility.GetHandleSize(t.position) * (lit ? 0.03f : 0.018f);
                    // A ring, not a dot: the solver holds a root still, so it cannot be pulled.
                    if (roots.Contains(t))
                    {
                        Handles.CircleHandleCap(0, t.position, camera.transform.rotation, size * 1.5f, EventType.Repaint);
                    }
                    else
                    {
                        Handles.DotHandleCap(0, t.position, Quaternion.identity, size, EventType.Repaint);
                    }
                    // Joined to the parent bone, so the dots read as the chains they are.
                    if (t.parent != null && inChain.Contains(t.parent))
                    {
                        Handles.DrawLine(t.parent.position, t.position, lit ? 2f : 1f);
                    }
                }
            }
            if (hoveredBone == null) return;
            int chains = Roots(hovered).Count;
            Handles.Label(hoveredBone.position, chains > 1 ? $"{hovered.name}: {chains} chains move together" : hovered.name);
        }

        // What a pick may take hold of. A root never moves, so a pull on it
        // pulls at nothing at full force for as long as it is held: one is
        // offered only on a chain with no other bone.
        internal static List<Transform> Grabbable(Component c)
        {
            var roots = Roots(c);
            var bones = ChainBones(c);
            var free = bones.Where(t => !roots.Contains(t)).ToList();
            return free.Count > 0 ? free : bones;
        }

        // The chain bone nearest the cursor, within a reach that does not steal
        // clicks meant for the rest of the Scene view. Bones within a few
        // pixels of the nearest go to the one nearer the camera, so a chain in
        // front wins over one behind it. Measured from the nearest, never from
        // the pick so far, or a row of close bones walks the pick off the cursor.
        // A chain that is off cannot take a force, so it is never picked.
        static bool PickBone(CVRAvatar avatar, Vector2 mouse, out Component chain, out Transform bone)
        {
            const float reach = 24f, tie = 4f;
            var near = new List<(Component chain, Transform bone, float d, float z)>();
            foreach (var c in Chains(avatar).Where(Running))
            {
                foreach (var t in Grabbable(c))
                {
                    var at = HandleUtility.WorldToGUIPointWithDepth(t.position);
                    float d = Vector2.Distance(at, mouse);
                    if (at.z > 0f && d < reach) near.Add((c, t, d, at.z));
                }
            }
            chain = null;
            bone = null;
            if (near.Count == 0) return false;
            float nearest = near.Min(n => n.d);
            var pick = near.Where(n => n.d <= nearest + tie).OrderBy(n => n.z).First();
            chain = pick.chain;
            bone = pick.bone;
            return true;
        }

        void Take(Component chain, Transform bone)
        {
            EndForce();
            _held = chain;
            _heldBone = bone;
            _heldPoint = bone.position;
            _flingVelocity = Vector3.zero;
            _lastDrag = EditorApplication.timeSinceStartup;
#if AVATARBRIDGE_DYNBONE
            if (chain is DynamicBone db)
            {
                _forced = db;
                _forceBefore = db.m_Force;
                _fling = Vector3.zero;
            }
#endif
        }

        void Hold(Vector3 pull)
        {
#if AVATARBRIDGE_MAGICA
            // Tripled after a first try read as limp: the cloth's own damping
            // eats most of a per-frame velocity add.
            if (_held is MagicaCloth cloth) Push(cloth, pull, Mathf.Min(pull.magnitude * 30f, 18f));
#endif
#if AVATARBRIDGE_DYNBONE
            // m_Force is a displacement per step, in the component's scale.
            // Capped, so a long reach nudges the chain instead of throwing it.
            if (_held is DynamicBone db && db == _forced)
            {
                db.m_Force = _forceBefore + Vector3.ClampMagnitude(pull * 0.15f, 0.05f) / Scale(db);
            }
#endif
        }

#if AVATARBRIDGE_MAGICA
        // MagicaCloth applies a pushed force only in a frame that runs a simulation step (90 Hz)
        // and clears it at the end of every frame, so a one-frame fling at 300 fps was usually
        // thrown away (measured, PhysicsTesterProbe). It is re-sent for a little over one step.
        MagicaCloth _flung;
        Vector3 _flungVelocity;
        float _flungUntil;
#endif

        void LetGo()
        {
#if AVATARBRIDGE_MAGICA
            if (_held is MagicaCloth cloth)
            {
                _flung = cloth;
                _flungVelocity = _flingVelocity;
                _flungUntil = Time.time + 1.5f / 90f;
            }
#endif
#if AVATARBRIDGE_DYNBONE
            // A displacement of v/60 in one step adds v at 60 Hz; a linear fade
            // over twelve steps adds up to six of its first, hence 360.
            if (_held is DynamicBone db && db == _forced)
            {
                _fling = Vector3.ClampMagnitude(_flingVelocity, 25f) / 360f / Scale(db);
                _flungAt = Time.time;
            }
#endif
            _held = null;
        }

#if AVATARBRIDGE_DYNBONE
        void Fade()
        {
            float left = 1f - (Time.time - _flungAt) / FlingTime;
            if (left <= 0f) EndForce();
            else _forced.m_Force = _forceBefore + _fling * left;
        }

        static float Scale(DynamicBone db) => Mathf.Max(Mathf.Abs(db.transform.lossyScale.x), 1e-4f);
#endif

        void EndForce()
        {
#if AVATARBRIDGE_DYNBONE
            if (_forced != null) _forced.m_Force = _forceBefore;
            _forced = null;
#endif
        }

#if AVATARBRIDGE_MAGICA
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
#endif

        // ---- live edits ----------------------------------------------------------------

        static int Hash(Component c) => Json(c).GetHashCode();

        void PollEdits()
        {
            if (EditorApplication.timeSinceStartup < _nextEditPoll) return;
            _nextEditPoll = EditorApplication.timeSinceStartup + 0.25;
            // The Inspector edits the selection, and an Inspector locked before
            // Play shows one of the avatar's chains while the avatar is selected.
            // Anything seen stays watched.
            var watch = Selection.gameObjects.SelectMany(go => go.GetComponents<Component>()).Where(IsChain)
                .Concat(Chains(ResolveAvatar()));
            foreach (var c in watch)
            {
                // A prefab asset keeps its edits anyway.
                if (c.gameObject.scene.IsValid() && !_seen.ContainsKey(c)) _seen[c] = Hash(c);
            }
            bool changed = false;
            foreach (var c in _seen.Keys.ToList())
            {
                if (c == null)
                {
                    _seen.Remove(c);
                    continue;
                }
#if AVATARBRIDGE_DYNBONE
                // The grab writes m_Force while it pushes; that is not an edit.
                if (c == _forced) continue;
#endif
                int hash = Hash(c);
                if (hash == _seen[c]) continue;
                _seen[c] = hash;
                Refresh(c);
                if (!_edited.Contains(c)) _edited.Add(c);
                changed = true;
            }
            if (changed) ShowEdited();
        }

        void ShowEdited()
        {
            if (_editedRows == null) return;
            _editedRows.Clear();
            _edited.RemoveAll(c => c == null);
            if (_edited.Count == 0) return;
            var heading = BridgeElements.SubHeading("Edited this Play session");
            heading.style.marginTop = 6;
            _editedRows.Add(heading);
            _editedRows.Add(BridgeElements.Hint(
                "Unity throws away what changes in Play mode. Keep writes a component's settings back into the " +
                "scene when Play mode ends; nothing you do not keep is kept."));
            var records = LoadKept();
            foreach (var chain in _edited)
            {
                var captured = chain;
                int kept = records.instances.IndexOf(chain.GetInstanceID());
                string state = kept < 0 ? "" : records.json[kept].GetHashCode() == Hash(chain) ? "  kept" : "  changed since Keep";
                var line = new VisualElement();
                line.style.flexDirection = FlexDirection.Row;
                line.style.alignItems = Align.Center;
                var name = new Label($"{chain.name}  ({chain.GetType().Name}){state}");
                name.style.flexGrow = 1;
                line.Add(name);
                line.Add(new Button(() => { Keep(captured); ShowEdited(); })
                {
                    text = "Keep",
                    tooltip = "Write this component's settings as they are now into the scene when Play mode ends.",
                });
                _editedRows.Add(line);
            }
            _editedRows.Add(new Button(() => { foreach (var c in _edited) Keep(c); ShowEdited(); }) { text = "Keep all" });
        }

        static void Keep(Component chain)
        {
            if (chain == null) return;
            var kept = LoadKept();
            int i = kept.instances.IndexOf(chain.GetInstanceID());
            if (i < 0)
            {
                i = kept.instances.Count;
                kept.instances.Add(chain.GetInstanceID());
                kept.ids.Add(GlobalObjectId.GetGlobalObjectIdSlow(chain).ToString());
                kept.json.Add(null);
            }
            kept.json[i] = Json(chain);
            SaveKept(kept);
        }

        // Once Play mode has handed the scene back: each record onto the
        // component it came from, as one undoable edit.
        static void ApplyKept()
        {
            var kept = LoadKept();
            if (kept.json.Count == 0) return;
            SessionState.EraseString(KeptKey);
            var names = new List<string>();
            for (int i = 0; i < kept.json.Count; i++)
            {
                var target = (GlobalObjectId.TryParse(kept.ids[i], out var id)
                    ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) : null) as Component;
                // An unsaved scene has no id to resolve; a scene object keeps
                // its instance id through Play mode.
                if (target == null) target = EditorUtility.InstanceIDToObject(kept.instances[i]) as Component;
                if (target == null || !IsChain(target))
                {
                    Debug.LogWarning("[AvatarBridge] A physics component kept in Play mode is no longer in the scene, " +
                                     "so its settings were not kept.");
                    continue;
                }
                Overwrite(target, kept.json[i]);
                names.Add($"{target.name} ({target.GetType().Name})");
            }
            if (names.Count > 0)
            {
                Debug.Log($"[AvatarBridge] Kept the Play-mode settings of {string.Join(", ", names)}.");
            }
        }

        // Object references travel as instance ids. A scene object keeps its id
        // through Play mode, one made during it does not, and an id that finds
        // nothing reads back as None. A Keep must never empty a root bone or
        // collider list, so a reference the scene had and the record lost is
        // put back. A slot cleared on purpose in Play mode comes back with it,
        // the cheaper of the two mistakes.
        static void Overwrite(Component target, string json)
        {
            var so = new SerializedObject(target);
            var had = new Dictionary<string, Object>();
            for (var p = so.GetIterator(); p.Next(true);)
            {
                if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue != null)
                {
                    had[p.propertyPath] = p.objectReferenceValue;
                }
            }
            Undo.RecordObject(target, "Keep Play-mode physics");
            EditorJsonUtility.FromJsonOverwrite(json, Tunable(target));
            so.Update();
            for (var p = so.GetIterator(); p.Next(true);)
            {
                if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue == null
                    && had.TryGetValue(p.propertyPath, out var was))
                {
                    p.objectReferenceValue = was;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        }

        // ---- overlay ------------------------------------------------------------------

        void SetOverlay(CVRAvatar avatar, bool on)
        {
            _overlay = on;
            _restByBone.Clear();
            _labels.Clear();
            // MagicaCloth draws its own particles, radius and colliders when
            // asked. A DynamicBone has no such switch and is drawn here.
            foreach (var chain in Chains(avatar))
            {
                var gizmos = Member(chain, "GizmoSerializeData");
                var debug = Member(gizmos, "clothDebugSettings");
                if (gizmos == null || debug == null) continue;
                if (on)
                {
                    if (!_gizmosBefore.ContainsKey(chain))
                    {
                        _gizmosBefore[chain] = ((bool)(Member(gizmos, "always") ?? false),
                            (bool)(Member(debug, "enable") ?? false));
                    }
                    SetMember(gizmos, "always", true);
                    SetMember(debug, "enable", true);
                    SetMember(debug, "position", true);
                    SetMember(debug, "radius", true);
                    SetMember(debug, "collider", true);
                }
                else if (_gizmosBefore.TryGetValue(chain, out var before))
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
            foreach (var chain in FindObjectsOfType<CVRAvatar>(true).SelectMany(a => Chains(a)))
            {
                foreach (var t in ChainBones(chain))
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

        static readonly Color HealthGood = new Color(0.3f, 0.9f, 0.5f);
        static readonly Color HealthWarn = new Color(1f, 0.7f, 0.1f);
        static readonly Color HealthBad = new Color(1f, 0.3f, 0.25f);

        // How far a chain has strayed from where it hangs at rest. Stretch is
        // the worst bone's distance from its parent over the rest distance,
        // strayed the worst bone's distance from its rest over the chain's length.
        struct Strain
        {
            public float Stretch, Strayed;
            public bool Broken;
        }

        // A label at each chain with its settings and health, and what the
        // solvers do not draw themselves: each bone's swing bound, and a
        // DynamicBone's bones, radius and colliders.
        void DrawChains(CVRAvatar avatar, SceneView view)
        {
            if (!_labels.TryGetValue(view, out var labels)) _labels[view] = labels = new List<(Rect, Component)>();
            labels.Clear();
            if (avatar == null) return;
            var judged = new List<(Component chain, Vector3 at, (Color colour, string why, int rank) health)>();
            var drawn = new HashSet<Component>();
            foreach (var chain in Chains(avatar))
            {
                var health = Health(chain, draw: true);
                DrawColliders(chain, drawn);
                var roots = Roots(chain);
                judged.Add((chain, roots.Count > 0 ? roots[0].position : chain.transform.position, health));
            }
            // Worst first, so a crowd leaves out the healthy and the switched
            // off, never the chain in trouble. Stable, so ties keep hierarchy order.
            var placed = new List<Rect>();
            foreach (var (chain, at, health) in judged.OrderBy(j => j.health.rank))
            {
                Label(chain, at, health, view.camera, placed, labels);
            }
        }

        void Label(Component chain, Vector3 at, (Color colour, string why, int rank) health, Camera camera,
            List<Rect> placed, List<(Rect, Component)> labels)
        {
            // Off screen, a label is only clutter piled at the edge.
            var view = camera.WorldToViewportPoint(at);
            if (view.z <= 0f || view.x < 0f || view.x > 1f || view.y < 0f || view.y > 1f) return;
            string text = LabelText(chain, health.why);
            var style = EditorStyles.whiteMiniLabel;
            var size = style.CalcSize(new GUIContent(text));
            var point = HandleUtility.WorldToGUIPoint(at);
            var rect = new Rect(point.x + 8f, point.y - size.y * 0.5f, size.x + 8f, size.y);
            // Moved below whatever it would cover; past a few tries, left out
            // rather than piled on, unless it is red. Each move clears a label
            // for good, so a red one always lands.
            for (int tries = 0; ; tries++)
            {
                float below = float.MinValue;
                foreach (var r in placed)
                {
                    if (r.Overlaps(rect)) below = Mathf.Max(below, r.yMax);
                }
                if (below == float.MinValue) break;
                if (tries == 6 && health.rank > 0) return;
                rect.y = below + 1f;
            }
            placed.Add(rect);
            labels.Add((rect, chain));
            Handles.BeginGUI();
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.6f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), health.colour);
            GUI.Label(new Rect(rect.x + 5f, rect.y, size.x, rect.height), text, style);
            Handles.EndGUI();
        }

        // The reason first and bracketed: at the end it read as one of the
        // values ("col 3  off").
        internal static string LabelText(Component chain, string why) =>
            chain.name.Replace("MagicaCloth_", "").Replace("DynamicBone_", "") +
            (why != null ? $"  [{why}]" : "") + "  " + Readout(chain);

        // Measured against where the chain hangs at rest. With draw, each
        // bone's swing bound, or a DynamicBone's bones and radius, on the way.
        internal (Color colour, string why, int rank) Health(Component chain, bool draw = false)
        {
            var excluded = Excluded(chain);
            var strain = new Strain();
            foreach (var root in Roots(chain))
            {
                var rest = root.localToWorldMatrix;
                float length = Reach(root, rest, excluded);
                Walk(root, rest, 0, 0f, length, excluded, ref strain, draw ? Drawer(chain, root, length) : null);
            }
            return Judge(chain, strain);
        }

        // Red: a bone gone invalid, or thrown past twice the chain's length from
        // where it hangs. Amber: switched off or hidden, not running, or a bone
        // pulled past half its rest length again. Green otherwise. Rank is the
        // order labels are placed in: red, amber in trouble, green, then off.
        static (Color colour, string why, int rank) Judge(Component chain, Strain s)
        {
            if (s.Broken) return (HealthBad, "invalid position", 0);
            if (s.Strayed > 2f) return (HealthBad, $"{s.Strayed:0.#} lengths from rest", 0);
            if (!chain.gameObject.activeInHierarchy) return (HealthWarn, "object hidden", 3);
            if (chain is Behaviour b && !b.enabled) return (HealthWarn, "switched off", 3);
#if AVATARBRIDGE_MAGICA
            // A cloth whose build failed sits still with no other sign.
            if (chain is MagicaCloth cloth && !cloth.IsValid()) return (HealthWarn, "not running", 1);
#endif
            if (s.Stretch > 1.5f) return (HealthWarn, $"stretched {s.Stretch:0.#}x", 1);
            return (HealthGood, null, 2);
        }

        static System.Action<Transform, Vector3, int, float> Drawer(Component chain, Transform root, float length)
        {
#if AVATARBRIDGE_MAGICA
            if (chain is MagicaCloth cloth)
            {
                var motion = Member(cloth.SerializeData, "motionConstraint");
                if (!(Member(motion, "useMaxDistance") is bool use) || !use) return null;
                var maxDistance = Member(motion, "maxDistance");
                int deepest = Mathf.Max(1, Deepest(root));
                return (bone, rest, depth, along) =>
                {
                    float bound = Curve(maxDistance, depth / (float)deepest);
                    float off = Vector3.Distance(rest, bone.position);
                    Handles.color = off > bound * 0.95f ? new Color(1f, 0.55f, 0.1f) : new Color(0.3f, 0.9f, 0.5f, 0.8f);
                    Handles.DrawLine(rest, bone.position);
                    WireSphere(rest, bound);
                };
            }
#endif
#if AVATARBRIDGE_DYNBONE
            if (chain is DynamicBone db)
            {
                float scale = Mathf.Abs(db.transform.lossyScale.x);
                var falloff = db.m_RadiusDistrib != null && db.m_RadiusDistrib.length > 0 ? db.m_RadiusDistrib : null;
                return (bone, rest, depth, along) =>
                {
                    Handles.color = new Color(0.4f, 0.8f, 1f, 0.8f);
                    Handles.DrawLine(bone.parent.position, bone.position);
                    // DynamicBone's own falloff: its curve over the length so far.
                    float r = db.m_Radius * scale * (falloff != null && length > 0f ? falloff.Evaluate(along / length) : 1f);
                    if (r > 0f) WireSphere(bone.position, r);
                };
            }
#endif
            return null;
        }

        // MagicaCloth draws its own; a DynamicBone's show only while selected.
        static void DrawColliders(Component chain, HashSet<Component> drawn)
        {
#if AVATARBRIDGE_DYNBONE
            if (!(chain is DynamicBone db) || db.m_Colliders == null) return;
            Handles.color = new Color(1f, 0.6f, 0.9f, 0.7f);
            foreach (var collider in db.m_Colliders)
            {
                if (!(collider is DynamicBoneCollider round) || !drawn.Add(round)) continue;
                var t = round.transform;
                var axis = round.m_Direction == DynamicBoneColliderBase.Direction.X ? Vector3.right
                    : round.m_Direction == DynamicBoneColliderBase.Direction.Y ? Vector3.up : Vector3.forward;
                float h = Mathf.Max(0f, round.m_Height * 0.5f - round.m_Radius);
                float r = round.m_Radius * Mathf.Abs(t.lossyScale.x);
                var a = t.TransformPoint(round.m_Center + axis * h);
                var b = t.TransformPoint(round.m_Center - axis * h);
                WireSphere(a, r);
                if (h <= 0f) continue;
                WireSphere(b, r);
                Handles.DrawLine(a, b);
            }
#endif
        }

        // Every bone below a root against where it would hang at rest: the
        // root's live pose carried down by each bone's rest local pose. The
        // solver holds the root, so its live pose is its rest.
        void Walk(Transform bone, Matrix4x4 rest, int depth, float along, float length, HashSet<Transform> excluded,
            ref Strain strain, System.Action<Transform, Vector3, int, float> draw)
        {
            // Indexed, not foreach: Transform's enumerator allocates, per bone per repaint.
            for (int i = 0; i < bone.childCount; i++)
            {
                var child = bone.GetChild(i);
                if (excluded.Contains(child)) continue;
                var childRest = RestChild(rest, child);
                Vector3 to = childRest.GetColumn(3);
                float step = Vector3.Distance(rest.GetColumn(3), to);
                var at = child.position;
                float sum = at.x + at.y + at.z;
                if (float.IsNaN(sum) || float.IsInfinity(sum))
                {
                    strain.Broken = true;
                }
                else
                {
                    if (step > 1e-5f) strain.Stretch = Mathf.Max(strain.Stretch, Vector3.Distance(bone.position, at) / step);
                    if (length > 1e-4f) strain.Strayed = Mathf.Max(strain.Strayed, Vector3.Distance(to, at) / length);
                }
                draw?.Invoke(child, to, depth + 1, along + step);
                Walk(child, childRest, depth + 1, along + step, length, excluded, ref strain, draw);
            }
        }

        // The longest way down a chain at rest.
        float Reach(Transform bone, Matrix4x4 rest, HashSet<Transform> excluded)
        {
            float most = 0f;
            for (int i = 0; i < bone.childCount; i++)
            {
                var child = bone.GetChild(i);
                if (excluded.Contains(child)) continue;
                var childRest = RestChild(rest, child);
                most = Mathf.Max(most, Vector3.Distance(rest.GetColumn(3), childRest.GetColumn(3))
                                       + Reach(child, childRest, excluded));
            }
            return most;
        }

        Matrix4x4 RestChild(Matrix4x4 rest, Transform child)
        {
            var local = RestOf(child) ?? (child.localPosition, child.localRotation);
            return rest * Matrix4x4.TRS(local.Item1, local.Item2, child.localScale);
        }

        static readonly Vector3[] Axes = { Vector3.up, Vector3.right, Vector3.forward };

        static void WireSphere(Vector3 centre, float radius)
        {
            foreach (var axis in Axes)
            {
                Handles.DrawWireDisc(centre, axis, radius);
            }
        }

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

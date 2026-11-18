#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    // A copy of the live avatar run as another player's client runs it: it gets only synced
    // parameters within the budget and the game's movement, gesture and voice values. "#" locals
    // stay default, IsLocal is false, and animation-driven parameters are left to the copy's own
    // animator, as every client computes those itself.
    public partial class CckAnimatorTester
    {
        // The game sets these per viewer, so copying the wearer's would be wrong.
        static readonly HashSet<string> PerViewer = new HashSet<string> { "IsLocal", "IsFriend", "DistanceTo" };

        static GameObject _twin;
        static Animator _twinSource, _twinAnimator;
        static (string name, AnimatorControllerParameterType type)[] _twinHears;
        static int _twinDropped;

        static string TwinNote() => _twin == null
            ? "The copy hears synced parameters, movement, gestures and voice from you, and nothing else: \"#\" locals stay at their defaults and IsLocal is off. Where it differs from you, other players see it."
            : (_twinDropped > 0
                ? $"The copy is running. {BridgeElements.Count(_twinDropped, "parameter past the sync budget stays at its default", "parameters past the sync budget stay at their defaults")} on it, as for everyone else."
                : "The copy is running. Contacts and the CCK's own drivers do nothing in the editor, on either copy.")
              + (_twinRested ? "" : " It started from your current look, since this window opened after Play began; open it before Play for the copy to start as the upload does.");

        static bool IsTwin(Component c) => _twin != null && c != null && c.transform.IsChildOf(_twin.transform);

        static void ShowTwin(Animator live)
        {
            DropTwin();
            var avatar = live.GetComponentInParent<CVRAvatar>();
            var source = avatar != null ? avatar.gameObject : live.gameObject;
            var controller = live.runtimeAnimatorController is AnimatorOverrideController o
                ? o.runtimeAnimatorController as AnimatorController
                : live.runtimeAnimatorController as AnimatorController;

            var budget = controller != null ? BridgeDiagnostics.BudgetOrder(controller).ToList()
                : new List<(AnimatorControllerParameter param, bool fits)>();
            _twinDropped = budget.Count(b => !b.fits);
            var hears = new HashSet<string>(budget.Where(b => b.fits).Select(b => b.param.name));
            hears.UnionWith(BridgeDiagnostics.ClientCoreParameters.Where(n => !PerViewer.Contains(n)));
            _twinHears = live.parameters.Where(p => hears.Contains(p.name)).Select(p => (p.name, p.type)).ToArray();

            _twin = Instantiate(source, source.transform.parent);
            _twin.name = source.name + " (as others see it)";
            _twinRested = false;
            // Beside the wearer, one avatar's width and a little apart.
            var bounds = source.GetComponentsInChildren<Renderer>().Select(r => r.bounds)
                .DefaultIfEmpty(new Bounds(source.transform.position, Vector3.one * 0.5f))
                .Aggregate((a, b) => { a.Encapsulate(b); return a; });
            _twin.transform.position = source.transform.position + source.transform.right * (bounds.size.x + 0.3f);
            // Streams run on the wearer's machine only; a remote copy has none.
            foreach (var stream in _twin.GetComponentsInChildren<CVRParameterStream>(true)) DestroyImmediate(stream);

            _twinSource = live;
            _twinAnimator = _twin.GetComponentsInChildren<Animator>(true)
                .FirstOrDefault(a => a.runtimeAnimatorController == live.runtimeAnimatorController);
            if (_twinAnimator != null)
            {
                // A copy of the wearer starts from what the wearer's animations wrote; another
                // client starts from the upload, and a state writing no curve keeps that.
                var rest = LoadTwinRest();
                _twinRested = rest.TryGetValue(ScenePath(live.transform), out var values) && ApplyRest(_twinAnimator, values);
                if (_twinRested) _twinAnimator.Rebind();
                _twinAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                SetParameter(_twinAnimator, "IsLocal", 0f);
            }
            EditorApplication.update -= HoldTwin;
            EditorApplication.update += HoldTwin;
        }

        static void DropTwin()
        {
            EditorApplication.update -= HoldTwin;
            if (_twin != null) DestroyImmediate(_twin);
            _twin = null;
            _twinSource = _twinAnimator = null;
        }

        static void HoldTwin()
        {
            if (!Application.isPlaying || _twin == null || _twinSource == null || _twinAnimator == null)
            {
                DropTwin();
                return;
            }
            foreach (var (name, type) in _twinHears)
            {
                switch (type)
                {
                    case AnimatorControllerParameterType.Bool: _twinAnimator.SetBool(name, _twinSource.GetBool(name)); break;
                    case AnimatorControllerParameterType.Int: _twinAnimator.SetInteger(name, _twinSource.GetInteger(name)); break;
                    case AnimatorControllerParameterType.Float: _twinAnimator.SetFloat(name, _twinSource.GetFloat(name)); break;
                }
            }
        }

        static bool _twinRested;
        const string TwinRestKey = "AvatarBridge.TwinRest";

        // Every value an avatar's animations write, as it stood before Play, by binding.
        [System.Serializable]
        internal class RestValue
        {
            public string path, type, property, reference;
            public float value;
            public bool isReference;
        }

        [System.Serializable]
        class TwinRest
        {
            public List<string> animators = new List<string>();
            public List<RestValues> values = new List<RestValues>();
        }

        [System.Serializable]
        class RestValues
        {
            public List<RestValue> list = new List<RestValue>();
        }

        static string ScenePath(Transform t) => t.gameObject.scene.path + ":" + AnimationUtility.CalculateTransformPath(t, null);

        // Serialized, so the domain reload on entering Play keeps them.
        static void CaptureTwinRest()
        {
            var rest = new TwinRest();
            foreach (var avatar in FindObjectsOfType<CVRAvatar>(true))
            {
                var animator = avatar.GetComponent<Animator>();
                if (animator == null) continue;
                rest.animators.Add(ScenePath(animator.transform));
                rest.values.Add(new RestValues { list = RestOf(animator, avatar.overrides) });
            }
            SessionState.SetString(TwinRestKey, JsonUtility.ToJson(rest));
        }

        static Dictionary<string, List<RestValue>> LoadTwinRest()
        {
            var rest = JsonUtility.FromJson<TwinRest>(SessionState.GetString(TwinRestKey, "{}")) ?? new TwinRest();
            var map = new Dictionary<string, List<RestValue>>();
            for (int i = 0; i < rest.animators.Count && i < rest.values.Count; i++) map[rest.animators[i]] = rest.values[i].list;
            return map;
        }

        // The values every clip of these controllers writes, read off the scene. Muscles are left
        // out: a humanoid's rest is its bones, which the transform bindings already carry.
        internal static List<RestValue> RestOf(Animator animator, params RuntimeAnimatorController[] more)
        {
            var clips = new[] { animator.runtimeAnimatorController }.Concat(more).Where(c => c != null)
                .SelectMany(c => c.animationClips).Where(c => c != null).Distinct();
            var floats = new HashSet<EditorCurveBinding>();
            var references = new HashSet<EditorCurveBinding>();
            foreach (var clip in clips)
            {
                floats.UnionWith(AnimationUtility.GetCurveBindings(clip));
                references.UnionWith(AnimationUtility.GetObjectReferenceCurveBindings(clip));
            }
            var root = animator.gameObject;
            var values = new List<RestValue>();
            foreach (var b in floats.Where(b => b.type != typeof(Animator)))
                if (AnimationUtility.GetFloatValue(root, b, out float v))
                    values.Add(new RestValue { path = b.path, type = b.type.AssemblyQualifiedName, property = b.propertyName, value = v });
            foreach (var b in references)
                if (AnimationUtility.GetObjectReferenceValue(root, b, out var o))
                    values.Add(new RestValue { path = b.path, type = b.type.AssemblyQualifiedName, property = b.propertyName, isReference = true,
                        reference = o != null ? GlobalObjectId.GetGlobalObjectIdSlow(o).ToString() : null });
            return values;
        }

        // The values written back by sampling one clip that holds them all, so every kind an
        // animation can write is written the way the animator writes it.
        internal static bool ApplyRest(Animator animator, List<RestValue> values)
        {
            if (values == null || values.Count == 0) return false;
            var clip = new AnimationClip();
            try
            {
                var floats = values.Where(v => !v.isReference && System.Type.GetType(v.type) != null).ToList();
                AnimationUtility.SetEditorCurves(clip,
                    floats.Select(v => EditorCurveBinding.FloatCurve(v.path, System.Type.GetType(v.type), v.property)).ToArray(),
                    floats.Select(v => AnimationCurve.Constant(0f, 0f, v.value)).ToArray());
                foreach (var v in values.Where(v => v.isReference && System.Type.GetType(v.type) != null))
                {
                    Object o = null;
                    if (v.reference != null && GlobalObjectId.TryParse(v.reference, out var id)) o = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                    AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve(v.path, System.Type.GetType(v.type), v.property),
                        new[] { new ObjectReferenceKeyframe { time = 0f, value = o } });
                }
                clip.SampleAnimation(animator.gameObject, 0f);
                return true;
            }
            finally
            {
                DestroyImmediate(clip);
            }
        }

        static void SetParameter(Animator animator, string name, float value)
        {
            foreach (var p in animator.parameters.Where(x => x.name == name))
            {
                if (p.type == AnimatorControllerParameterType.Bool) animator.SetBool(name, value > 0.5f);
                else if (p.type == AnimatorControllerParameterType.Int) animator.SetInteger(name, (int)value);
                else if (p.type == AnimatorControllerParameterType.Float) animator.SetFloat(name, value);
            }
        }
    }
}
#endif

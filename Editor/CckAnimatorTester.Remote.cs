#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    // A second copy of the live avatar, run the way another player's client
    // runs it: it hears only what ChilloutVR sends. Synced parameters within
    // the budget and the game's movement, gesture and voice parameters are
    // copied from the wearer every frame. "#" locals stay at their defaults,
    // IsLocal is false, and a parameter an animation drives is left to the
    // copy's own animator, since every client computes those itself. Wherever
    // the two differ, other players see something the wearer does not.
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
            : _twinDropped > 0
                ? $"The copy is running. {_twinDropped} parameter(s) past the sync budget stay at their defaults on it, as for everyone else."
                : "The copy is running. Contacts and the CCK's own drivers do nothing in the editor, on either copy.";

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

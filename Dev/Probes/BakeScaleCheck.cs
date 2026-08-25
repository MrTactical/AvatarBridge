// The scaled-bone check: proves the bake-scale mirror against a bone that
// is NOT sitting at 1.
//
// _YAPS_BakeScale is 1 on the material, meaning "the size this was baked
// at". The mirror used to copy a bone's m_LocalScale curve across as an
// ABSOLUTE number, so any bone not at exactly 1 when it was baked had its
// scale spent twice: once by the skinning, again by the shader. It showed
// only in game, because the editor runs no animator and the static 1
// stood, and it reached a user wearing an avatar.
//
// Every plug in the corpus sits at scale 1, which is the one value where
// the old code was right, so 87 avatars passed green for weeks. This is
// not a corpus scene: the digest records no curve values, so no baseline
// could have moved even with a scaled bone in it. The numbers are asserted
// directly instead.
//
//   AvatarBridge > Spike > Check the bake-scale mirror
//   Unity.exe -batchmode -quit -executeMethod AvatarBridge.Dev.BakeScaleCheck.Run
#if UNITY_EDITOR && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Dev
{
    public static class BakeScaleCheck
    {
        static readonly StringBuilder Log = new StringBuilder();
        static int _failed;

        [MenuItem("AvatarBridge/Spike/Check the bake-scale mirror")]
        public static void Run()
        {
            Log.Clear();
            _failed = 0;

            ScaledBone();
            RestBone();
            ChildAxis();

            Log.AppendLine(_failed == 0 ? "all checks passed" : _failed + " check(s) FAILED");
            if (_failed == 0)
            {
                Debug.Log("[YAPS] bake-scale mirror\n" + Log);
            }
            else
            {
                Debug.LogError("[YAPS] bake-scale mirror\n" + Log);
            }
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(_failed == 0 ? 0 : 1);
            }
        }

        // A bone baked at 0.4 that a size slider takes to 1.2. At the bottom
        // of that slider the plug is the size it was baked at, so the mirror
        // must say 1; at the top it is three times that, not 1.2.
        static void ScaledBone()
        {
            Log.AppendLine("bone baked at 0.4, slider to 1.2");
            using (var rig = new Rig(new Vector3(0.4f, 0.4f, 0.4f), Vector3.one, Quaternion.identity))
            {
                var clip = new AnimationClip { name = "size" };
                Curve(clip, rig, rig.Bone, "z", 0.4f, 1.2f);
                Near("clips written", Mirror(clip, rig), 1f);

                var curve = Read(clip, rig, "_YAPS_BakeScale");
                if (curve == null)
                {
                    Fail("no _YAPS_BakeScale curve written");
                    return;
                }
                Near("at the bake pose", curve.Evaluate(0f), 1f);
                Near("at the top of the slider", curve.Evaluate(1f), 3f);
                // Tangents are a slope in the same units. Left undivided the
                // curve sags between its keys while both keys still read
                // correctly, which is the kind of wrong nobody spots.
                Near("halfway, so the tangents went too", curve.Evaluate(0.5f), 2f);
            }
        }

        // The same rig at 1, which is where every avatar in the corpus sits
        // and why this went unseen: the old code was right here, and this
        // check exists to keep the fix from breaking it.
        static void RestBone()
        {
            Log.AppendLine("bone baked at 1, slider to 2.5");
            using (var rig = new Rig(Vector3.one, Vector3.one, Quaternion.identity))
            {
                var clip = new AnimationClip { name = "size" };
                Curve(clip, rig, rig.Bone, "z", 1f, 2.5f);
                Mirror(clip, rig);

                var curve = Read(clip, rig, "_YAPS_BakeScale");
                if (curve == null)
                {
                    Fail("no _YAPS_BakeScale curve written");
                    return;
                }
                Near("passes through untouched", curve.Evaluate(0f), 1f);
                Near("and at the top", curve.Evaluate(1f), 2.5f);
            }
        }

        // A child further down the chain, turned so its own length axis is
        // not its root's. Its scale curve is in ITS space, so reading it
        // with the root's axis takes the wrong number, or none at all.
        static void ChildAxis()
        {
            Log.AppendLine("child bone turned, its length axis is y where the root's is z");
            var turned = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
            using (var rig = new Rig(Vector3.one, new Vector3(0.5f, 0.25f, 0.5f), turned))
            {
                var clip = new AnimationClip { name = "size" };
                Curve(clip, rig, rig.Child, "y", 0.25f, 1f);   // length, ratio 4
                Curve(clip, rig, rig.Child, "x", 0.5f, 1f);    // girth, ratio 2
                Mirror(clip, rig);

                var length = Read(clip, rig, "_YAPS_BakeScale");
                var girth = Read(clip, rig, "_YAPS_BakeGirth");
                if (length == null)
                {
                    Fail("no _YAPS_BakeScale curve: the child's own axis was not used");
                    return;
                }
                if (girth == null)
                {
                    Fail("no _YAPS_BakeGirth curve written");
                    return;
                }
                Near("length from the child's y", length.Evaluate(1f), 4f);
                Near("girth from the child's x", girth.Evaluate(1f), 2f);
            }
        }

        // Identity: the bake measured a shaft running along world +Z.
        static int Mirror(AnimationClip clip, Rig rig)
        {
            return YapsCurveMirror.MirrorBoneScale(new[] { clip }, rig.Bones,
                rig.RendererPath, typeof(SkinnedMeshRenderer), Quaternion.identity);
        }

        static void Curve(AnimationClip clip, Rig rig, Transform bone, string axis, float from, float to)
        {
            AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
            {
                path = rig.Path(bone), type = typeof(Transform), propertyName = "m_LocalScale." + axis,
            }, AnimationCurve.Linear(0f, from, 1f, to));
        }

        static AnimationCurve Read(AnimationClip clip, Rig rig, string property)
        {
            return AnimationUtility.GetEditorCurve(clip, new EditorCurveBinding
            {
                path = rig.RendererPath, type = typeof(SkinnedMeshRenderer),
                propertyName = "material." + property,
            });
        }

        static void Near(string what, float got, float want)
        {
            bool ok = Mathf.Abs(got - want) < 0.001f;
            if (!ok)
            {
                _failed++;
            }
            Log.AppendLine("  " + (ok ? "ok  " : "FAIL") + " " + what
                           + ": " + got.ToString("0.###") + " (want " + want.ToString("0.###") + ")");
        }

        static void Fail(string what)
        {
            _failed++;
            Log.AppendLine("  FAIL " + what);
        }

        // The least hierarchy the mirror needs: a root to measure paths
        // from, a bone chain to read scales off, and a renderer to write
        // onto. Hidden and not saved, so running this leaves whatever scene
        // is open alone.
        class Rig : System.IDisposable
        {
            public readonly Transform Root, Bone, Child, Renderer;
            public readonly Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();

            public Rig(Vector3 boneScale, Vector3 childScale, Quaternion childRotation)
            {
                Root = Make("BakeScaleCheck", null, Quaternion.identity, Vector3.one);
                Bone = Make("Bone", Root, Quaternion.identity, boneScale);
                Child = Make("Child", Bone, childRotation, childScale);
                Renderer = Make("Mesh", Root, Quaternion.identity, Vector3.one);
                Renderer.gameObject.AddComponent<SkinnedMeshRenderer>();
                Bones[Path(Bone)] = Bone;
                Bones[Path(Child)] = Child;
            }

            static Transform Make(string name, Transform parent, Quaternion rotation, Vector3 scale)
            {
                var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
                if (parent != null)
                {
                    go.transform.SetParent(parent, false);
                }
                go.transform.localRotation = rotation;
                go.transform.localScale = scale;
                return go.transform;
            }

            public string Path(Transform t)
            {
                return AnimationUtility.CalculateTransformPath(t, Root);
            }

            public string RendererPath { get { return Path(Renderer); } }

            public void Dispose()
            {
                Object.DestroyImmediate(Root.gameObject);
            }
        }
    }
}
#endif

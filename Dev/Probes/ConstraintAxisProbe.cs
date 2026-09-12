// What does Unity's RotationConstraint put on an axis it is NOT affecting?
//
// VRChat's own solver masks an unaffected axis to the driven transform's LIVE
// local euler (VRCConstraintJob: val10 = CalculateEulerZXY(localRotation),
// snapped to the previous frame's value within 0.01 degrees). Unity has no
// such input, so it must substitute something else, and rotationAtRest is the
// only candidate. If it does, a bone whose masked axes are animated converts
// to one that snaps to rest on those axes, which is issue #6.
//
// Nothing about that is reasonable to assume. This builds the case and reads
// the answer off the transform.
//
//   -executeMethod AvatarBridge.Regression.ConstraintAxisProbe.Run
#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace AvatarBridge.Regression
{
    public static class ConstraintAxisProbe
    {
        const float AnimatedY = 40f;   // what an animation writes on the masked axis
        const float RestY = 0f;        // what rotationAtRest says that axis is
        const float SourceX = 25f;     // what the source drives on the affected axis

        static Transform driven;
        static int ticks;

        public static void Run()
        {
            var source = new GameObject("Source").transform;
            source.localEulerAngles = new Vector3(SourceX, 0f, 0f);

            driven = new GameObject("Driven").transform;
            driven.localEulerAngles = new Vector3(0f, AnimatedY, 0f);

            var c = driven.gameObject.AddComponent<RotationConstraint>();
            c.AddSource(new ConstraintSource { sourceTransform = source, weight = 1f });
            c.rotationAtRest = new Vector3(0f, RestY, 0f);
            c.rotationOffset = Vector3.zero;
            c.rotationAxis = Axis.X;          // Y and Z masked
            c.weight = 1f;
            c.constraintActive = true;
            c.locked = true;

            Log("before any evaluation");
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            // Written once, in Run, and never again: if the constraint
            // substitutes rest on the masked axis it overwrites the value on
            // its first evaluation and nothing here puts it back. Re-asserting
            // it each tick would make the read depend on whether the
            // constraint ran before or after this callback.
            EditorApplication.QueuePlayerLoopUpdate();
            ticks++;
            if (ticks < 8)
            {
                return;
            }
            EditorApplication.update -= Tick;
            Log($"after {ticks} editor ticks");

            var final = driven.localEulerAngles;
            float y = Mathf.DeltaAngle(0f, final.y);
            Debug.Log("PROBE verdict: " + (
                Mathf.Abs(final.x - SourceX) > 1f
                    ? "INCONCLUSIVE, the constraint never drove the affected axis in this mode"
                    : Mathf.Abs(y - AnimatedY) < 1f
                        ? "Unity KEEPS the live value on a masked axis, same as VRChat"
                        : Mathf.Abs(y - RestY) < 1f
                            ? "Unity writes rotationAtRest onto a masked axis; VRChat keeps the live value"
                            : $"Unity wrote {y:0.##} on the masked axis, neither live nor rest"));
            EditorApplication.Exit(0);
        }

        static void Log(string when)
        {
            var e = driven.localEulerAngles;
            Debug.Log($"PROBE {when}: driven local euler = " +
                $"({Mathf.DeltaAngle(0f, e.x):0.##}, {Mathf.DeltaAngle(0f, e.y):0.##}, " +
                $"{Mathf.DeltaAngle(0f, e.z):0.##}); expected x={SourceX} from the source, " +
                $"y is either {AnimatedY} (live) or {RestY} (rest)");
        }
    }
}
#endif

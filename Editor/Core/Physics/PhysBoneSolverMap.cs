#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEngine;

namespace AvatarBridge
{
    // Maps a PhysBone's feel onto MagicaCloth2's, derived from both
    // solvers' source. The derivation is in docs/SolverCalibration.md.
    public static class PhysBoneSolverMap
    {
        public const float PhysBoneHz = 60f;

        public const float MagicaHz = 90f;

        public const float RestorationScale = 0.2f;

        // Damping is scaled the same way: ClothSerializeDataFunction multiplies it by 0.2
        // ("20%") before the solver sees it. Missing this left every chain with a fifth of
        // its damping, measured as overshoot in Play mode against the original PhysBones.
        public const float DampingScale = 0.2f;

        const float SimplifiedSpringCeiling = 0.99f;

        public const int RestorationIterations = 3;

        static float Rebase(float retentionAt60)
        {
            return Mathf.Pow(Mathf.Clamp01(retentionAt60), PhysBoneHz / MagicaHz);
        }

        public static float Retention60(float pull, float spring, float stiffness, bool advanced)
        {
            pull = Mathf.Clamp01(pull);
            spring = Mathf.Clamp01(spring);
            stiffness = Mathf.Clamp01(stiffness);

            return advanced
                ? spring * (1f - pull) * (1f - stiffness)
                : spring * SimplifiedSpringCeiling;
        }

        public static float Restore60(float pull, float spring, float stiffness, bool advanced)
        {
            pull = Mathf.Clamp01(pull);
            spring = Mathf.Clamp01(spring);
            stiffness = Mathf.Clamp01(stiffness);

            return advanced
                ? pull * (1f - stiffness)
                : pull * (1f - spring * SimplifiedSpringCeiling);
        }

        // PhysBone 1.0 adds stiffness * prevVector and puts the bone back to its length; 1.1
        // subtracts the step's own displacement. So a 1.0 chain with stiffness s moves exactly
        // like a 1.1 chain with s / (1 + s), checked against the decompiled solver to float
        // precision. Everything below is derived for 1.1.
        public static float AsVersion11(float stiffness, bool version10) =>
            version10 ? stiffness / (1f + Mathf.Max(0f, stiffness)) : stiffness;

        public static float Damping(float pull, float spring, float stiffness, bool advanced)
        {
            return Mathf.Clamp01((1f - Rebase(Retention60(pull, spring, stiffness, advanced))) / DampingScale);
        }

        public static float RestorationStiffness(float pull, float spring, float stiffness,
            bool advanced, out bool saturated)
        {
            float perStep90 = 1f - Rebase(1f - Restore60(pull, spring, stiffness, advanced));

            // Un-compound the three iterations: solving 1 - (1-s)^3 = perStep90 for s.
            float perIteration = 1f - Mathf.Pow(1f - perStep90, 1f / RestorationIterations);

            float inspector = perIteration / RestorationScale;
            saturated = inspector > 1f;
            return Mathf.Clamp01(inspector);
        }

        public static void MapCurve(float atRoot, float atTip, out float value,
            out float curveStart, out float curveEnd, out bool useCurve)
        {
            atRoot = Mathf.Clamp01(atRoot);
            atTip = Mathf.Clamp01(atTip);

            value = Mathf.Max(atRoot, atTip);
            if (value <= 0.0001f)
            {
                value = 0f;
                curveStart = curveEnd = 1f;
                useCurve = false;
                return;
            }

            curveStart = atRoot / value;
            curveEnd = atTip / value;
            useCurve = !Mathf.Approximately(curveStart, curveEnd);
        }

        public static float SafeEvaluate(AnimationCurve curve, float t)
        {
            if (curve == null || curve.length == 0)
            {
                return 1f;
            }
            return Mathf.Clamp01(curve.Evaluate(t));
        }
    }
}
#endif

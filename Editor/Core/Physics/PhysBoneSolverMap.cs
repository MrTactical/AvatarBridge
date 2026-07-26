#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// Converts a PhysBone's feel into MagicaCloth2's, derived from both solvers' source rather
    /// than guessed.
    ///
    /// For a long time AvatarBridge refused to map pull/spring/stiffness at all, on the grounds
    /// that PhysBones were per-bone rotational springs and MagicaCloth2 a particle solver, so no
    /// exchange rate could exist. That premise was wrong. `VRC.Dynamics.dll` ships with the
    /// VRChat SDK and is not obfuscated (the game client is; the SDK assembly is not), and
    /// `PhysBoneManager.PhysBoneJob.SolveChain` shows PhysBone integrating bone ENDPOINTS and
    /// reading rotations back out of where they land — structurally the same family as
    /// MagicaCloth2. What actually defeated the earlier attempts was calibration, and calibration
    /// is a solvable problem.
    ///
    /// ## The two step functions
    ///
    /// PhysBone, version 1.1, Advanced integration. `zero` is this step's displacement:
    ///
    ///     zero  = prevVelocity * spring;                                    // A
    ///     zero += (pose - (endPoint + zero)) * pull;                        // B
    ///     zero += (prevVector - (endPoint + zero - beginPoint)) * stiffness; // C
    ///
    /// At this point `endPoint` is still last step's endpoint (`endPoint = prevEndPoint` earlier
    /// in the loop) so `endPoint - beginPoint` IS `prevVector`, and term C reduces exactly to
    /// `zero += -zero * stiffness`. Expanding all three:
    ///
    ///     zero = [ spring*(1-pull)*prevVelocity + pull*(pose - endPoint) ] * (1 - stiffness)
    ///
    /// So the chain's real behaviour is a leaky integrator with a per-step velocity retention of
    /// `spring*(1-pull)*(1-stiffness)` and a per-step restoring fraction of `pull*(1-stiffness)`.
    /// Stiffness is not a separate axis at all — it scales both.
    ///
    /// Simplified integration is the same shape with different coefficients, and it never reads
    /// `stiffness`:
    ///
    ///     zero = lerp((pose - endPoint) * pull, prevVelocity, min(1, 0.99*spring))
    ///
    /// giving retention `0.99*spring` and restoring `(1 - 0.99*spring) * pull`.
    ///
    /// MagicaCloth2 is position-based Verlet with the velocity re-derived from the position delta
    /// (`velocity = (nextPos - velocityOldPos) / dt`), and applies its two coefficients as:
    ///
    ///     velocity *= saturate(1 - damping * simulationPower.z)             // per step
    ///     rotate toward rest by saturate(stiffness * 0.2 * simulationPower.w) // per step
    ///
    /// Note the `* 0.2f` in `AngleConstraint.Convert` — the inspector's restoration stiffness is
    /// scaled to a fifth of its face value before it reaches the solver.
    ///
    /// ## Rebasing 60 Hz onto 90 Hz
    ///
    /// Both sets of numbers are per-STEP fractions, and both reference rates are fixed and known:
    /// PhysBone runs `FRAME_TIME = 1/60` with at most 6 substeps; MagicaCloth2's
    /// `DefaultSimulationFrequency` is 90. MagicaCloth2 already normalises its own coefficients
    /// for a user-changed frequency via `SimulationPower`, which is 1.0 at 90 Hz, so deriving at
    /// the 90 Hz reference is the correct and only thing to do here.
    ///
    /// A retention `r` applied 60 times a second equals `r^(60/90)` applied 90 times a second,
    /// which is the whole conversion. Everything below is that one identity.
    ///
    /// ## Why the shipped defaults look nothing alike
    ///
    /// A PhysBone at its defaults (pull 0.2, spring 0.2) maps to damping 0.71 and restoration
    /// 0.75, where MagicaCloth2 ships damping 0.05 and restoration 0.2. That gap is real and it
    /// is not a bug in the arithmetic: VRChat's defaults describe a stiff chain that barely
    /// leaves its pose, MagicaCloth2's describe flowing cloth. Two authors, two intents. Reading
    /// the disagreement as evidence against the mapping is what stalled this for several
    /// versions.
    /// </summary>
    public static class PhysBoneSolverMap
    {
        /// <summary>`PhysBoneManager.FRAME_TIME` is 1f/60f.</summary>
        public const float PhysBoneHz = 60f;

        /// <summary>`Define.System.DefaultSimulationFrequency`.</summary>
        public const float MagicaHz = 90f;

        /// <summary>
        /// `AngleConstraint.AngleConstraintParams.Convert` multiplies the serialized restoration
        /// stiffness by this before the solver ever sees it, so the inspector's 0..1 covers a
        /// real per-step range of 0..0.2.
        /// </summary>
        public const float RestorationScale = 0.2f;

        /// <summary>
        /// Simplified integration feeds spring through `lerp(0, 0.99, spring)`, so it can never
        /// quite reach total retention.
        /// </summary>
        const float SimplifiedSpringCeiling = 0.99f;

        /// <summary>Re-expresses a per-step fraction measured at 60 Hz as one at 90 Hz.</summary>
        static float Rebase(float retentionAt60)
        {
            return Mathf.Pow(Mathf.Clamp01(retentionAt60), PhysBoneHz / MagicaHz);
        }

        /// <summary>
        /// The per-step velocity retention the PhysBone actually runs at, once pull and stiffness
        /// have been folded in. See the derivation in the class comment.
        /// </summary>
        public static float Retention60(float pull, float spring, float stiffness, bool advanced)
        {
            pull = Mathf.Clamp01(pull);
            spring = Mathf.Clamp01(spring);
            stiffness = Mathf.Clamp01(stiffness);

            return advanced
                ? spring * (1f - pull) * (1f - stiffness)
                : spring * SimplifiedSpringCeiling;
        }

        /// <summary>The per-step fraction of the gap to the animated pose that the chain closes.</summary>
        public static float Restore60(float pull, float spring, float stiffness, bool advanced)
        {
            pull = Mathf.Clamp01(pull);
            spring = Mathf.Clamp01(spring);
            stiffness = Mathf.Clamp01(stiffness);

            return advanced
                ? pull * (1f - stiffness)
                : pull * (1f - spring * SimplifiedSpringCeiling);
        }

        /// <summary>
        /// MagicaCloth2 `damping`. Its solver keeps `1 - damping` of the velocity per step, so
        /// damping is the complement of the rebased retention.
        /// </summary>
        public static float Damping(float pull, float spring, float stiffness, bool advanced)
        {
            return Mathf.Clamp01(1f - Rebase(Retention60(pull, spring, stiffness, advanced)));
        }

        /// <summary>
        /// MagicaCloth2 `angleRestorationConstraint.stiffness`, as the inspector wants it —
        /// i.e. already divided back out by <see cref="RestorationScale"/>.
        ///
        /// <paramref name="saturated"/> reports that the PhysBone asked for a faster snap than
        /// MagicaCloth2 can express. Its ceiling of 0.2 per step at 90 Hz is equivalent to a
        /// PhysBone pull of about 0.3, and both of those already close the gap to within a
        /// billionth inside one second — so the clamp costs nothing visible, but it does mean
        /// every pull above ~0.3 lands on the same value and the report should say so.
        /// </summary>
        public static float RestorationStiffness(float pull, float spring, float stiffness,
            bool advanced, out bool saturated)
        {
            float perStep90 = 1f - Rebase(1f - Restore60(pull, spring, stiffness, advanced));
            float inspector = perStep90 / RestorationScale;
            saturated = inspector > 1f;
            return Mathf.Clamp01(inspector);
        }

        /// <summary>
        /// Maps a PhysBone falloff curve onto a MagicaCloth2 one.
        ///
        /// Both multiply their base value by a curve evaluated over the chain's depth, 0 at the
        /// root to 1 at the tip, so the shapes correspond directly. The mapping between them is
        /// non-linear, though, so the endpoints are converted individually and the result is
        /// re-expressed as MagicaCloth2 wants it: a base value with a 0..1 curve over it.
        /// MagicaCloth2 builds that curve with <c>AnimationCurve.Linear</c>, so only the two
        /// ends can be honoured and any shape between them is lost.
        /// </summary>
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

        /// <summary>
        /// Evaluates a PhysBone falloff curve the way the solver does — `SafeEvaluate` treats an
        /// empty or absent curve as a flat 1.
        /// </summary>
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

# Solver calibration: PhysBone to MagicaCloth2 and DynamicBone

How AvatarBridge maps one solver's feel onto another, derived from both
solvers' source rather than guessed. Kept here rather than in a comment
block: it is a derivation, not an explanation of the code beside it.

## PhysBone to MagicaCloth2

Converts a PhysBone's feel into MagicaCloth2's, derived from both solvers' source rather
than guessed.

For a long time AvatarBridge refused to map pull/spring/stiffness at all, on the grounds
that PhysBones were per-bone rotational springs and MagicaCloth2 a particle solver, so no
exchange rate could exist. That premise was wrong. `VRC.Dynamics.dll` ships with the
VRChat SDK and is not obfuscated (the game client is; the SDK assembly is not), and
`PhysBoneManager.PhysBoneJob.SolveChain` shows PhysBone integrating bone ENDPOINTS and
reading rotations back out of where they land; structurally the same family as
MagicaCloth2. What actually defeated the earlier attempts was calibration, and calibration
is a solvable problem.

## The two step functions

PhysBone, version 1.1, Advanced integration. `zero` is this step's displacement:

    zero  = prevVelocity * spring;                                    // A
    zero += (pose - (endPoint + zero)) * pull;                        // B
    zero += (prevVector - (endPoint + zero - beginPoint)) * stiffness; // C

At this point `endPoint` is still last step's endpoint (`endPoint = prevEndPoint` earlier
in the loop) so `endPoint - beginPoint` IS `prevVector`, and term C reduces exactly to
`zero += -zero * stiffness`. Expanding all three:

    zero = [ spring*(1-pull)*prevVelocity + pull*(pose - endPoint) ] * (1 - stiffness)

So the chain's real behaviour is a leaky integrator with a per-step velocity retention of
`spring*(1-pull)*(1-stiffness)` and a per-step restoring fraction of `pull*(1-stiffness)`.
Stiffness is not a separate axis at all; it scales both.

Simplified integration is the same shape with different coefficients, and it never reads
`stiffness`:

    zero = lerp((pose - endPoint) * pull, prevVelocity, min(1, 0.99*spring))

giving retention `0.99*spring` and restoring `(1 - 0.99*spring) * pull`.

MagicaCloth2 is position-based Verlet with the velocity re-derived from the position delta
(`velocity = (nextPos - velocityOldPos) / dt`), and applies its two coefficients as:

    velocity *= saturate(1 - damping * simulationPower.z)               // once per step
    rotate toward rest by saturate(stiffness * 0.2 * simulationPower.w) // 3x per step

Two multipliers hide in that second line and both have to be undone. The `* 0.2f` is in
`AngleConstraint.Convert`; the inspector's restoration is scaled to a fifth of its face
value before the solver sees it. And the constraint runs inside
`for (k = 0; k < Define.System.AngleLimitIteration; k++)` with that constant equal to 3,
so the value compounds three times before the step ends.

## Rebasing 60 Hz onto 90 Hz

Both sets of numbers are per-STEP fractions, and both reference rates are fixed and known:
PhysBone runs `FRAME_TIME = 1/60` with at most 6 substeps; MagicaCloth2's
`DefaultSimulationFrequency` is 90. MagicaCloth2 already normalises its own coefficients
for a user-changed frequency via `SimulationPower`, which is 1.0 at 90 Hz, so deriving at
the 90 Hz reference is the correct and only thing to do here.

A retention `r` applied 60 times a second equals `r^(60/90)` applied 90 times a second,
which is the whole conversion. Everything below is that one identity.

## The check that catches a wrong coefficient

Run MagicaCloth2's own default restoration back through this in reverse. 0.2 inspector,
so 0.04 per iteration, compounded three times, rebased to 60 Hz; and it comes out as a
PhysBone pull of **0.168**. A default PhysBone (pull 0.2, spring 0.2) restores at
**0.160** per step. Two authors who never spoke, five percent apart.

That agreement is the only cheap test there is for this file, and it is worth re-running
after touching any coefficient. 2.35.0 shipped without the iteration count and the same
check would have read 0.55 against 0.16; the error was sitting in plain sight and got
waved through as "two authors, two intents". It was not; they agree.

Damping is the exception and genuinely does differ: MagicaCloth2 ships 0.05 where a
default PhysBone works out near 0.66. That one is a real difference in intent. VRChat's
default chain is nearly dead and creators raise spring to 0.6+ for hair, which lands back
in MagicaCloth2's territory.

## PhysBone to DynamicBone

PhysBone gravity, applied entirely through m_Force.

DynamicBone splits the idea across two fields: m_Gravity cancels out the part
already baked into the character's rest pose ("partial force apply to character's
initial pose is cancelled out"), while m_Force is a plain constant pull. This used
to mirror that split, sending gravityFalloff to m_Gravity and the remainder to
m_Force with gravity² = m_Gravity² + m_Force².

ChilloutVR's m_Gravity is not safe to use. In Zettai/UpdateParticlesJob.GetForce
the rest-pose cancellation is divided by the avatar's lossy scale while gravity
itself is multiplied by it:

    (gravity - dir * max(dot(x, dir), 0) / scale + m_Force + wind) * scale * dt

The two only balance at scale 1. AvatarBridge injects a height scaler, so converted
avatars sit off that point by construction, and below scale 1 the cancellation term
can exceed gravity and push bones upward. m_Force is added after the cancellation
and scales uniformly, so it behaves the same at any scale. A tester reported this as
"CVR doesn't play nicely with them at all".

The two halves were collinear parts of one magnitude, so collapsing them into one
field means the full magnitude; summing the halves would overshoot it by up to 41%.
Added rather than assigned so any force already on the component still composes.

Scaled by ElasticityScale, and that factor is load-bearing: where a chain settles
is the BALANCE of constant force against elastic restore, and the restore was
already scaled down by ElasticityScale to sit in DynamicBone's useful range.
Carrying the force over at full strength made every gravity-tinted chain deflect
~5x further than VRChat; a tester's tail with gravity -0.07 (a gentle upward
bias in VRChat) converted to a force that pinned the tail at the sky. Scaling
force and restore by the same factor preserves the resting pose exactly.
Asserted for EVERY chain, not only the ones carrying gravity. Zero is DynamicBone's
own default, so this changed nothing the day it was written; but it was relying on
that default rather than stating the requirement, and the requirement is hard:
anything that reaches m_Gravity on a scaled avatar goes through ChilloutVR's broken
path and can be pushed UPWARD.

Confirmed against the shipping client. DynamicBone.GetUpdatedDbData writes
m_LocalGravity through the root's world-to-local matrix WITHOUT the renormalisation
stock DynamicBone applies (".normalized * m_Gravity.magnitude"), and
UpdateParticlesJob.GetForce then divides the rest-pose cancellation by scale to
compensate; one factor too many, because local-to-world already put that scale
back. The gravity term works out to (g * scale - g), which is zero only at scale 1:
at 0.376 it is -0.62g, upward. m_Force is added after the cancellation and only ever
multiplied by scale, so it is correct at every size.

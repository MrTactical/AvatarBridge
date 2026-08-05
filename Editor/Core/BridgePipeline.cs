#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;

namespace AvatarBridge
{
    /// <summary>
    /// What a pass DOES to the conversion's shared state, as far as ordering is concerned.
    ///
    /// Only traits that have caused a real bug are listed. A flag nobody enforces is decoration,
    /// and a taxonomy invented up front ages badly — add one when something goes wrong that
    /// ordering could have prevented, not before.
    /// </summary>
    [Flags]
    public enum PassTraits
    {
        None = 0,

        /// <summary>
        /// Rewrites curves inside animation clips in place. Every such pass must run AFTER the
        /// clips belong to this conversion, or it edits the avatar author's own files.
        /// </summary>
        EditsClips = 1 << 0,

        /// <summary>
        /// Copies every clip the finished controller references into the output folder and
        /// repoints the controller at the copies. After this, editing a clip is safe.
        /// </summary>
        MakesClipsOurs = 1 << 1,
    }

    public struct BridgePass
    {
        public string Name;
        public PassTraits Traits;
        public Action<BridgeContext> Run;
    }

    /// <summary>
    /// The conversion's pass list, with the ordering rules that matter stated as data and CHECKED
    /// rather than described in a comment and hoped for.
    ///
    /// This exists because of a specific bug. BridgeConverter carried the sentence "After
    /// self-containment, because it edits clips: every one it touches is now the conversion's own
    /// copy, never the source avatar's" — and three lines ABOVE it sat ConstraintConverter, which
    /// edits clips, running before self-containment. The rule was written down, correct, and
    /// violated in the same screenful. It rewrote three of GoGo Loco's shipped flight animations
    /// in a real project, breaking that package for VRChat, and went unnoticed because VRCFury
    /// bakes to a throwaway copy on most avatars and was silently repairing the damage every run.
    ///
    /// A comment cannot fail. This can: the order is validated before anything executes, so a pass
    /// added in the wrong place stops the conversion at the start with both names, instead of
    /// quietly corrupting somebody's assets.
    /// </summary>
    public static class BridgePipeline
    {
        /// <summary>
        /// The problem with this ordering, or null when it is sound. Separate from Execute so a
        /// test can ask the question without running a conversion.
        /// </summary>
        public static string Validate(IReadOnlyList<BridgePass> passes)
        {
            if (passes == null)
            {
                return "no passes";
            }
            int makesOurs = -1;
            for (int i = 0; i < passes.Count; i++)
            {
                if ((passes[i].Traits & PassTraits.MakesClipsOurs) != 0)
                {
                    makesOurs = i;
                    break;
                }
            }

            for (int i = 0; i < passes.Count; i++)
            {
                if ((passes[i].Traits & PassTraits.EditsClips) == 0)
                {
                    continue;
                }
                if (makesOurs < 0)
                {
                    return $"\"{passes[i].Name}\" edits animation clips, but no pass in this " +
                           "pipeline copies them into the output folder first — so it would be " +
                           "editing the source avatar's own files.";
                }
                if (i < makesOurs)
                {
                    return $"\"{passes[i].Name}\" edits animation clips but runs BEFORE " +
                           $"\"{passes[makesOurs].Name}\", which is what makes those clips ours to " +
                           "edit. In that order it rewrites the avatar author's own animation " +
                           "files — or a package's — and the damage is invisible on any avatar " +
                           "whose bake regenerates its clips. Move it after.";
                }
            }
            return null;
        }

        /// <summary>
        /// Validates, then runs each pass in order. Throws on a bad ordering: this is a mistake in
        /// the tool, not in the user's avatar, and it must never be something a conversion limps
        /// past — the whole point is that it cannot reach the user's files.
        /// </summary>
        public static void Execute(BridgeContext ctx, IReadOnlyList<BridgePass> passes)
        {
            string problem = Validate(passes);
            if (problem != null)
            {
                throw new InvalidOperationException("AvatarBridge pass ordering is wrong: " + problem);
            }
            foreach (var pass in passes)
            {
                pass.Run(ctx);
            }
        }
    }
}
#endif

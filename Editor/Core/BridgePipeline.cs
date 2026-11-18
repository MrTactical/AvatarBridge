#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;

namespace AvatarBridge
{
    // Ordering traits. Only ones that caused a real bug; add one when ordering could have prevented a bug.
    [Flags]
    public enum PassTraits
    {
        None = 0,

        EditsClips = 1 << 0,

        MakesClipsOurs = 1 << 1,
    }

    public struct BridgePass
    {
        public string Name;
        public PassTraits Traits;
        public Action<BridgeContext> Run;
    }

    // Ordering rules as data, checked before anything runs. A comment-only rule was once violated
    // a few lines away: a clip-editing pass ran before self-containment and rewrote GoGo Loco's
    // shipped flight clips, hidden because VRCFury's throwaway bake copy masked it.
    public static class BridgePipeline
    {
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
                           "pipeline copies them into the output folder first, so it would be " +
                           "editing the source avatar's own files.";
                }
                if (i < makesOurs)
                {
                    return $"\"{passes[i].Name}\" edits animation clips but runs BEFORE " +
                           $"\"{passes[makesOurs].Name}\", which is what makes those clips ours to " +
                           "edit. In that order it rewrites the avatar author's own animation " +
                           "files, or a package's, and the damage is invisible on any avatar " +
                           "whose bake regenerates its clips. Move it after.";
                }
            }
            return null;
        }

        public static void Execute(BridgeContext ctx, IReadOnlyList<BridgePass> passes)
        {
            string problem = Validate(passes);
            if (problem != null)
            {
                throw new InvalidOperationException("AvatarBridge pass ordering is wrong: " + problem);
            }
            // Log, not report: report lines must not change between two runs of the same avatar.
            var times = new List<string>();
            var clock = new System.Diagnostics.Stopwatch();
            foreach (var pass in passes)
            {
                clock.Restart();
                try
                {
                    pass.Run(ctx);
                }
                catch (Exception e)
                {
                    // The bare message never says where, and the report entry is all a user sends.
                    throw new Exception($"\"{pass.Name}\" failed: {e.GetType().Name}: {e.Message}", e);
                }
                times.Add($"{pass.Name} {clock.ElapsedMilliseconds}");
            }
            UnityEngine.Debug.Log("[AvatarBridge] Pass times (ms): " + string.Join(", ", times));
        }
    }
}
#endif

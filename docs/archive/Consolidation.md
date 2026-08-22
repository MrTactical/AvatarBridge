# Consolidation — the tool's own optimisation pass

Written 2026-08-19, after the 4.1.1 release, from a complaint that is correct: the thing has grown
by accretion and it shows. `docs/archive/Optimisation.md` is about making avatars cheaper. This is about
making the project cheaper to use and to change.

Everything below is measured, not felt.

## What is actually there

**Four windows and eight menu entries.** `Tools/Avatar Bridge/` holds the converter, the
ChilloutVR Toolkit and the CCK Animator Tester; `Tools/YAPS/` holds Setup and two prefab makers;
`GameObject/YAPS/` adds two more. Four separate `EditorWindow`s: AvatarBridgeWindow,
ToolkitWindow, YapsSetupWindow, CckAnimatorTester.

Every one of them is the same shape — pick an avatar, choose some options, press a button — and
each expresses it differently. The converter uses numbered steps with collapsing cards, the setup
tab uses a different card set, the Toolkit uses one card per tool with its own button, YAPS Setup
uses its own again. Nothing tells a user which window holds the thing they want.

**47 settings, 40 of them booleans**, in one flat class, rendered into one collapsed card holding
six subheadings. A user went looking for two of them and could not find them, which is what
prompted this document. Two of those settings — `weighAvatar` and `surveyAvatar` — do not change
the avatar at all. They are a different KIND of setting living in the same list as the rest.

**AnimatorMerger.cs is 10,398 lines**, a fifth of the whole codebase, and holds the layer merge,
the parameter rename pass, restore-clip synthesis, blend-tree repair, mask handling and the
physics toggle rewiring. Three days of work this week happened inside it, and finding the right
place to stand each time cost real minutes.

**The same helpers exist more than once.** `Underlying` (unwrap an override controller) three
times, `WriteReportFile` twice, `ReadAvatar` twice. The two pipelines end with an identical
five-step tail:

```
BridgeDiagnostics.Run → ReadAvatar → StoreDescription → WriteReportFile → SaveAssets
```

## What to do, in the order worth doing it

**1. One window, four tabs.** Fold the Toolkit and YAPS Setup into AvatarBridgeWindow beside the
two tabs already there. The converter window already knows how to be tabbed; this removes the
"which window?" question entirely and lets the Toolkit's tools be reachable from a converted
avatar, which today they are not. The biggest user-visible win and the largest change.

**2. Reading options belong with Analyse.** `weighAvatar` and `surveyAvatar` are the only settings
that do not change what gets built. Put them under *Analyse this avatar* in step 2, so step 2 is
"understand this avatar" and the option cards stay "decide what gets built". Small, and it fixes
the specific thing that went wrong.

**3. One finishing sequence.** `BridgePipeline.Finish(ctx)` for the five-step tail, called by both
the converter and setup. Today a change to how conversions end has to be made twice and was,
twice this week. Behaviour-identical, and the corpus proves it.

**4. Split AnimatorMerger.** Not a rewrite — a move. The physics toggle rewiring is a self-contained
~500 lines that took three attempts to fix partly because of where it lives; restore-clip synthesis
is another cluster. Both lift into their own files with no behaviour change, which the corpus can
prove in one run.

**5. Dedupe the helpers.** `Underlying`, `UniqueChildName`, path helpers — one home each.
Mechanical, zero risk, and it stops the next person writing a fourth copy.

**6. Settings by category, not by hand-placed subheading.** Mark each setting as changing the
avatar or only reading it, and let the UI group them, rather than six subheadings placed by hand
in one method. Do this last: it is the one that most easily turns into a rewrite.

**7. The YAPS package's file list must verify itself.** It names which `Editor/Core` files come
with the standalone package, because most of Core is converter-only and would drag the VRChat SDK
in behind it. `Editor/Toolkit` ships whole, so adding cards there in 4.1.0 added references the
list did not know about — and both 4.1.0 and 4.1.1 shipped a YAPS package that fails to compile
on import:

```
FreeWins.cs(47,51): error CS0246: The type or namespace name 'AvatarSurvey' could not be found
```

The list was settled once by compiling it against the CCK with no VRChat SDK. That check has to
run as part of building, not once by hand, or the next file added to the Toolkit breaks it again
silently. Running it costs seconds: Mono's `csc` over the same file set the packer uses, with
`CVR_CCK_EXISTS` defined and no VRChat define.

Note for whoever runs it: the Roslyn `csc.exe` under `MonoBleedingEdge/lib/mono/msbuild` fails to
start in this environment and prints a missing-assembly message with **no** `error CS` lines,
which reads as a clean compile if the output is filtered for errors. Use
`MonoBleedingEdge/bin/mono.exe` with `lib/mono/4.5/csc.exe`, and check that the assembly was
actually produced rather than trusting an empty error list.

## What NOT to do

- **Do not rename settings.** They are in every regression digest and in users' saved profiles.
  A rename costs an 84-file diff and other people's muscle memory, and buys nothing.
- **Do not rebuild the UI framework.** UI Toolkit is fine; the problem is arrangement.
- **Do not merge the converter and setup pipelines.** They share an ending, not a middle. One
  bakes a VRChat avatar, the other configures a native one.
- **Do not do 1 and 4 in the same change.** One moves user-visible things, the other moves code.
  Mixing them means a corpus diff nobody can read.

## Done, 2026-08-19

7, 5, 3, 4, 1 and 2, in that order, each compiled before the next started.

- **7** — the packer refuses to build a YAPS package whose closure is open, naming the file and
  the type. Reintroducing the 4.1.1 bug now aborts the build.
- **5** — `Underlying` lives in `BridgeContext` alone. Three copies existed and two of them
  guarded against a cycle while the third looped unbounded.
- **3** — `BridgeFinish.Run` is the ending both flows share. Setup gained the survey and weight
  cards by getting the same ending; it still has no HTML report, because `DiagnosticsWriter` and
  `HtmlReportWriter` are guarded on the VRChat SDK for reasons nobody remembers. Worth its own
  change.
- **4** — the physics rewiring is `AnimatorMerger.Physics.cs`, 816 lines of a partial class.
  The original is down to 9,607 from 10,398. A move, not a rewrite: same access, same signatures.
- **1** — the Toolkit is a **panel**, mounted by its own window and by the main window's new
  **Tools** tab. Same cards, one implementation. The tab exists without the VRChat SDK, which is
  exactly who it is for.
- **2** — the two reading settings sit under *Analyse this avatar* in both flows, under "What the
  report tells you".

**6 was not done, on purpose.** Its intent — separate what changes the avatar from what only
reads it — is what 2 delivered, in the place a user actually looks. What remains of it is an
attribute system serving two fields, which is the "turns into a rewrite" this document warned
about. Worth revisiting only if a third reading-only setting appears.

## Order

3, 5 and 4 first — they are invisible to users and the corpus proves them outright. Then 2, which
is small and answers the actual complaint. Then 1, which deserves its own release and its own
testing. Then 6, if it still seems worth it.

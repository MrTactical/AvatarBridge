# Dev

Development tooling. None of this ships: the package build prunes the
whole folder, and a public `.unitypackage` never contains it.

| Folder | What is in it |
|---|---|
| `Build/` | `build-package.sh`, the only definition of how a release is assembled |
| `Corpus/` | the regression runner and the toggle sweep it reports with |
| `Tests/` | one behaviour each, run from Tools ▸ Avatar Bridge ▸ Dev in a test project |
| `Probes/` | one-shot investigations: what a binding resolves to, what the CCK declares |
| `Scenes/` | building and cleaning the test scenes the corpus runs against |
| `Spikes/` | throwaway experiments, gitignored |

A test that patches or bakes writes into the shared `Assets/YAPS/Generated`,
so it takes `GeneratedFiles.Snapshot()` first and calls
`GeneratedFiles.DeleteNew` in its `finally`: only what the run added goes,
never a copy that was there before it.

The corpus reads its avatar lists from `Regression/corpus.cfg` and its
repo root from the `AVATARBRIDGE_REPO` environment variable. Digests are
written to `Regression/`, which is gitignored: they describe the
internals of paid avatars.

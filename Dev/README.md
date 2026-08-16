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

The corpus reads its avatar lists from `Regression/corpus.cfg` and its
repo root from the `AVATARBRIDGE_REPO` environment variable. Digests are
written to `Regression/`, which is gitignored: they describe the
internals of paid avatars.

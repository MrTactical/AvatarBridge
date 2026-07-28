# AvatarBridge — project instructions

## ALWAYS: docs move with the code, same commit

Any change that alters what a user sees, does, or should expect **must update the documentation in
the same commit** — never "later", never as a follow-up version. This rule exists because drift
happened repeatedly: the README described removed labels, dead defaults and superseded behaviour
within *minutes* of the code changing, and stale docs on a public repo mislead exactly the people
two open issues are pointing at it.

Surfaces to check on every user-facing change:

1. **README.md** — the section for the changed feature, the "What gets converted" table, the
   Highlights list, the options tables (defaults must match `BridgeSettings` *exactly*), the
   vrc3cvr comparison table, Known limitations, and Troubleshooting.
2. **Window UI text** — tooltips and labels in `Editor/AvatarBridgeWindow.cs` for the changed
   setting; labels in the README must match the window verbatim.
3. **Settings comments** — the field comment in `Editor/Core/BridgeSettings.cs`.
4. **Report wording** — if behaviour changed, the `ctx.Report.*` strings describing it.
5. **Store description claims** — `Editor/Core/AvatarDescription.cs` checks menu entries and
   components by name; renames break its claims silently.

After editing README.md, verify internal anchors:

```bash
grep -o "](#[a-z0-9-]*)" README.md | sed 's/](#//;s/)//' | sort -u > /tmp/L.txt; grep "^#" README.md | sed 's/^#* //' | tr '[:upper:]' '[:lower:]' | sed 's/[^a-z0-9 -]//g;s/ /-/g;s/^-*//' | sort -u > /tmp/H.txt; comm -23 /tmp/L.txt /tmp/H.txt
```

Empty output = all anchors resolve.

## Standing project rules (summary — details in auto-memory)

- **Never reuse a shipped version number** (`Editor/BridgeDefines.cs`); bump instead. The build
  script refuses to overwrite an existing `.unitypackage`, and old packages are never deleted.
- **Compile all five configurations** before any build: plain, `AVATARBRIDGE_DECLS`,
  `AVATARBRIDGE_DYNBONE` (+stub), no-CCK, no-VRC.
- **All work lands on the `dev` branch** (created 2026-07-28 from v2.50.6). Commit there and
  push `dev` freely — it is the visible work-in-progress. **`main` only moves when the
  maintainer explicitly says to batch it**: merges to main, tags and releases each require
  explicit instruction, per instance. Never commit directly to main.
- **The Unity editor cannot falsify physics, shaders, or sync behaviour** — only wearing the
  avatar in ChilloutVR counts as verification, and the report/README must not claim otherwise.
- This file is **excluded from the built `.unitypackage`** (the build script skips it); it needs
  no `.meta`.

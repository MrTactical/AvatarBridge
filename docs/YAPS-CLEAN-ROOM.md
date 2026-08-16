# YAPS and VRCFury's SPS: the clean-room posture

YAPS is inspired by [VRCFury](https://vrcfury.com/)'s SPS, which invented mesh-deforming
penetration for VRChat and is credited as prior art in the README. This note says what that
means for the code.

## What ships

**No VRCFury code.** Not their patched shader, not their includes, not transcribed functions.
The deform in `Editor/Yaps/yaps_*.cginc` and the baker in `Editor/Yaps/YapsBaker.cs` were
written from an understanding of what SPS does, not from its source, and their structure is
their own: one nearest socket resolved over marker lights and a contact channel, not SPS's
screen-atlas chain.

VRCFury's licence permits derivatives for personal use with the notice retained and forbids
them under its commercial terms, which are drawn broadly. Rather than argue about which applies
to a converted avatar, nothing of theirs is redistributed, so no term of that licence is engaged.

## What is used

- **The bake texture idea and its documented layout.** A bake is the user's own mesh data in an
  arrangement produced by a tool they licensed and ran on their own avatar. Implementing a
  format is not copying an implementation; YAPS reads and writes its own bake with its own code.
- **The DPS marker light protocol** (ranges 0.41 hole, 0.42 ring, 0.45 front, 0.49 tracker).
  An interoperability wire format that predates SPS. Speaking it is the whole point of talking
  to content already on the platform. Facts about a protocol, not creative expression.
- **The contact tag names** TPS and SPS sockets and plugs answer to. The same reasoning: those
  strings are how content finds each other.

## Obligations kept

- SPS and VRCFury are credited as prior art in the README.
- AvatarBridge's YAPS support is not monetised, and will not be, donations included.

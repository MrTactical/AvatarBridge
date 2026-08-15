// Which system a knob comes from — DPS, TPS, SPS, or YAPS itself — so a
// user who knows a feature by the system they learned it in can find it
// by that name. Metadata on the field, beside its tooltip and range; the
// inspector draws it as a tag and lets the list be filtered by it.
//
// No dependency on any SDK, no #if. It compiles in an empty project.
using UnityEngine;

namespace AvatarBridge.Yaps
{
    public class YapsFromAttribute : PropertyAttribute
    {
        // "DPS", "TPS", "SPS", "YAPS", or several joined with " · ".
        public readonly string System;
        public YapsFromAttribute(string system) { System = system; }
    }
}

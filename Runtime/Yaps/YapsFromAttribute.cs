// Which system a knob came from: DPS, TPS, SPS or YAPS.
// The inspector shows it as a tag and can filter by it.
using UnityEngine;

namespace AvatarBridge.Yaps
{
    public class YapsFromAttribute : PropertyAttribute
    {
        // One name, or several joined with " · ".
        public readonly string System;
        public YapsFromAttribute(string system) { System = system; }
    }
}

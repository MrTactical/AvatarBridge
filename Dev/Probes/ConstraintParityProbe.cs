// Where does a Unity RotationConstraint disagree with the VRChat one it replaced?
//
// Issue #6 reports that freezing rotation axes behaves differently between the
// two. Guessing at the mechanism produced one wrong answer already, so this
// asks both solvers the same question instead: it builds the same rig twice,
// once with each component, sweeps the configuration space, and prints every
// combination whose driven transform ends up somewhere different.
//
// Both solvers run in edit mode. VRChat's does because VRCConstraintManager
// executes its jobs outside play mode by default, which is what makes a
// side-by-side comparison possible without entering play.
//
//   -executeMethod AvatarBridge.Regression.ConstraintParityProbe.Run
#if VRC_SDK_VRCSDK3 && UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Dynamics;

namespace AvatarBridge.Regression
{
    public static class ConstraintParityProbe
    {
        class Case
        {
            public string Name;
            public Transform UnityDriven;
            public Transform VrcDriven;
            public Vector3 Start;
        }

        static readonly List<Case> cases = new List<Case>();
        static int ticks;

        static readonly Vector3 SourceEuler = new Vector3(25f, 35f, -15f);

        public static void Run()
        {
            var axisSets = new (Axis axis, bool x, bool y, bool z, string name)[]
            {
                (Axis.X, true, false, false, "X"),
                (Axis.Y, false, true, false, "Y"),
                (Axis.Z, false, false, true, "Z"),
                (Axis.X | Axis.Y, true, true, false, "XY"),
                (Axis.X | Axis.Z, true, false, true, "XZ"),
                (Axis.Y | Axis.Z, false, true, true, "YZ"),
                (Axis.X | Axis.Y | Axis.Z, true, true, true, "XYZ"),
            };
            var drivenStarts = new[] { Vector3.zero, new Vector3(12f, 40f, -25f) };
            var rests = new[] { Vector3.zero, new Vector3(5f, -10f, 15f) };
            var offsets = new[] { Vector3.zero, new Vector3(7f, 0f, 0f) };
            var weights = new[] { 1f, 0.5f, 0f };
            var sourceWeights = new[] { 1f, 0.5f };
            var parentEulers = new[] { Vector3.zero, new Vector3(0f, 90f, 0f) };
            // VRChat's solver corrects the parent's rotation for lossy scale
            // before it decomposes anything. Whether Unity's does is the
            // question a uniformly scaled rig can never ask.
            var parentScales = new[] { Vector3.one, new Vector3(1f, 0.5f, 1f) };
            var sourceCounts = new[] { 1, 2 };

            var source = new GameObject("Source").transform;
            source.localEulerAngles = SourceEuler;
            var second = new GameObject("Source2").transform;
            second.localEulerAngles = new Vector3(-40f, 10f, 60f);

            foreach (var axes in axisSets)
            foreach (var start in drivenStarts)
            foreach (var rest in rests)
            foreach (var offset in offsets)
            foreach (var weight in weights)
            foreach (var sourceWeight in sourceWeights)
            foreach (var parentEuler in parentEulers)
            foreach (var parentScale in parentScales)
            foreach (var sourceCount in sourceCounts)
            {
                string name = $"axes={axes.name} start={V(start)} rest={V(rest)} off={V(offset)} " +
                              $"w={weight} sw={sourceWeight} parent={V(parentEuler)} " +
                              $"scale={V(parentScale)} sources={sourceCount}";

                var unityDriven = MakeDriven(parentEuler, parentScale, start);
                var u = unityDriven.gameObject.AddComponent<RotationConstraint>();
                u.AddSource(new ConstraintSource { sourceTransform = source, weight = sourceWeight });
                if (sourceCount > 1)
                {
                    u.AddSource(new ConstraintSource { sourceTransform = second, weight = 1f });
                }
                u.rotationAtRest = rest;
                u.rotationOffset = offset;
                u.rotationAxis = axes.axis;
                u.weight = weight;
                u.constraintActive = true;
                u.locked = true;

                var vrcDriven = MakeDriven(parentEuler, parentScale, start);
                var v = vrcDriven.gameObject.AddComponent<VRCRotationConstraint>();
                v.Sources.Add(new VRCConstraintSource(source, sourceWeight));
                if (sourceCount > 1)
                {
                    v.Sources.Add(new VRCConstraintSource(second, 1f));
                }
                v.RotationAtRest = rest;
                v.RotationOffset = offset;
                v.AffectsRotationX = axes.x;
                v.AffectsRotationY = axes.y;
                v.AffectsRotationZ = axes.z;
                v.GlobalWeight = weight;
                v.IsActive = true;
                v.Locked = true;

                cases.Add(new Case
                {
                    Name = name,
                    UnityDriven = unityDriven,
                    VrcDriven = vrcDriven,
                    Start = start
                });
            }

            Debug.Log($"PROBE built {cases.Count} paired cases");
            EditorApplication.update += Tick;
        }

        // The converter reads the axis flags BY NAME, and its reflection helper
        // returns the fallback when a member does not resolve. The fallback for
        // an axis flag is TRUE, so a renamed field would not fail: it would
        // quietly convert every constraint as affecting all three axes, which
        // is exactly what "freeze rotation axes doesn't work the same" looks
        // like from the outside.
        public static void RunNames()
        {
            var go = new GameObject("NameCheck");
            var v = go.AddComponent<VRCRotationConstraint>();
            v.AffectsRotationX = true;
            v.AffectsRotationY = false;
            v.AffectsRotationZ = false;

            foreach (var name in new[] { "AffectsRotationX", "AffectsRotationY", "AffectsRotationZ",
                                         "AffectsPositionX", "RotationAtRest", "RotationOffset",
                                         "GlobalWeight", "IsActive", "Locked", "SolveInLocalSpace",
                                         "FreezeToWorld", "Sources" })
            {
                var t = v.GetType();
                var p = t.GetProperty(name, System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance);
                var f = t.GetField(name, System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance);
                object value = p != null ? p.GetValue(v) : f != null ? f.GetValue(v) : null;
                Debug.Log($"PROBE name {name,-20} " +
                    (p != null ? "property" : f != null ? "field   " : "MISSING ") +
                    $"  value={value}");
            }
            EditorApplication.Exit(0);
        }

        // Does a CHAIN of constraints keep up? VRChat sorts constraints by
        // dependency before solving (VRCConstraintGrouper), so a constraint
        // reading a bone another constraint writes sees this frame's value.
        // Unity has no such sort, so the same chain can lag by a frame per
        // link, which only shows while the source is moving.
        static Transform chainSource;
        static readonly List<Case> chainCases = new List<Case>();

        public static void RunChain()
        {
            chainSource = new GameObject("ChainSource").transform;

            foreach (bool reversed in new[] { false, true })
            {
                var unityLinks = MakeChain(reversed, vrc: false);
                var vrcLinks = MakeChain(reversed, vrc: true);
                for (int i = 0; i < unityLinks.Count; i++)
                {
                    chainCases.Add(new Case
                    {
                        Name = $"link {i + 1} of 3, created {(reversed ? "leaf first" : "root first")}",
                        UnityDriven = unityLinks[i],
                        VrcDriven = vrcLinks[i],
                        Start = Vector3.zero
                    });
                }
            }

            Debug.Log($"PROBE chain: {chainCases.Count} links paired");
            EditorApplication.update += ChainTick;
        }

        static List<Transform> MakeChain(bool reversed, bool vrc)
        {
            var links = new List<Transform>();
            for (int i = 0; i < 3; i++)
            {
                links.Add(new GameObject($"Link{i}").transform);
            }
            // Creation order is what a solver with no dependency sort is left
            // to fall back on, so building the chain leaf first is the case
            // that separates "sorted" from "whatever order they registered".
            var order = reversed ? Enumerable.Range(0, 3).Reverse() : Enumerable.Range(0, 3);
            foreach (int i in order)
            {
                var target = i == 0 ? chainSource : links[i - 1];
                if (vrc)
                {
                    var v = links[i].gameObject.AddComponent<VRCRotationConstraint>();
                    v.Sources.Add(new VRCConstraintSource(target, 1f));
                    v.IsActive = true;
                    v.Locked = true;
                }
                else
                {
                    var u = links[i].gameObject.AddComponent<RotationConstraint>();
                    u.AddSource(new ConstraintSource { sourceTransform = target, weight = 1f });
                    u.constraintActive = true;
                    u.locked = true;
                }
            }
            return links;
        }

        static void ChainTick()
        {
            // Moving, because a lag is invisible once everything settles.
            chainSource.localEulerAngles = new Vector3(0f, ticks * 6f, 0f);
            EditorApplication.QueuePlayerLoopUpdate();
            if (++ticks < 8)
            {
                return;
            }
            EditorApplication.update -= ChainTick;
            Debug.Log($"PROBE chain source at {V(chainSource.localEulerAngles)}");
            foreach (var c in chainCases)
            {
                float delta = Quaternion.Angle(c.UnityDriven.localRotation, c.VrcDriven.localRotation);
                Debug.Log($"PROBE chain {(delta > 0.1f ? "MISMATCH" : "match   ")} {delta,6:0.##} deg  " +
                    $"{c.Name}: unity={V(c.UnityDriven.localEulerAngles)} " +
                    $"vrc={V(c.VrcDriven.localEulerAngles)}");
            }
            EditorApplication.Exit(0);
        }

        static Transform MakeDriven(Vector3 parentEuler, Vector3 parentScale, Vector3 start)
        {
            var parent = new GameObject("Parent").transform;
            parent.localEulerAngles = parentEuler;
            parent.localScale = parentScale;
            var driven = new GameObject("Driven").transform;
            driven.SetParent(parent, false);
            driven.localEulerAngles = start;
            return driven;
        }

        static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (++ticks < 8)
            {
                return;
            }
            EditorApplication.update -= Tick;
            Report();
            EditorApplication.Exit(0);
        }

        static void Report()
        {
            var mismatched = new List<string>();
            foreach (var c in cases)
            {
                float delta = Quaternion.Angle(c.UnityDriven.localRotation, c.VrcDriven.localRotation);
                if (delta > 0.1f)
                {
                    mismatched.Add($"{delta,7:0.##} deg  {c.Name}\n" +
                        $"            unity={V(c.UnityDriven.localEulerAngles)} " +
                        $"vrc={V(c.VrcDriven.localEulerAngles)}");
                }
            }

            // Two components that both did nothing agree perfectly, so the
            // comparison means nothing until each is shown to have moved
            // something.
            int unityMoved = cases.Count(c => Quaternion.Angle(c.UnityDriven.localRotation,
                Quaternion.Euler(c.Start)) > 0.1f);
            int vrcMoved = cases.Count(c => Quaternion.Angle(c.VrcDriven.localRotation,
                Quaternion.Euler(c.Start)) > 0.1f);
            Debug.Log($"PROBE solvers ran: unity moved {unityMoved}/{cases.Count}, " +
                $"vrc moved {vrcMoved}/{cases.Count}");
            var sample = cases[cases.Count / 2];
            Debug.Log($"PROBE sample {sample.Name}; start={V(sample.Start)} " +
                $"unity={V(sample.UnityDriven.localEulerAngles)} " +
                $"vrc={V(sample.VrcDriven.localEulerAngles)}");
            Debug.Log($"PROBE {mismatched.Count} of {cases.Count} configurations disagree");
            foreach (var line in mismatched.Take(40))
            {
                Debug.Log("PROBE MISMATCH " + line);
            }

            // Which single setting the disagreements have in common. A factor
            // present in every mismatch and absent from every match is the
            // mechanism; one merely correlated is a lead.
            foreach (var factor in new[] { "axes=X ", "axes=Y ", "axes=Z ", "axes=XY", "axes=XZ",
                                           "axes=YZ", "axes=XYZ", "start=(0.0,0.0,0.0)", "w=0.5",
                                           "sw=0.5", "parent=(0.0,90.0,0.0)", "rest=(0.0,0.0,0.0)",
                                           "off=(0.0,0.0,0.0)", "w=0 ", "scale=(1.0,0.5,1.0)",
                                           "sources=2" })
            {
                int bad = mismatched.Count(m => m.Contains(factor));
                int all = cases.Count(c => c.Name.Contains(factor));
                Debug.Log($"PROBE factor {factor,-22} {bad}/{all} disagree");
            }
        }

        static string V(Vector3 v) => $"({v.x:0.0},{v.y:0.0},{v.z:0.0})";
    }
}
#endif

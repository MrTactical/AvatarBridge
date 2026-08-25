// Two fixture avatars for the corpus classes it never had, and the
// BHFBUNNY face look, in one headless pass.
//
// Fixture_DeformSocket: a socket with a dedicated blendshaped mesh, so
// the bake path and the shader's depth stages finally run under
// regression. Fixture_HeadTransplant: a hand-built vrcfAlwaysVisibleHead
// exactly as VRCFury bakes it, disabled, with a socket beneath it, so
// the transplant class is finally in the corpus.
//
//   Unity.exe -batchmode -quit -executeMethod AvatarBridge.Dev.FixtureBuilder.Run
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Dev
{
    public static class FixtureBuilder
    {
        const string SourceScene = "Sootie Scene";
        static readonly string Log = "D:/AvatarBridge/Regression/fixture-builder.log";
        static StringBuilder _log = new StringBuilder();

        public static void Run()
        {
            int code = 0;
            try
            {
                BuildFixtures();
                BhfFaceLook();
            }
            catch (Exception e)
            {
                _log.AppendLine($"FAILED: {e}");
                code = 1;
            }
            File.WriteAllText(Log, _log.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static string FindScene(string nameContains)
        {
            return AssetDatabase.FindAssets("t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p)
                    .IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // Only the new one. Run() rebuilds all three from the source
        // scene, which would overwrite two fixtures the corpus already has
        // a baseline for — and the source scene has been edited by hand
        // since they were made, so they would not come back the same.
        //
        //   Unity.exe -batchmode -quit -executeMethod AvatarBridge.Dev.FixtureBuilder.RunStrafeOnly
        public static void RunStrafeOnly()
        {
            int code = 0;
            try
            {
                string source = FindScene(SourceScene);
                if (source == null) throw new Exception($"no scene matching \"{SourceScene}\"");
                string dir = Path.GetDirectoryName(source).Replace('\\', '/');
                BuildOne(source, dir + "/Fixture_AsymmetricStrafe.unity", AddAsymmetricStrafe);
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                _log.AppendLine("FAILED: " + e);
                code = 1;
            }
            File.WriteAllText(Log, _log.ToString());
            EditorApplication.Exit(code);
        }

        static void BuildFixtures()
        {
            string source = FindScene(SourceScene);
            if (source == null) throw new Exception($"no scene matching \"{SourceScene}\"");
            string dir = Path.GetDirectoryName(source).Replace('\\', '/');

            BuildOne(source, dir + "/Fixture_DeformSocket.unity", AddDeformSocket);
            BuildOne(source, dir + "/Fixture_HeadTransplant.unity", AddTransplantSocket);
            BuildOne(source, dir + "/Fixture_AsymmetricStrafe.unity", AddAsymmetricStrafe);
            AssetDatabase.SaveAssets();
        }

        static void BuildOne(string source, string target, Action<VRCAvatarDescriptor> mutate)
        {
            if (File.Exists(target)) AssetDatabase.DeleteAsset(target);
            if (!AssetDatabase.CopyAsset(source, target)) throw new Exception($"could not copy to {target}");
            var scene = EditorSceneManager.OpenScene(target, OpenSceneMode.Single);
            var descriptor = scene.GetRootGameObjects()
                .Select(r => r.GetComponentInChildren<VRCAvatarDescriptor>(true))
                .FirstOrDefault(d => d != null);
            if (descriptor == null) throw new Exception($"{target}: no descriptor");
            mutate(descriptor);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            _log.AppendLine($"built {target}");
        }

        static Transform Bone(VRCAvatarDescriptor d, HumanBodyBones bone)
        {
            var animator = d.GetComponent<Animator>();
            var t = animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
            if (t == null) throw new Exception($"no {bone} bone");
            return t;
        }

        static Component AddFurySocket(GameObject host, string name)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VF.Component.VRCFuryHapticSocket"))
                .FirstOrDefault(t => t != null);
            if (type == null) throw new Exception("VRCFuryHapticSocket type not found");
            var comp = host.AddComponent(type);
            var so = new SerializedObject(comp);
            so.FindProperty("addLight").enumValueIndex = 1;   // Hole
            so.FindProperty("name").stringValue = name;
            so.ApplyModifiedPropertiesWithoutUndo();
            return comp;
        }

        // A socket whose mesh is its own and carries blendshapes: the
        // bake path, which no corpus avatar has ever exercised.
        static void AddDeformSocket(VRCAvatarDescriptor d)
        {
            var hips = Bone(d, HumanBodyBones.Hips);
            var socket = new GameObject("Fixture Socket");
            socket.transform.SetParent(hips, false);
            socket.transform.localPosition = new Vector3(0f, -0.05f, 0.05f);

            var meshGo = new GameObject("Fixture Socket Mesh");
            meshGo.transform.SetParent(socket.transform, false);
            var smr = meshGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = TubeWithShapes();
            smr.rootBone = meshGo.transform;
            smr.bones = new[] { meshGo.transform };
            var shader = Shader.Find("YAPS/Simple Lit");
            smr.sharedMaterial = new Material(shader != null ? shader : Shader.Find("Standard"));
            string matDir = "Assets/FixtureAssets";
            if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder("Assets", "FixtureAssets");
            AssetDatabase.CreateAsset(smr.sharedMaterial, matDir + "/FixtureSocket.mat");
            AssetDatabase.CreateAsset(smr.sharedMesh, matDir + "/FixtureSocket.asset");

            AddFurySocket(socket, "Fixture Hole");
            _log.AppendLine("  deform socket on Hips, 2 blendshapes, YAPS Simple Lit");
        }

        // The user's class, built by hand exactly as Fury's bake leaves
        // it: a disabled proxy head pinned to the real one by constraint,
        // with the socket beneath it. The transplant must save this.
        static void AddTransplantSocket(VRCAvatarDescriptor d)
        {
            var head = Bone(d, HumanBodyBones.Head);
            var fake = new GameObject("vrcfAlwaysVisibleHead");
            fake.transform.SetParent(head.parent, false);
            fake.transform.SetPositionAndRotation(head.position, head.rotation);
            var constraint = fake.AddComponent<ParentConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = head, weight = 1f });
            constraint.constraintActive = true;

            var socket = new GameObject("Fixture Mouth");
            socket.transform.SetParent(fake.transform, false);
            socket.transform.localPosition = new Vector3(0f, -0.03f, 0.08f);
            AddFurySocket(socket, "Fixture Mouth");

            // Disabled is the point: this is how it arrives in the wild.
            fake.SetActive(false);
            _log.AppendLine("  vrcfAlwaysVisibleHead (disabled, constrained to Head), socket beneath");
        }

        // A short open tube around the origin, +Z along the axis, with
        // two shapes: Open flares the mouth, Bulge swells the middle.
        static Mesh TubeWithShapes()
        {
            const int seg = 8, rings = 3;
            const float radius = 0.02f, length = 0.08f;
            var verts = new Vector3[seg * rings];
            var norms = new Vector3[seg * rings];
            for (int r = 0; r < rings; r++)
            {
                float z = -length * r / (rings - 1);
                for (int s = 0; s < seg; s++)
                {
                    float a = Mathf.PI * 2f * s / seg;
                    var radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    verts[r * seg + s] = radial * radius + new Vector3(0, 0, z);
                    norms[r * seg + s] = radial;
                }
            }
            var tris = new System.Collections.Generic.List<int>();
            for (int r = 0; r < rings - 1; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * seg + s, b = r * seg + (s + 1) % seg;
                    int c = a + seg, e = b + seg;
                    tris.AddRange(new[] { a, b, c, b, e, c });
                }
            var mesh = new Mesh { name = "Fixture Socket Tube", vertices = verts, normals = norms };
            mesh.SetTriangles(tris, 0);
            var weights = new BoneWeight[verts.Length];
            for (int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            mesh.boneWeights = weights;
            mesh.bindposes = new[] { Matrix4x4.identity };

            var open = new Vector3[verts.Length];
            var bulge = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var radial = new Vector3(verts[i].x, verts[i].y, 0f).normalized;
                if (i < seg) open[i] = radial * 0.015f;                       // mouth ring flares
                if (i >= seg && i < seg * 2) bulge[i] = radial * 0.01f;       // middle swells
            }
            mesh.AddBlendShapeFrame("Open", 100f, open, null, null);
            mesh.AddBlendShapeFrame("Bulge", 100f, bulge, null, null);
            return mesh;
        }

        // Convert BHFBUNNY, then drive the face sliders and say exactly
        // which blendshapes move and which curves claim them. The sweep
        // says responded=0 where the baseline said 2; this names why.
        static void BhfFaceLook()
        {
            string scenePath = FindScene("BHFBUNNY");
            if (scenePath == null) { _log.AppendLine("BHF: no scene found"); return; }
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var descriptor = scene.GetRootGameObjects()
                .Select(r => r.GetComponentInChildren<VRCAvatarDescriptor>(true))
                .FirstOrDefault(x => x != null);
            if (descriptor == null) { _log.AppendLine("BHF: no descriptor"); return; }
            for (var t = descriptor.transform; t != null; t = t.parent)
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);

            BridgeConverter.Convert(descriptor, new BridgeSettings());
            var target = Selection.activeGameObject;
            if (target == null) { _log.AppendLine("BHF: conversion produced no target"); return; }
            var animator = target.GetComponent<Animator>();
            var controller = animator != null
                ? BridgeContext.Underlying(animator.runtimeAnimatorController) : null;
            if (controller == null) { _log.AppendLine("BHF: no controller"); return; }

            foreach (string parameter in new[] { "VRCFaceBlendH", "VRCFaceBlendV" })
            {
                _log.AppendLine($"BHF: === {parameter} ===");
                if (!controller.parameters.Any(p => p.name == parameter))
                {
                    _log.AppendLine("  parameter not in controller");
                    continue;
                }
                // Which curves read it: blend trees keyed on the parameter.
                foreach (var layer in controller.layers)
                {
                    foreach (var child in layer.stateMachine.states)
                    {
                        if (child.state.motion is BlendTree tree
                            && (tree.blendParameter == parameter || tree.blendParameterY == parameter))
                        {
                            _log.AppendLine($"  layer \"{layer.name}\" weight={layer.defaultWeight} " +
                                            $"state \"{child.state.name}\" tree \"{tree.name}\"");
                            foreach (var m in tree.children)
                            {
                                if (!(m.motion is AnimationClip clip)) continue;
                                foreach (var b in AnimationUtility.GetCurveBindings(clip).Take(6))
                                {
                                    bool resolves = target.transform.Find(b.path) != null;
                                    _log.AppendLine($"    {clip.name}: {b.path} | {b.propertyName}" +
                                                    (resolves ? "" : "  <- PATH DOES NOT RESOLVE"));
                                }
                            }
                        }
                    }
                }
                // Drive it and watch every face blendshape.
                var faces = target.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(s => s.sharedMesh != null && s.sharedMesh.blendShapeCount > 0).ToList();
                var before = faces.ToDictionary(f => f, Snapshot);
                animator.SetFloat(parameter, 1f);
                animator.Update(0f);
                for (int i = 0; i < 30; i++) animator.Update(1f / 60f);
                int moved = 0;
                foreach (var f in faces)
                {
                    var now = Snapshot(f);
                    var was = before[f];
                    for (int i = 0; i < now.Length; i++)
                    {
                        if (Mathf.Abs(now[i] - was[i]) < 0.5f) continue;
                        _log.AppendLine($"  MOVED {f.name}.{f.sharedMesh.GetBlendShapeName(i)}: {was[i]:0.#} -> {now[i]:0.#}");
                        moved++;
                    }
                }
                if (moved == 0) _log.AppendLine("  nothing moved on any mesh");
                animator.SetFloat(parameter, 0f);
                animator.Update(0f);
            }
        }


        // A Base layer whose locomotion tree has a DIFFERENT clip on the
        // left and the right of every sideways direction.
        //
        // The corpus has never held this shape: every avatar in it either
        // mirrors its strafe or has no velocity tree at all, so the graft's
        // folding of east and west into one direction bucket has never had
        // anything to lose. Here it does. Grafted correctly the CCK tree
        // ends up with StrafeL at x<0 and StrafeR at x>0; folded, one of
        // them is written to both sides and the other never appears.
        //
        // The digest records blend tree children as position=>motion, so
        // the failure and the fix are both readable there without a single
        // extra field.
        static void AddAsymmetricStrafe(VRCAvatarDescriptor d)
        {
            string dir = "Assets/FixtureAssets";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "FixtureAssets");

            var controller = AnimatorController.CreateAnimatorControllerAtPath(dir + "/FixtureStrafe.controller");
            controller.AddParameter("VelocityX", AnimatorControllerParameterType.Float);
            controller.AddParameter("VelocityZ", AnimatorControllerParameterType.Float);

            var tree = new BlendTree
            {
                name = "Standing Blend",
                blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = "VelocityX",
                blendParameterY = "VelocityZ",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            // Cardinals and diagonals, each side its own clip. Magnitudes
            // sit on one ring so the classifier reads them as one speed.
            var placements = new (string name, float x, float z)[]
            {
                ("Fix_Idle", 0f, 0f),
                ("Fix_Fwd", 0f, 1f),
                ("Fix_Back", 0f, -1f),
                ("Fix_StrafeL", -1f, 0f),
                ("Fix_StrafeR", 1f, 0f),
                ("Fix_FwdDiagL", -0.7f, 0.7f),
                ("Fix_FwdDiagR", 0.7f, 0.7f),
                ("Fix_BackDiagL", -0.7f, -0.7f),
                ("Fix_BackDiagR", 0.7f, -0.7f),
            };
            foreach (var (name, x, z) in placements)
            {
                tree.AddChild(StrafeClip(dir, name), new Vector2(x, z));
            }

            var machine = controller.layers[0].stateMachine;
            var standing = machine.AddState("Standing");
            standing.motion = tree;
            machine.defaultState = standing;

            var layers = d.baseAnimationLayers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].type != VRCAvatarDescriptor.AnimLayerType.Base) continue;
                layers[i].animatorController = controller;
                layers[i].isDefault = false;
                layers[i].isEnabled = true;
            }
            d.baseAnimationLayers = layers;
            d.customizeAnimationLayers = true;
            EditorUtility.SetDirty(d);
            AssetDatabase.SaveAssets();
            _log.AppendLine($"asymmetric strafe: {placements.Length} clips, distinct per side");
        }

        // One clip per placement, each moving the root a different amount
        // so no two are equal and the digest can tell them apart by name.
        static AnimationClip StrafeClip(string dir, string name)
        {
            string path = dir + "/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) return existing;
            var clip = new AnimationClip { name = name, wrapMode = WrapMode.Loop };
            float lift = 0.01f * (Mathf.Abs(name.GetHashCode()) % 20 + 1);
            clip.SetCurve("", typeof(Transform), "m_LocalPosition.y",
                AnimationCurve.Linear(0f, 0f, 1f, lift));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        static float[] Snapshot(SkinnedMeshRenderer smr)
        {
            var w = new float[smr.sharedMesh.blendShapeCount];
            for (int i = 0; i < w.Length; i++) w[i] = smr.GetBlendShapeWeight(i);
            return w;
        }
    }
}
#endif

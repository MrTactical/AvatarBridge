// A socket you can SEE react. Depth animation, made visible.
//
// The other sockets in the kit are inert: they say where they are and what
// kind they are, and that is all. This one is a tube that bulges around
// whatever goes into it, which is what a real socket's author builds and
// what "depth reactions" actually means to anyone looking.
//
// ---------------------------------------------------------------------
// HOW A PROP ANIMATES AT ALL
// ---------------------------------------------------------------------
//
// A prop cannot carry the avatar trigger the converter uses — that is on
// the client's local-avatar whitelist and nothing else. Spawnables have
// their own path, and it lands in the same place by a different route:
//
//   CVRSpawnableTrigger  stay task, SetFromPosition on Z
//     -> CVRSpawnable.syncValues[0], a float the prop owns
//       -> the Animator parameter that value is mapped to
//         -> a blend tree driving the Bulge blendshape
//
// Sampling POSITION rather than distance is what makes it read correctly:
// the value is how far along the tube's own Z axis the plug's tip has
// travelled, so the bulge follows where the plug actually is instead of
// merely swelling when something is nearby.
#if UNITY_EDITOR && CVR_CCK_EXISTS
using System.Collections.Generic;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Spike
{
    public static class YapsTubeProp
    {
        const string Dir = "Assets/SpsSpike/Props";
        const float Length = 0.30f;
        const float Radius = 0.045f;
        const float BulgeAmount = 0.035f;   // how far the wall pushes out
        const int Around = 24;
        const int Along = 32;

        // Legacy RING digits. A tube open at both ends is a ring, not a
        // hole: a hole swallows, tapering everything past its entrance to
        // nothing so it cannot poke out the far side of a body, which is
        // right for a body and wrong for something you push through.
        const float RootRange = 0.4206f;
        const float FrontRange = 0.4506f;

        [MenuItem("AvatarBridge/Spike/Build YAPS tube socket (bulges)")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/SpsSpike"))
            {
                AssetDatabase.CreateFolder("Assets", "SpsSpike");
            }
            if (!AssetDatabase.IsValidFolder(Dir))
            {
                AssetDatabase.CreateFolder("Assets/SpsSpike", "Props");
            }

            var root = new GameObject("YAPS Tube Socket");

            // --- the mesh, and the shape that bulges it ------------------
            var mesh = BuildTube();
            AssetDatabase.CreateAsset(mesh,
                AssetDatabase.GenerateUniqueAssetPath(Dir + "/YAPS Tube Mesh.asset"));

            var body = new GameObject("Tube");
            body.transform.SetParent(root.transform, false);
            // Skinned, because blendshapes need one. No bones: a mesh with
            // no bone weights renders from its own transform, and this only
            // ever has to bulge.
            var skin = body.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.rootBone = body.transform;
            skin.updateWhenOffscreen = true;
            skin.sharedMaterial = TubeMaterial();

            // --- what turns depth into a bulge ---------------------------
            var controller = BuildController();
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var spawnable = root.AddComponent<CVRSpawnable>();
            spawnable.spawnHeight = 1.2f;
            // Filling the list is not enough — the flag is what makes the
            // client look at it, and without it the trigger reports that
            // the spawnable uses no additional values while the value sits
            // right there.
            spawnable.useAdditionalValues = true;
            spawnable.syncValues.Add(new CVRSpawnableValue
            {
                name = "Depth",
                startValue = 0f,
                updatedBy = CVRSpawnableValue.UpdatedBy.None,   // a trigger drives it
                updateMethod = CVRSpawnableValue.UpdateMethod.Override,
                animator = animator,
                animatorParameterName = "Depth",
            });

            // The box is the tube's own volume, so "where along Z" means "how
            // far in".
            var trigger = root.AddComponent<CVRSpawnableTrigger>();
            trigger.areaSize = new Vector3(Radius * 2f, Radius * 2f, Length);
            trigger.areaOffset = new Vector3(0, 0, Length * 0.5f);
            trigger.useAdvancedTrigger = true;
            // ZNegative, not positive. A socket faces OUT of its opening —
            // that is why the front marker sits at +Z — so the plug enters
            // from +Z and travels toward -Z as it goes deeper. Sampling the
            // positive direction reads one at the entrance and zero at the
            // far end, and the tube deflates as the plug goes in.
            trigger.sampleDirection = CVRSpawnableTrigger.SampleDirection.ZNegative;
            trigger.allowedTypes = PlugTypes;
            trigger.stayTasks.Add(new CVRSpawnableTriggerTaskStay
            {
                settingIndex = 0,
                updateMethod = CVRSpawnableTriggerTaskStay.UpdateMethod.SetFromPosition,
                minValue = 0f,
                maxValue = 1f,
            });
            // Nothing writes a stay task once the plug leaves, so without
            // this the tube stays swollen around nothing.
            trigger.exitTasks.Add(new CVRSpawnableTriggerTask
            {
                settingIndex = 0,
                settingValue = 0f,
                updateMethod = CVRSpawnableTriggerTask.UpdateMethod.Override,
            });

            // --- so a plug can find it -----------------------------------
            MarkerLight(root.transform, "Root", RootRange, Vector3.zero);
            MarkerLight(root.transform, "Front", FrontRange, new Vector3(0, 0, 0.01f));

            // A ROOT and a FRONT, in both ecosystems' names, exactly as a
            // converted avatar's socket carries them.
            //
            // The root alone is a destination. A plug given only that can
            // aim at the tube but not thread it, because nothing says which
            // way the tube faces — and threading is the entire point of a
            // tube. The second point, a centimetre along +Z, is what says
            // it: subtracting one from the other IS the axis, which is how
            // TPS and SPS have always described a socket's facing.
            //
            // Both namings because a plug should not have to care which
            // system dressed the socket it just met. Without the TPS pair a
            // real TPS penetrator cannot see this tube at all, since the
            // SPSLL names mean nothing to it.
            SocketPointer("Socket Root", "SPSLL_Socket_Ring", Vector3.zero);
            SocketPointer("Socket Front", "SPSLL_Socket_Front", new Vector3(0, 0, 0.01f));
            SocketPointer("Orifice Root", "TPS_Orf_Root", Vector3.zero);
            SocketPointer("Orifice Norm", "TPS_Orf_Norm", new Vector3(0, 0, 0.01f));

            void SocketPointer(string name, string type, Vector3 at)
            {
                var pointer = new GameObject(name);
                pointer.transform.SetParent(root.transform, false);
                pointer.transform.localPosition = at;
                pointer.AddComponent<CVRPointer>().type = type;
            }

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.height = Length;
            capsule.radius = Radius * 1.3f;
            capsule.center = new Vector3(0, 0, Length * 0.5f);
            // A trigger, so nothing shoves. Two solid pickups push each
            // other apart, which keeps the plug from ever reaching the
            // inside of the box that measures depth — and an intermittent
            // shove reads as an intermittent feature. Raycasts still hit
            // triggers, so it stays grabbable.
            capsule.isTrigger = true;

            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.drag = 6f;
            rigidbody.angularDrag = 6f;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<CVRPickupObject>().maximumGrabDistance = 8f;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Dir + "/YAPS Tube Socket.prefab");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();

            Debug.Log("[YAPS] Tube socket built in " + Dir + ". Push a plug in along its axis " +
                      "and the tube swells around it, tracking how far in the tip has gone " +
                      "rather than merely that something is close. It reads as a RING, so the " +
                      "plug passes straight through rather than being swallowed: a tube open at " +
                      "both ends is a ring, while a hole tapers everything past its entrance to " +
                      "nothing, which suits a body and not this.");
            Selection.activeObject = prefab;
        }

        // The plug end of a contact. Tip is what depth is measured from.
        // The TIP alone. A plug also announces its ROOT, and that sits at
        // the base of the shaft — on the wearer's hips — so accepting it
        // means the tube measures depth from somebody's crotch being nearby
        // rather than from anything going in. It swells as you walk up to
        // it and stays swollen.
        //
        // Worse than wrong, it is unstable: a trigger reports whichever
        // allowed pointer entered last, so tip and root take turns and the
        // depth jumps between them. That is the stutter.
        //
        // Depth is how far the TIP has travelled down the tube. Only the
        // tip can answer that.
        static readonly string[] PlugTypes =
        {
            "TPS_Pen_Penetrating", "SPSLL_Pen_Penetrating",
        };

        // --- the mesh --------------------------------------------------

        // An open tube along +Z, with one blendshape that pushes its middle
        // outward. The bulge is weighted by a raised cosine along the
        // length, so it swells smoothly instead of stepping, and it is
        // strongest at the centre — which is where a plug of about this
        // length would sit.
        static Mesh BuildTube()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var deltas = new List<Vector3>();
            var triangles = new List<int>();

            for (int ring = 0; ring <= Along; ring++)
            {
                float t = ring / (float) Along;
                float z = t * Length;
                // Raised cosine: zero at both ends, one in the middle.
                float swell = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f);
                for (int a = 0; a < Around; a++)
                {
                    float angle = a / (float) Around * Mathf.PI * 2f;
                    var outward = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                    vertices.Add(outward * Radius + new Vector3(0, 0, z));
                    // Inward-facing, since a tube is seen from inside as
                    // much as out.
                    normals.Add(-outward);
                    deltas.Add(outward * (BulgeAmount * swell));
                }
            }

            for (int ring = 0; ring < Along; ring++)
            {
                for (int a = 0; a < Around; a++)
                {
                    int next = (a + 1) % Around;
                    int here = ring * Around;
                    int up = (ring + 1) * Around;
                    triangles.Add(here + a); triangles.Add(up + next); triangles.Add(up + a);
                    triangles.Add(here + a); triangles.Add(here + next); triangles.Add(up + next);
                }
            }

            var mesh = new Mesh { name = "YAPS Tube" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            // Normals shift with the wall, tangents do not meaningfully, so
            // only the two that matter are supplied.
            var deltaNormals = new Vector3[vertices.Count];
            mesh.AddBlendShapeFrame("Bulge", 100f, deltas.ToArray(), deltaNormals,
                new Vector3[vertices.Count]);
            return mesh;
        }

        // --- depth to bulge --------------------------------------------

        static AnimatorController BuildController()
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(Dir + "/YAPS Tube Depth.controller");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Depth", AnimatorControllerParameterType.Float);

            var tree = new BlendTree
            {
                name = "Depth",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Depth",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            foreach (float at in new[] { 0f, 1f })
            {
                var clip = new AnimationClip { name = $"Bulge {at:0}" };
                clip.SetCurve("Tube", typeof(SkinnedMeshRenderer), "blendShape.Bulge",
                    AnimationCurve.Constant(0f, 1f / 60f, at * 100f));
                AssetDatabase.AddObjectToAsset(clip, controller);
                tree.AddChild(clip, at);
            }

            var layer = controller.layers[0];
            var state = layer.stateMachine.AddState("Depth");
            state.writeDefaultValues = true;
            state.motion = tree;
            layer.stateMachine.defaultState = state;
            controller.layers = new[] { layer };

            EditorUtility.SetDirty(controller);
            return controller;
        }

        // A tube is seen from inside as much as out, and single-sided
        // rendering makes half of it vanish — including the half you are
        // looking through when a plug goes in. Unity's Standard shader has
        // no cull switch, so this prefers a shader that does and only falls
        // back to Standard when there is none.
        static Material TubeMaterial()
        {
            var shader = Shader.Find(".poiyomi/Poiyomi Toon")
                         ?? Shader.Find("Poiyomi/Poiyomi Toon")
                         ?? Shader.Find(".poiyomi/Poiyomi Pro")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = "YAPS Tube" };
            material.color = new Color(0.85f, 0.5f, 0.55f);
            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", 0f);   // both faces
            }
            else
            {
                Debug.LogWarning("[YAPS] The tube is on a shader with no cull switch, so its " +
                                 "far wall will disappear when you look through it. Import " +
                                 "Poiyomi, or set the material two-sided by hand.");
            }
            AssetDatabase.CreateAsset(material,
                AssetDatabase.GenerateUniqueAssetPath(Dir + "/YAPS Tube.mat"));
            return material;
        }

        static void MarkerLight(Transform parent, string name, float range, Vector3 at)
        {
            var go = new GameObject("Marker " + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            // Black, intensity one. Zero intensity would have Unity drop it
            // from the per-object list and the socket would be invisible.
            light.color = Color.black;
            light.intensity = 1f;
            light.bounceIntensity = 0f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForceVertex;
        }
    }
}
#endif

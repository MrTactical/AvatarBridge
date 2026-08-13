// Two props for testing YAPS in game without needing a second person.
//
// Spawn both, pick one up, move it at the other, and watch. The plug bends
// toward the socket, arrives along its axis, and relaxes when pulled away.
// That is the whole feature, testable alone in about ten seconds.
//
// The plug prop carries NO parameters and no channel at all, deliberately.
// It finds the socket entirely through the marker lights, which is the
// path a plug uses against every piece of DPS content ChilloutVR already
// has, and the tier a converted avatar falls back to when its sync budget
// is full. If this pair works in game, that path is proven.
//
// The socket prop also carries a CVRPointer tagged the way a converted
// avatar's socket is tagged, so a converted avatar's CONTACT channel fires
// on it too. Wear Angela, spawn this, and both paths are under test at
// once — the light path on the prop plug, the contact channel on her.
//
// Props were checked against the client before being built this way:
// AssetFilter.FilterProp explicitly allows Light, and it accepts avatar
// whitelist components as well as spawnable ones, so lights and material
// drivers both survive on a prop. That is not true of the trigger, which
// is local-avatar only — hence no channel here.
#if UNITY_EDITOR && CVR_CCK_EXISTS
using System.Collections.Generic;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Spike
{
    public static class YapsPropRig
    {
        const string Dir = "Assets/SpsSpike/Props";
        const float PlugLength = 0.25f;
        const float PlugRadius = 0.028f;

        // Root must outrange front, and these are the only two digits the
        // legacy DPS protocol left unclaimed. Same values the converter
        // writes onto a real avatar's sockets.
        const float RootRange = 0.4706f;
        const float FrontRange = 0.4006f;

        [MenuItem("AvatarBridge/Spike/Build YAPS test props")]
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

            // Three sockets, because one that emits both lights and a
            // contact pointer cannot tell you which of the two made the
            // plug move. Colour says which is which in game.
            var socket = BuildSocket("YAPS Test Socket", new Color(0.9f, 0.35f, 0.6f),
                lights: true, pointer: true, hole: false);
            var lightsOnly = BuildSocket("YAPS Test Socket (lights only)",
                new Color(0.95f, 0.8f, 0.2f), lights: true, pointer: false, hole: false);
            var contactOnly = BuildSocket("YAPS Test Socket (contact only)",
                new Color(0.25f, 0.6f, 0.95f), lights: false, pointer: true, hole: false);
            // Every socket above is a RING, which is why the taper has never
            // shown itself: a ring lets the plug pass straight through and
            // only a hole closes around it. Nothing built so far could say
            // "hole" — the pointer tag was the generic root, and our own
            // light digits deliberately carry no kind.
            var holeSocket = BuildSocket("YAPS Test Socket (hole)",
                new Color(0.4f, 0.15f, 0.5f), lights: true, pointer: true, hole: true);

            // The one an actual user wants, as opposed to the four above,
            // which are instruments for telling paths apart while
            // debugging. Universal turns out to mean speaking LEGACY rather
            // than speaking more: our decoder reads the legacy digits
            // already, so a socket carrying only the legacy pair is
            // understood by YAPS plugs AND by every DPS plug on the
            // platform, on two lights rather than four. Our own inverted
            // encoding only earns its keep on an avatar with a dozen
            // sockets fighting over four slots — which no prop is.
            var universal = BuildSocket("YAPS Test Socket (universal)",
                new Color(0.25f, 0.75f, 0.4f), lights: true, pointer: true, hole: false,
                legacy: true);
            var plug = BuildPlug();
            AssetDatabase.SaveAssets();

            Debug.Log("[YAPS] Test props built in " + Dir + ".\n" +
                      "PINK socket: lights and a contact pointer, the realistic one.\n" +
                      "YELLOW socket: marker lights only, the path existing DPS content uses.\n" +
                      "BLUE socket: contact pointer only, no lights, the only way to see the " +
                      "channel working on its own.\n" +
                      "PURPLE socket: a HOLE. Every other socket here is a ring, which is why " +
                      "the taper never showed itself. Push the plug into this one and it should " +
                      "narrow and stop instead of passing through.\n" +
                      "Taper is tunable on the PLUG's material: YAPS hole taper start and end, " +
                      "as fractions of plug length. Widen the gap for a soft grip, close it for " +
                      "an abrupt one.\n" +
                      "GREEN socket: UNIVERSAL, and the one a real user wants. Legacy encoding, " +
                      "so YAPS plugs and every DPS plug already on the platform both understand " +
                      "it. The other four are instruments for telling paths apart while " +
                      "debugging, not products.\n" +
                      "The plug PROP has no channel, so it reacts to pink, yellow, purple and " +
                      "green, and never to blue. A converted AVATAR should react to all five.");
            Selection.objects = new Object[]
                { universal, socket, lightsOnly, contactOnly, holeSocket, plug };
        }

        // --- the socket ------------------------------------------------

        static GameObject BuildSocket(string name, Color colour, bool lights, bool pointer, bool hole,
            bool legacy = false)
        {
            var root = new GameObject(name);
            var body = new GameObject("Ring");
            body.transform.SetParent(root.transform, false);

            var mesh = BuildRingMesh();
            AssetDatabase.CreateAsset(mesh,
                AssetDatabase.GenerateUniqueAssetPath(Dir + "/YAPS Socket Ring.asset"));
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = SolidMaterial(name, colour);

            // The two marker lights: black, no shadows, vertex-only. Black
            // because the decoder rejects anything carrying colour as
            // somebody's real lighting; vertex-only because that is what
            // keeps them out of ChilloutVR's Advanced Safety light budget.
            if (lights)
            {
                // Legacy states the KIND in its digit, which is the only way
                // a light can say it at all — our own two digits went on
                // root and front and carry none. So a hole is always legacy,
                // and a universal socket is legacy by choice, which is what
                // makes it readable by content that predates all of this.
                float rootRange = hole ? 0.4106f : legacy ? 0.4206f : RootRange;
                float frontRange = hole || legacy ? 0.4506f : FrontRange;
                MarkerLight(root.transform, "Root", rootRange, Vector3.zero);
                MarkerLight(root.transform, "Front", frontRange, new Vector3(0, 0, 0.01f));
            }

            // Tagged the way a converted avatar's socket is tagged, so a
            // converted avatar's contact channel reacts to this prop too.
            if (pointer)
            {
                var host = new GameObject("Socket Pointer");
                host.transform.SetParent(root.transform, false);
                host.AddComponent<CVRPointer>().type =
                    hole ? "SPSLL_Socket_Hole" : legacy ? "SPSLL_Socket_Ring" : "SPSLL_Socket_Root";
            }

            // Tight to the ring. Two pickups cannot overlap, so every
            // centimetre of collider is a centimetre the plug can never
            // get closer than — and the interesting part of the deform is
            // the arrival.
            var sphere = root.AddComponent<SphereCollider>();
            sphere.radius = 0.05f;
            MakeGrabbable(root);

            return SaveAsPrefab(root, Dir + "/" + name + ".prefab");
        }

        static void MarkerLight(Transform parent, string name, float range, Vector3 at)
        {
            var go = new GameObject("Marker " + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            // Black, but NOT zero intensity. Black is what makes it carry
            // no illumination and what lets the decoder tell a protocol
            // light from somebody's real lighting. Intensity zero is
            // something else entirely: Unity drops a light that contributes
            // nothing from the per-object light list, so the slot the
            // decoder is reading never gets filled and the socket is
            // invisible. The prop pair failed on exactly this.
            light.color = Color.black;
            light.intensity = 1f;
            light.bounceIntensity = 0f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForceVertex;
        }

        // --- the plug --------------------------------------------------

        static GameObject BuildPlug()
        {
            var root = new GameObject("YAPS Test Plug Prop");
            var body = new GameObject("Plug");
            body.transform.SetParent(root.transform, false);

            var mesh = BuildPlugMesh();
            AssetDatabase.CreateAsset(mesh, Dir + "/YAPS Plug Mesh.asset");
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = body.AddComponent<MeshRenderer>();

            var shader = Shader.Find("AvatarBridge/YAPS Test Plug");
            if (shader == null)
            {
                Debug.LogError("[YAPS] The YAPS Test Plug shader is missing — the prop will not " +
                               "deform. It lives in Tools/SpsSpike/YapsTestPlug.shader.");
                return SaveAsPrefab(root, Dir + "/YAPS Test Plug Prop.prefab");
            }
            renderer.sharedMaterial = new Material(shader);

            // The real baker, on a real renderer. The plug object IS the
            // plug frame here, so no vertex frame recovery is needed.
            var result = YapsBaker.Bake(renderer, body.transform, Dir, null, out string failure);
            if (result == null)
            {
                Debug.LogError("[YAPS] Could not bake the prop plug: " + failure);
                return SaveAsPrefab(root, Dir + "/YAPS Test Plug Prop.prefab");
            }

            var material = YapsBaker.Apply(result, renderer.sharedMaterial, shader, Dir, false);
            material.SetFloat("_YAPS_Enabled", 1f);
            material.SetFloat("_YAPS_Overrun", 1f);
            // No channel on a prop: the trigger that feeds one is local
            // avatar only. Position and engagement both come from the
            // socket's marker lights.
            material.SetFloat("_YAPS_ChannelSpace", 0f);
            material.SetFloat("_YAPS_SelfTag", -1f);   // a prop wears no sockets of its own
            material.SetFloat("_YAPS_TaperStart", 0.05f);
            material.SetFloat("_YAPS_TaperEnd", 0.10f);
            renderer.sharedMaterial = material;

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 2;   // along Z, the shaft
            capsule.height = PlugLength;
            capsule.radius = PlugRadius * 1.6f;
            capsule.center = new Vector3(0, 0, PlugLength * 0.5f);
            MakeGrabbable(root);

            return SaveAsPrefab(root, Dir + "/YAPS Test Plug Prop.prefab");
        }

        // A pickup needs something to raycast against and something to
        // move. Without a collider there is nothing to point at, and
        // without a rigidbody the default Rigidbody move mode has nothing
        // to drive — so the prop spawns, renders, and cannot be touched.
        //
        // Gravity off on purpose: these are aimed at each other, and a
        // prop that drops to the floor the moment you let go is useless
        // for that. Drag holds it where it was left.
        static void MakeGrabbable(GameObject root)
        {
            var body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = false;
            body.drag = 6f;
            body.angularDrag = 6f;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var pickup = root.AddComponent<CVRPickupObject>();
            pickup.maximumGrabDistance = 8f;

            var spawnable = root.AddComponent<CVRSpawnable>();
            spawnable.spawnHeight = 1.2f;   // chest height, not at your feet
        }

        // --- meshes ----------------------------------------------------

        // A shaft along +Z with a rounded tip. Segmented finely enough
        // along its length that a bend reads as a curve rather than as
        // four flat facets.
        static Mesh BuildPlugMesh()
        {
            const int around = 20;
            const int along = 28;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();

            for (int ring = 0; ring <= along; ring++)
            {
                float t = ring / (float) along;
                float z = t * PlugLength;
                // Taper the last fifth into a dome so it reads as a tip.
                float radius = t < 0.8f
                    ? PlugRadius
                    : PlugRadius * Mathf.Cos((t - 0.8f) / 0.2f * Mathf.PI * 0.5f);
                for (int a = 0; a < around; a++)
                {
                    float angle = a / (float) around * Mathf.PI * 2f;
                    var offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                    vertices.Add(offset * radius + new Vector3(0, 0, z));
                    normals.Add(offset);
                }
            }

            for (int ring = 0; ring < along; ring++)
            {
                for (int a = 0; a < around; a++)
                {
                    int next = (a + 1) % around;
                    int here = ring * around;
                    int up = (ring + 1) * around;
                    triangles.Add(here + a); triangles.Add(up + a); triangles.Add(up + next);
                    triangles.Add(here + a); triangles.Add(up + next); triangles.Add(here + next);
                }
            }

            var mesh = new Mesh { name = "YAPS Plug Mesh" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A ring lying in XY so its axis is +Z, plus a spike along +Z, so
        // which way the socket faces is obvious at a glance in game. Which
        // way it faces matters: the deform flips a socket's axis to meet
        // the approach, and seeing that happen is half the test.
        static Mesh BuildRingMesh()
        {
            const float major = 0.05f, minor = 0.012f;
            const int around = 24, through = 12;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();

            for (int i = 0; i < around; i++)
            {
                float u = i / (float) around * Mathf.PI * 2f;
                var centre = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f) * major;
                for (int j = 0; j < through; j++)
                {
                    float v = j / (float) through * Mathf.PI * 2f;
                    var outward = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f) * Mathf.Cos(v)
                                  + Vector3.forward * Mathf.Sin(v);
                    vertices.Add(centre + outward * minor);
                    normals.Add(outward);
                }
            }
            for (int i = 0; i < around; i++)
            {
                for (int j = 0; j < through; j++)
                {
                    int a = i * through + j;
                    int b = i * through + (j + 1) % through;
                    int c = ((i + 1) % around) * through + j;
                    int d = ((i + 1) % around) * through + (j + 1) % through;
                    triangles.Add(a); triangles.Add(c); triangles.Add(d);
                    triangles.Add(a); triangles.Add(d); triangles.Add(b);
                }
            }

            // The facing spike.
            int spikeBase = vertices.Count;
            const int spikeAround = 8;
            for (int a = 0; a < spikeAround; a++)
            {
                float angle = a / (float) spikeAround * Mathf.PI * 2f;
                var offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.006f;
                vertices.Add(offset);
                normals.Add(offset.normalized);
            }
            vertices.Add(new Vector3(0, 0, 0.06f));
            normals.Add(Vector3.forward);
            int tip = vertices.Count - 1;
            for (int a = 0; a < spikeAround; a++)
            {
                triangles.Add(spikeBase + a);
                triangles.Add(tip);
                triangles.Add(spikeBase + (a + 1) % spikeAround);
            }

            var mesh = new Mesh { name = "YAPS Socket Ring" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // --- plumbing --------------------------------------------------

        static Material SolidMaterial(string name, Color colour)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = name };
            material.color = colour;
            AssetDatabase.CreateAsset(material,
                AssetDatabase.GenerateUniqueAssetPath(Dir + "/" + name + ".mat"));
            return material;
        }

        static GameObject SaveAsPrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
#endif

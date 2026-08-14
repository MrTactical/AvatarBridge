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
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
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

            // TPS orifices are contacts and nothing else — the system has no
            // marker lights anywhere in it — so this is the blue socket
            // wearing the other ecosystem's tag, and that is exactly what
            // makes it worth building: it proves the channel matches
            // TPS_Orf_Root as readily as it matches the SPS names.
            var tpsSocket = BuildSocket("YAPS Test Socket (TPS)",
                new Color(0.95f, 0.45f, 0.15f), lights: false, pointer: true, hole: false,
                tps: true);
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
                      "ORANGE socket: TPS. Contacts and nothing else, which is all TPS ever " +
                      "had, tagged TPS_Orf_Root and TPS_Orf_Norm. Functionally the blue socket " +
                      "in the other ecosystem's clothes, so it answers one question only: does " +
                      "the channel match the TPS names as well as the SPS ones.\n" +
                      "The plug PROP has no channel, so it reacts to pink, yellow, purple and " +
                      "green, and never to blue or orange. A converted AVATAR should react to " +
                      "all six.\n" +
                      "Test the contact-only sockets ALONE. A lit socket anywhere within a plug " +
                      "length or so refines the position, and until it is proven otherwise a " +
                      "nearby green or pink will mask whether blue or orange did anything.");
            Selection.objects = new Object[]
                { universal, socket, lightsOnly, contactOnly, holeSocket, tpsSocket, plug };
        }

        // --- the socket ------------------------------------------------

        static GameObject BuildSocket(string name, Color colour, bool lights, bool pointer, bool hole,
            bool legacy = false, bool tps = false)
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
                host.AddComponent<CVRPointer>().type = tps ? "TPS_Orf_Root"
                    : hole ? "SPSLL_Socket_Hole"
                    : legacy ? "SPSLL_Socket_Ring" : "SPSLL_Socket_Root";

                // TPS says which way it faces with a SECOND pointer a little
                // way along its normal, because it has no light to say it in.
                // Nothing reads this yet — the deform takes its direction
                // from the approach instead, which is why a converted socket
                // works from any side — but a socket without it is not the
                // shape TPS content actually has, and this prop exists to be
                // that shape.
                if (tps)
                {
                    var norm = new GameObject("Socket Normal");
                    norm.transform.SetParent(root.transform, false);
                    norm.transform.localPosition = new Vector3(0, 0, 0.01f);
                    norm.AddComponent<CVRPointer>().type = "TPS_Orf_Norm";
                }
            }

            // Tight to the ring. Two pickups cannot overlap, so every
            // centimetre of collider is a centimetre the plug can never
            // get closer than — and the interesting part of the deform is
            // the arrival.
            var sphere = root.AddComponent<SphereCollider>();
            sphere.radius = 0.05f;
            // A trigger, so two props never shove each other apart.
            // Raycasts still hit triggers, so it stays grabbable.
            sphere.isTrigger = true;
            MakeGrabbable(root);

            return SaveAsPrefab(root, Dir + "/" + name + ".prefab");
        }

        static void PlugPointer(Transform parent, string name, string type, Vector3 at)
        {
            var go = new GameObject("Plug " + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.AddComponent<CVRPointer>().type = type;
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
            // A prop gets a channel of its own. The avatar route is barred
            // to it — CVRAdvancedAvatarSettingsTrigger exists only on a
            // wearer's client — but a spawnable has its own equivalent, and
            // its values are synced by the client rather than recomputed per
            // viewer. So the prop reads contact-only sockets, which is every
            // TPS orifice and any SPS socket whose author turned lights off,
            // and everyone sees the same bend. See BuildPropChannel.
            material.SetFloat("_YAPS_ChannelSpace", 1f);
            material.SetFloat("_YAPS_SelfTag", -1f);   // a prop wears no sockets of its own
            // Start narrowing a twentieth of a plug length past the hole and
            // close over the next third of one. The old 0.10 shut it over a
            // twentieth, which reads as the shaft popping out of existence
            // rather than sinking into something.
            material.SetFloat("_YAPS_TaperStart", 0.05f);
            material.SetFloat("_YAPS_TaperEnd", 0.35f);
            renderer.sharedMaterial = material;

            // The plug END of a contact. Without these the prop can bend
            // toward a socket and never trigger anything in it: depth
            // reactions live on the socket and fire on a plug's tags, so a
            // plug that announces nothing arrives invisibly.
            //
            // Tip and root are separate points because that is how depth is
            // measured — a socket compares the two to know how far in
            // something has gone, which is exactly what drives a bulge.
            PlugPointer(root.transform, "Tip", "TPS_Pen_Penetrating",
                new Vector3(0, 0, PlugLength));
            PlugPointer(root.transform, "Root", "TPS_Pen_Root", Vector3.zero);
            PlugPointer(root.transform, "Width", "TPS_Pen_Width",
                new Vector3(PlugRadius, 0, 0));

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 2;   // along Z, the shaft
            capsule.height = PlugLength;
            capsule.radius = PlugRadius * 1.6f;
            capsule.center = new Vector3(0, 0, PlugLength * 0.5f);
            capsule.isTrigger = true;
            MakeGrabbable(root);
            BuildPropChannel(root, body, material, result);

            return SaveAsPrefab(root, Dir + "/YAPS Test Plug Prop.prefab");
        }

        // --- the prop's own contact channel ----------------------------
        //
        // The same five values the avatar publishes — engaged, is-hole, and
        // the socket's offset on three axes — carried by the one transport a
        // prop actually has. A CVRSpawnableValue is synced by the client and
        // writes straight into an Animator parameter, so a blend tree can
        // put it on the material with no driver component in between.
        //
        // Reach and encoding match the avatar exactly, because the shader
        // decoding them cannot tell it is reading a prop.
        const float BoxLengths = 1.75f;

        static readonly string[] SocketTypes =
        {
            "TPS_Orf_Root", "TPS_Orf_Root_SelfNotOnHips",
            "SPSLL_Socket_Root", "SPSLL_Socket_Root_SelfNotOnHips",
            "SPSLL_Socket_Hole", "SPSLL_Socket_Hole_SelfNotOnHips",
            "SPSLL_Socket_Ring", "SPSLL_Socket_Ring_SelfNotOnHips",
        };
        static readonly string[] HoleTypes =
        {
            "SPSLL_Socket_Hole", "SPSLL_Socket_Hole_SelfNotOnHips",
        };
        // The second point, which is what says which way a socket faces.
        static readonly string[] FrontTypes =
        {
            "TPS_Orf_Norm", "TPS_Orf_Norm_SelfNotOnHips",
            "SPSLL_Socket_Front", "SPSLL_Socket_Front_SelfNotOnHips",
        };

        static void BuildPropChannel(GameObject root, GameObject body, Material material,
            YapsBaker.Result result)
        {
            float extent = Mathf.Max(result.Length, 0.01f) * BoxLengths;
            var box = new Vector3(extent * 2f, extent * 2f, extent * 2f);
            material.SetVector("_YAPS_ChannelExtents", new Vector4(extent, extent, extent, 0f));

            string path = body.name;
            var controller = BuildChannelController(path);
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            // A prop the viewer is not looking at still has to hold its
            // shape: the deform is the whole point and it is driven from
            // here.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // MakeGrabbable already added one, and a second would be a
            // second prop as far as the client is concerned.
            var spawnable = root.GetComponent<CVRSpawnable>();
            // Filling the list is not enough — this flag is what makes the
            // client look at it at all.
            spawnable.useAdditionalValues = true;
            foreach (string name in new[] { "E", "H", "X", "Y", "Z", "FX", "FY", "FZ" })
            {
                spawnable.syncValues.Add(new CVRSpawnableValue
                {
                    name = name,
                    startValue = 0f,
                    updatedBy = CVRSpawnableValue.UpdatedBy.None,   // triggers drive these
                    updateMethod = CVRSpawnableValue.UpdateMethod.Override,
                    animator = animator,
                    animatorParameterName = name,
                });
            }

            // ONE OBJECT PER TRIGGER. The client turns each trigger into a
            // ContactReceiver added to the trigger's own GameObject, and
            // gives that receiver the trigger's shape — so two triggers
            // sharing an object means two receivers on it fighting over one
            // shape, and the channel reports nothing at all. Stacking them
            // was silent: no error, no warning, just a prop that had stopped
            // reading contacts.
            //
            // Each sits on the frame measured from the MESH rather than on
            // the object's transform, which on the avatar was 28 cm out and
            // put every socket half a plug length short.
            GameObject Host(string name)
            {
                var host = new GameObject(name);
                host.transform.SetParent(root.transform, false);
                host.transform.SetPositionAndRotation(result.Origin, result.Rotation);
                return host;
            }

            // Engagement. Distance-only, so a SPHERE, and areaSize.x is its
            // radius outright rather than half of it.
            var engage = Host("YAPS Channel E").AddComponent<CVRSpawnableTrigger>();
            engage.areaSize = box * 0.5f;
            engage.useAdvancedTrigger = true;
            engage.allowedTypes = SocketTypes;
            engage.stayTasks.Add(new CVRSpawnableTriggerTaskStay
            {
                settingIndex = 0,
                updateMethod = CVRSpawnableTriggerTaskStay.UpdateMethod.SetFromDistance,
                minValue = 0f,
                maxValue = 1f,
            });
            engage.exitTasks.Add(new CVRSpawnableTriggerTask
            {
                settingIndex = 0,
                settingValue = 0f,
                updateMethod = CVRSpawnableTriggerTask.UpdateMethod.Override,
            });

            // Hole or ring. Enter and exit only, which also counts as
            // distance-only, so this is a sphere too.
            var hole = Host("YAPS Channel H").AddComponent<CVRSpawnableTrigger>();
            hole.areaSize = box * 0.5f;
            hole.useAdvancedTrigger = true;
            hole.allowedTypes = HoleTypes;
            hole.enterTasks.Add(new CVRSpawnableTriggerTask
            {
                settingIndex = 1,
                settingValue = 1f,
                updateMethod = CVRSpawnableTriggerTask.UpdateMethod.Override,
            });
            hole.exitTasks.Add(new CVRSpawnableTriggerTask
            {
                settingIndex = 1,
                settingValue = 0f,
                updateMethod = CVRSpawnableTriggerTask.UpdateMethod.Override,
            });

            // Where the socket sits, one axis each. These carry a position
            // task, so they really are boxes and want the full size.
            // Twice over: the socket's root, then the second point that says
            // which way it faces. Subtracting one from the other is the
            // axis, which is the one thing the light path had and this
            // channel did not — a bare point can only be aimed at, so the
            // plug reached the socket instead of threading it.
            var axes = new[]
            {
                (2, "X", SocketTypes, CVRSpawnableTrigger.SampleDirection.XPositive),
                (3, "Y", SocketTypes, CVRSpawnableTrigger.SampleDirection.YPositive),
                (4, "Z", SocketTypes, CVRSpawnableTrigger.SampleDirection.ZPositive),
                (5, "FX", FrontTypes, CVRSpawnableTrigger.SampleDirection.XPositive),
                (6, "FY", FrontTypes, CVRSpawnableTrigger.SampleDirection.YPositive),
                (7, "FZ", FrontTypes, CVRSpawnableTrigger.SampleDirection.ZPositive),
            };
            foreach (var (index, name, types, direction) in axes)
            {
                var axis = Host("YAPS Channel " + name).AddComponent<CVRSpawnableTrigger>();
                axis.areaSize = box;
                axis.sampleDirection = direction;
                axis.useAdvancedTrigger = true;
                axis.allowedTypes = types;
                axis.stayTasks.Add(new CVRSpawnableTriggerTaskStay
                {
                    settingIndex = index,
                    updateMethod = CVRSpawnableTriggerTaskStay.UpdateMethod.SetFromPosition,
                    minValue = 0f,
                    maxValue = 1f,
                });
            }
        }

        // One layer per value, each a two-motion blend tree: a clip holding
        // the property at 0 and one holding it at 1, blended by the
        // parameter, which makes the material property track the value.
        static AnimatorController BuildChannelController(string path)
        {
            string dir = Dir + "/YAPS Plug Channel.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(
                AssetDatabase.GenerateUniqueAssetPath(dir));
            // A fresh controller arrives with a Base Layer nobody asked for.
            // Counted down rather than looped on the length, so a
            // RemoveLayer that ever declines to remove cannot hang the
            // editor.
            for (int i = controller.layers.Length - 1; i >= 0; i--)
            {
                controller.RemoveLayer(i);
            }

            var properties = new (string Value, string Property)[]
            {
                ("E", "material._YAPS_SocketFlags.x"),
                ("H", "material._YAPS_SocketFlags.y"),
                ("X", "material._YAPS_SocketPos.x"),
                ("Y", "material._YAPS_SocketPos.y"),
                ("Z", "material._YAPS_SocketPos.z"),
                ("FX", "material._YAPS_SocketFront.x"),
                ("FY", "material._YAPS_SocketFront.y"),
                ("FZ", "material._YAPS_SocketFront.z"),
            };

            foreach (var (value, property) in properties)
            {
                controller.AddParameter(value, AnimatorControllerParameterType.Float);

                var tree = new BlendTree
                {
                    name = value,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = value,
                    useAutomaticThresholds = false,
                    hideFlags = HideFlags.HideInHierarchy,
                };
                tree.AddChild(PropertyClip(path, property, 0f), 0f);
                tree.AddChild(PropertyClip(path, property, 1f), 1f);

                var machine = new AnimatorStateMachine
                {
                    name = value,
                    hideFlags = HideFlags.HideInHierarchy,
                };
                var state = machine.AddState("Blend Tree");
                state.writeDefaultValues = true;
                state.motion = tree;
                machine.defaultState = state;

                // In memory until it is part of the asset, and dropped
                // silently on save otherwise — a layer, correctly named,
                // driving nothing.
                AssetDatabase.AddObjectToAsset(machine, controller);
                AssetDatabase.AddObjectToAsset(tree, controller);
                foreach (var child in tree.children)
                {
                    AssetDatabase.AddObjectToAsset(child.motion, controller);
                }

                var layers = controller.layers.ToList();
                layers.Add(new AnimatorControllerLayer
                {
                    name = value,
                    defaultWeight = 1f,
                    stateMachine = machine,
                });
                controller.layers = layers.ToArray();
            }

            AssetDatabase.SaveAssets();
            return controller;
        }

        static AnimationClip PropertyClip(string path, string property, float value)
        {
            var clip = new AnimationClip { name = property + " " + value };
            clip.SetCurve(path, typeof(MeshRenderer), property,
                AnimationCurve.Constant(0f, 1f / 60f, value));
            return clip;
        }

        // A pickup needs a COLLIDER, which is what a grab raycast hits. It
        // does not need a rigidbody from us: CVRPickupObject.Awake adds one
        // if the object has none, kinematic and with gravity off, which is
        // exactly what these want.
        //
        // So there is no rigidbody here and the move mode is Transform,
        // which routes the grab through a handler that moves the transform
        // directly instead of the physics one. These props are aimed at
        // each other and held against each other; simulating them buys
        // drift, shoving and jitter and nothing else. It also removes any
        // chance of a physics nudge fighting the contact channel, which
        // reads in game as a socket that will not hold still.
        //
        // (An earlier note here said a pickup without a rigidbody cannot be
        // touched. The collider was the real half of that.)
        static void MakeGrabbable(GameObject root)
        {
            var pickup = root.AddComponent<CVRPickupObject>();
            pickup.moveMode = CVRPickupObject.MoveMode.Transform;
            pickup.maximumGrabDistance = 8f;
            // Joe found this, and it is what makes a prop with a contact
            // channel usable by anyone but its owner.
            //
            // A channel value is written by whoever's SOCKET the prop met,
            // not by whoever is holding it — the client grants authority
            // when the sending contact belongs to your own avatar. Writing
            // one sends a full prop update carrying the prop's position and
            // marks it no longer remotely synced, so a remote player
            // bringing this to your socket had it taken out of their hands
            // the moment the socket switched on.
            //
            // DisallowTheft makes CanPickup refuse anyone who is not the
            // current holder while it is held, which closes that off and
            // leaves the channel working.
            pickup.disallowTheft = true;

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

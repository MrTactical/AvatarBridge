// How VRChat's PhysBone reads Endpoint Position on a scaled bone: in the
// bone's own scaled units, or in metres. Measured 2026-10-01: scaled units,
// as MagicaClothWriter's "_End" bone already places it (Far turned 3.03
// degrees, the angle a 10 m tip needs; Near 0.89, where a 0.1 m tip needs
// about 11). The SDK only builds the tip at runtime, so this asks the
// simulation: chains under a parent at scale 100, Endpoint Position 0.1, one
// collider 5 m out that only a tip in scaled units reaches, one just past the
// bone that any tip reaches. "Gravity" is no control: a bone pointing up has
// gravity along its own axis and never turns.
//
// Run: -executeMethod AvatarBridge.Regression.EndpointScaleProbe.Run (no -quit)
#if VRC_SDK_VRCSDK3
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static class EndpointScaleProbe
    {
        const string Key = "EndpointScaleProbe.frames";

        // Entering Play mode reloads the domain; this picks the probe back up.
        static EndpointScaleProbe()
        {
            if (SessionState.GetInt(Key, -1) >= 0) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Build("Far", new Vector3(0.2f, 5f, 0f), 0.5f, -1f, 0f);
            Build("Near", new Vector3(0.02f, 0.15f, 0f), 0.04f, 1f, 0f);
            Build("Gravity", null, 0f, 0f, 1f);
            SessionState.SetInt(Key, 0);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Build(string name, Vector3? colliderAt, float colliderRadius, float side, float gravity)
        {
            var root = new GameObject(name).transform;
            root.position = new Vector3(side * 20f, 0f, 0f);
            root.localScale = Vector3.one * 100f;
            var a = new GameObject("A").transform;
            a.SetParent(root, false);
            var b = new GameObject("B").transform;
            b.SetParent(a, false);
            b.localPosition = new Vector3(0f, 0.001f, 0f);   // 0.1 m in the world

            var pb = a.gameObject.AddComponent<VRCPhysBone>();
            pb.endpointPosition = new Vector3(0f, 0.1f, 0f);
            pb.gravity = gravity;
            pb.pull = 0.2f;
            pb.radius = 0.0001f;
            pb.allowCollision = VRCPhysBoneBase.AdvancedBool.True;
            if (colliderAt is Vector3 at)
            {
                var col = new GameObject(name + " collider").AddComponent<VRCPhysBoneCollider>();
                col.transform.position = root.position + at;
                col.shapeType = VRCPhysBoneColliderBase.ShapeType.Sphere;
                col.radius = colliderRadius;
                pb.colliders.Add(col);
            }
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            int frames = SessionState.GetInt(Key, 0) + 1;
            SessionState.SetInt(Key, frames);
            if (Time.time < 2f && frames < 2000) return;
            Debug.Log($"[EndpointScaleProbe] PhysBoneManager present: {Object.FindObjectOfType<PhysBoneManager>() != null}");
            foreach (string name in new[] { "Far", "Near", "Gravity" })
            {
                var b = GameObject.Find(name + "/A/B");
                float turned = b != null ? Quaternion.Angle(b.transform.localRotation, Quaternion.identity) : -1f;
                Debug.Log($"[EndpointScaleProbe] {name}: B turned {turned:F2} deg after {frames} frames, {Time.time:F2} s");
            }
            SessionState.EraseInt(Key);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }
    }
}
#endif

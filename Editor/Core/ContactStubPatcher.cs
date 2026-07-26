#if CVR_CCK_EXISTS
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// Generates a minimal declaration of ChilloutVR's native contact components so avatars can
    /// be authored with them.
    ///
    /// ChilloutVR replaced its pointer/trigger contacts with a system in the NAK.Contacts
    /// namespace that is a near-exact superset of VRChat's: the same shapes plus Box, the same
    /// allowSelf/allowOthers/localOnly/collisionTags fields under the same names, and receiver
    /// types covering Constant, OnEnter, three flavours of Proximity, and velocity. The catch is
    /// that it lives in the game client only — CCK 4.0.x ships no such types, so nothing can be
    /// authored against them and every conversion has to go through the legacy
    /// CVRPointer/CVRAdvancedAvatarSettingsTrigger approximation instead.
    ///
    /// It doesn't have to. Unity binds a MonoBehaviour inside an asset bundle by class name,
    /// namespace and assembly name rather than by GUID, so a declaration that matches on those
    /// three points and carries the same serialized fields is enough: the avatar serializes
    /// against this, and the client deserializes it straight onto its own real implementation,
    /// which is the code that then runs. This is the same trick the VRLabs DynamicBone stub uses.
    ///
    /// Only the serialized surface is reproduced. There is deliberately no behaviour, no editor
    /// UI and no gizmos — anything this file did at runtime would be dead code the moment the
    /// avatar is in game, because the client's implementation is what actually executes.
    ///
    /// Generated into the project rather than shipped as part of the package. If ChilloutVR ever
    /// ships these types in the CCK, two definitions of the same class would break compilation of
    /// the whole project — including this patcher, leaving nothing able to undo it. Writing the
    /// file only after confirming the real thing is absent, and deleting it again the moment the
    /// real thing appears, keeps that failure from being self-sealing.
    /// </summary>
    [InitializeOnLoad]
    public static class ContactStubPatcher
    {
        const string StubTypeName = "NAK.Contacts.ContactBase";
        const string MarkerInterface = "AvatarBridge.IGeneratedContactStub";
        const string FileName = "AvatarBridgeContactStub.cs";

        /// <summary>Bumped when the generated source changes, so old copies get rewritten.</summary>
        const string StubVersion = "3";
        const string VersionTag = "// AvatarBridge generated contact stub, revision " + StubVersion;

        /// <summary>
        /// The newest CCK whose contact surface these declarations were checked field-for-field
        /// against. Past this, the shape of the data is an assumption rather than a finding.
        /// </summary>
        const string VerifiedCckVersion = "4.0.1";

        static ContactStubPatcher()
        {
            EditorApplication.delayCall += Sync;
        }

        /// <summary>
        /// True when NAK.Contacts exists and is not the file this class generates — i.e. the
        /// real thing has arrived and the stub must get out of its way.
        /// </summary>
        static bool RealTypesPresent(out Type found)
        {
            found = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try { type = assembly.GetType(StubTypeName, false); }
                catch { continue; }
                if (type == null)
                {
                    continue;
                }
                found = type;
                // Matched by interface name rather than by a typeof() reference: this class must
                // stay compilable in the moments when the generated file does not exist.
                bool isOurs = type.GetInterfaces().Any(i => i.FullName == MarkerInterface);
                if (!isOurs)
                {
                    return true;
                }
            }
            return false;
        }

        static void Sync()
        {
            string dir = GeneratedFolder();
            if (dir == null)
            {
                return;
            }
            string path = dir + "/" + FileName;

            if (RealTypesPresent(out var real))
            {
                if (File.Exists(path))
                {
                    AssetDatabase.DeleteAsset(path);
                    Debug.Log("[AvatarBridge] ChilloutVR now provides " + StubTypeName + " itself (" +
                              real.Assembly.GetName().Name + "); removed the generated stub.");
                }
                return;
            }

            // A newer CCK than the one these declarations were verified against may have changed
            // the contact components without yet exposing them. Guessing at that would serialize
            // avatars against a layout nobody has checked, and the failure would be silent — data
            // quietly dropped on load. Refusing is the safer answer, and the conversion falls back
            // to the legacy pointer/trigger path on its own.
            string cck = InstalledCckVersion();
            if (cck != null && CompareVersions(cck, VerifiedCckVersion) > 0)
            {
                if (File.Exists(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }
                Debug.LogWarning("[AvatarBridge] CCK " + cck + " is newer than the " + VerifiedCckVersion +
                                 " these contact declarations were verified against, and it still doesn't " +
                                 "provide them itself. Not generating them — native contact conversion is " +
                                 "unavailable and AvatarBridge will use the legacy pointer/trigger path. If " +
                                 "the CCK's contacts are unchanged, raise VerifiedCckVersion in " +
                                 nameof(ContactStubPatcher) + ".");
                return;
            }

            string existing = File.Exists(path) ? File.ReadAllText(path) : null;
            if (existing != null && existing.Contains(VersionTag))
            {
                return;
            }

            Directory.CreateDirectory(dir);
            File.WriteAllText(path, StubSource);
            AssetDatabase.ImportAsset(path);
            Debug.Log("[AvatarBridge] Wrote " + path + " so ChilloutVR's native contact components " +
                      "can be authored. Delete it if the CCK ever ships them itself.");
        }

        /// <summary>
        /// A "Runtime" folder beside AvatarBridge's Editor folder. The location matters: with no
        /// assembly definition, anything outside an Editor folder lands in Assembly-CSharp, which
        /// is the assembly the client's own NAK.Contacts types live in, and matching it is half of
        /// what makes the binding work.
        /// </summary>
        /// <summary>
        /// ABI.CCK.Scripts.CVRCommon.BaseVersion, read reflectively so this file keeps compiling
        /// if the CCK moves or renames it. Null when it can't be found, which is treated as
        /// "can't tell" rather than "too new".
        /// </summary>
        static string InstalledCckVersion()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try { type = assembly.GetType("ABI.CCK.Scripts.CVRCommon", false); }
                catch { continue; }
                var field = type?.GetField("BaseVersion");
                if (field != null && field.GetValue(null) is string version && version.Length > 0)
                {
                    return version;
                }
            }
            return null;
        }

        /// <summary>Numeric dotted-version compare; unparsable parts count as 0.</summary>
        static int CompareVersions(string a, string b)
        {
            var left = a.Split('.');
            var right = b.Split('.');
            for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
            {
                int l = i < left.Length && int.TryParse(left[i], out var lv) ? lv : 0;
                int r = i < right.Length && int.TryParse(right[i], out var rv) ? rv : 0;
                if (l != r)
                {
                    return l.CompareTo(r);
                }
            }
            return 0;
        }

        static string GeneratedFolder()
        {
            var guids = AssetDatabase.FindAssets("t:MonoScript ContactStubPatcher");
            foreach (var guid in guids)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!scriptPath.EndsWith("/ContactStubPatcher.cs", StringComparison.Ordinal))
                {
                    continue;
                }
                // .../AvatarBridge/Editor/Core/ContactStubPatcher.cs -> .../AvatarBridge
                var dir = Path.GetDirectoryName(scriptPath)?.Replace('\\', '/');
                int editor = dir?.LastIndexOf("/Editor", StringComparison.Ordinal) ?? -1;
                if (editor > 0)
                {
                    return dir.Substring(0, editor) + "/Runtime";
                }
            }
            return null;
        }

        const string StubSource = VersionTag + @"
//
// DO NOT EDIT. Written by AvatarBridge's ContactStubPatcher, and rewritten whenever it changes.
// Safe to delete: it is regenerated on the next domain reload, and is removed automatically if a
// future CCK provides these types itself.
//
// This declares ChilloutVR's native contact components so avatars can be authored against them.
// The game client holds the real implementation; Unity binds serialized components in an asset
// bundle by class name, namespace and assembly name, so only those three things and the
// serialized field layout have to match. There is intentionally no behaviour here.
//
// Field names, types, defaults and order are copied from the client's own NAK.Contacts.
using System;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>Marks these declarations as AvatarBridge's, so the patcher can recognise its own
    /// work and stand down if ChilloutVR ever ships the real types.</summary>
    public interface IGeneratedContactStub { }
}

namespace NAK.Contacts
{
    public enum ShapeType : byte { Sphere, Capsule, Box }

    public enum ReceiverType : byte
    {
        Constant,
        OnEnter,
        ProximitySenderToReceiver,
        ProximityReceiverToSender,
        ProximityCenterToCenter,
        CopyValueFromSender,
        VelocityReceiver,
        VelocitySender,
        VelocityMagnitude
    }

    [Flags]
    public enum ContentType : byte { World = 1, Avatar = 2, Prop = 4, Player = 8 }

    [DefaultExecutionOrder(18200)]
    public abstract class ContactBase : MonoBehaviour, AvatarBridge.IGeneratedContactStub
    {
        public ShapeType shapeType;
        public Vector3 localPosition = Vector3.zero;
        public Quaternion localRotation = Quaternion.identity;
        public float radius = 0.5f;
        public float height = 1f;
        public Vector3 boxSize = Vector3.one;
        public bool allowSelf = true;
        public bool allowOthers = true;
        public bool localOnly;
        public ContentType contentTypes = ContentType.World | ContentType.Avatar | ContentType.Prop | ContentType.Player;
        public string[] collisionTags = Array.Empty<string>();
        public float contactValue = 1f;
        public bool drawGizmos = true;
        public Color gizmoColor = Color.green;

#if UNITY_EDITOR
        // Editor-only, and the one exception to this file having no behaviour. The client draws
        // these in game from its own copy of the class; without something here as well, a contact
        // is invisible in the scene view and there is no way to see or tune the volume you just
        // authored. drawGizmos and gizmoColor are honoured so the fields mean what they say.
        private void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            Gizmos.color = gizmoColor;
            Gizmos.matrix = transform.localToWorldMatrix
                            * Matrix4x4.TRS(localPosition, localRotation, Vector3.one);
            switch (shapeType)
            {
                case ShapeType.Box:
                    Gizmos.DrawWireCube(Vector3.zero, boxSize);
                    break;
                case ShapeType.Capsule:
                    // Two caps and the lines between them; height is the full span.
                    float half = Mathf.Max(0f, height * 0.5f - radius);
                    Vector3 a = Vector3.up * half, b = Vector3.down * half;
                    Gizmos.DrawWireSphere(a, radius);
                    Gizmos.DrawWireSphere(b, radius);
                    Gizmos.DrawLine(a + Vector3.right * radius, b + Vector3.right * radius);
                    Gizmos.DrawLine(a + Vector3.left * radius, b + Vector3.left * radius);
                    Gizmos.DrawLine(a + Vector3.forward * radius, b + Vector3.forward * radius);
                    Gizmos.DrawLine(a + Vector3.back * radius, b + Vector3.back * radius);
                    break;
                default:
                    Gizmos.DrawWireSphere(Vector3.zero, radius);
                    break;
            }
        }
#endif
    }

    public class ContactSender : ContactBase { }

    public class ContactReceiver : ContactBase
    {
        public ReceiverType receiverType;
    }

    // Bridges a ContactReceiver to an animator parameter. The client's version subscribes to the
    // receiver's contact events and writes the collision's target value straight onto the
    // animator, which is why a receiver alone drives nothing.
    public class ContactAnimator : MonoBehaviour, AvatarBridge.IGeneratedContactStub
    {
        public Animator animator;
        public string parameter;
    }
}
";
    }
}
#endif

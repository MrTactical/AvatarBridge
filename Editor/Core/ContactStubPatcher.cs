#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // Generates declarations of ChilloutVR's native contact components
    // so avatars can be authored with them. The system lives in the
    // game client only; the CCK ships no such types.
    //
    // The only authority for the serialized surface is the shipped
    // client, decompiled. The author's public repo is a diverged
    // work-in-progress that drops fields the client still reads;
    // never import it into a conversion project. The repo contributes
    // its MIT-licensed inspector, adapted below.
    //
    // An asset bundle carries no script assemblies; it records
    // assembly, namespace and class per MonoBehaviour, resolved
    // against the player's own assemblies. Matching declarations are
    // enough for the client's implementation to pick the data up.
    //
    // One MonoBehaviour per file, each file named after its class.
    // Unity only associates a MonoBehaviour with a script asset when
    // the file name matches the class name; merged files compile and
    // AddComponent works, but no MonoScript exists and the avatar
    // arrives with missing script references. Never merge them.
    //
    // Only the serialized surface is reproduced, plus editor gizmos.
    // Generated into the project rather than shipped: if the CCK ever
    // ships these types, a duplicate definition would break the whole
    // project, including the code able to undo it.
    [InitializeOnLoad]
    public static class ContactStubPatcher
    {
        const string StubTypeName = "NAK.Contacts.ContactBase";
        const string MarkerInterface = "AvatarBridge.IGeneratedContactStub";

        const string StubVersion = "7";
        const string VersionTag = "// AvatarBridge generated contact declaration, revision " + StubVersion;

        const string VerifiedCckVersion = "4.0.2";

        const string LegacySingleFile = "AvatarBridgeContactStub.cs";

        // One generated file, with a GUID pinned by hand.
        //
        // Unity identifies a script asset by the GUID in its .meta and every component stores
        // that GUID. Letting Unity mint one means the file is a different asset after every
        // regeneration or reinstall, orphaning components created before. Never change these
        // values: doing so breaks every avatar already converted against them.
        readonly struct StubFile
        {
            public readonly string Name;
            public readonly string Guid;
            public readonly string Source;
            public StubFile(string name, string guid, string source) { Name = name; Guid = guid; Source = source; }
        }

        static IEnumerable<StubFile> Files()
        {
            yield return new StubFile("NakContactTypes.cs", "a7f1c93e26b04d8fb0e5c1742a6d3f80", SharedTypesSource);
            yield return new StubFile("ContactBase.cs", "b41e7d2905fa4c1d9e3760a8c5271be4", ContactBaseSource);
            yield return new StubFile("ContactSender.cs", "c9026b3fd7184e51a8f4d2306e5b19ac", ContactSenderSource);
            yield return new StubFile("ContactReceiver.cs", "d5837ae1c06b4f2eb91c48d7350fa6e2", ContactReceiverSource);
            yield return new StubFile("ContactAnimator.cs", "e6194cf28b3d47a0ac52e7169b840d3f", ContactAnimatorSource);
            yield return new StubFile("NakContactStubEditor.cs", "f7a2b5d861c94e33a90d5f8e12c47ab6", ContactEditorSource);
        }

        static ContactStubPatcher()
        {
            EditorApplication.delayCall += Sync;
        }

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
                // Matched by interface name rather than a typeof() reference: this class must stay
                // compilable in the moments when the generated files do not exist.
                if (!type.GetInterfaces().Any(i => i.FullName == MarkerInterface))
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

            if (RealTypesPresent(out var real))
            {
                if (RemoveAll(dir))
                {
                    Debug.Log("[AvatarBridge] ChilloutVR now provides " + StubTypeName + " itself (" +
                              real.Assembly.GetName().Name + "); removed the generated declarations.");
                }
                return;
            }

            // A newer CCK may have changed the contact components.
            // Guessing serializes against an unchecked layout and
            // fails silently. Refuse; conversion falls back to the
            // legacy path by itself.
            string cck = InstalledCckVersion();
            if (cck != null && CompareVersions(cck, VerifiedCckVersion) > 0)
            {
                if (RemoveAll(dir))
                {
                    Debug.LogWarning("[AvatarBridge] CCK " + cck + " is newer than the " + VerifiedCckVersion +
                                     " these contact declarations were verified against. Removed them; contacts " +
                                     "convert through the pointer/trigger path. If the CCK's contacts are " +
                                     "unchanged, raise VerifiedCckVersion in " + nameof(ContactStubPatcher) + ".");
                }
                return;
            }

            // Revisions 1-3 put every class in one file, which is why nothing Unity generated from
            // them could ever bind. Clear it out before writing the split version.
            string legacy = dir + "/" + LegacySingleFile;
            if (File.Exists(legacy))
            {
                AssetDatabase.DeleteAsset(legacy);
                Debug.LogWarning("[AvatarBridge] Removed " + legacy + ", which declared several MonoBehaviours " +
                                 "in one file. Unity only ties a MonoBehaviour to a script asset when the file " +
                                 "is named after the class, so components made against it had no usable script " +
                                 "reference. Any avatar converted with native contacts on needs converting again.");
            }

            int written = 0;
            foreach (var file in Files())
            {
                string path = dir + "/" + file.Name;
                string existing = File.Exists(path) ? File.ReadAllText(path) : null;
                if (existing != null && existing.Contains(VersionTag))
                {
                    continue;
                }
                Directory.CreateDirectory(dir);

                // Order matters. Writing the .cs first lets Unity
                // import it under its own GUID before the pinned .meta
                // lands; overwriting the .meta then changes the
                // identity of an imported asset, and live components
                // get a MonoScript with no path and no text.
                //
                // So an existing asset is removed outright rather than rewritten, and the .meta is
                // written before the .cs, so the pair is complete the first time Unity sees it.
                if (File.Exists(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }
                WritePinnedMeta(path, file.Guid);
                File.WriteAllText(path, file.Source);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                written++;
            }
            if (written > 0)
            {
                Debug.Log("[AvatarBridge] Wrote " + written + " declaration(s) to " + dir + " so ChilloutVR's " +
                          "native contact components can be authored. Delete them if the CCK ever ships them.");
            }
        }

        static bool RemoveAll(string dir)
        {
            bool any = false;
            foreach (var name in Files().Select(f => f.Name).Concat(new[] { LegacySingleFile }))
            {
                string path = dir + "/" + name;
                if (File.Exists(path))
                {
                    AssetDatabase.DeleteAsset(path);
                    any = true;
                }
            }
            return any;
        }

        static void WritePinnedMeta(string assetPath, string guid)
        {
            string metaPath = assetPath + ".meta";
            File.WriteAllText(metaPath,
                "fileFormatVersion: 2\n" +
                "guid: " + guid + "\n" +
                "MonoImporter:\n" +
                "  externalObjects: {}\n" +
                "  serializedVersion: 2\n" +
                "  defaultReferences: []\n" +
                "  executionOrder: 0\n" +
                "  icon: {instanceID: 0}\n" +
                "  userData:\n" +
                "  assetBundleName:\n" +
                "  assetBundleVariant:\n");
        }

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
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript ContactStubPatcher"))
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!scriptPath.EndsWith("/ContactStubPatcher.cs", StringComparison.Ordinal))
                {
                    continue;
                }
                var dir = Path.GetDirectoryName(scriptPath)?.Replace('\\', '/');
                int editor = dir?.LastIndexOf("/Editor", StringComparison.Ordinal) ?? -1;
                if (editor > 0)
                {
                    return dir.Substring(0, editor) + "/Runtime";
                }
            }
            return null;
        }

        const string Header = VersionTag + @"
//
// DO NOT EDIT and DO NOT MERGE THESE FILES TOGETHER. Written by AvatarBridge's
// ContactStubPatcher; safe to delete, regenerated on the next domain reload, and removed
// automatically if a future CCK provides these types itself.
//
// Declares one of ChilloutVR's native contact components so avatars can be authored against it.
// The game client holds the real implementation. An asset bundle records a MonoScript naming the
// assembly, namespace and class, and the player resolves it against its own assemblies, so those
// three and the serialized field layout are what must match.
//
// Each MonoBehaviour must stay in its own file named after the class: Unity only associates a
// MonoBehaviour with a script asset on that basis, and without it the component has no usable
// script reference at all.
//
// Field names, types, defaults and order are copied from the system author's own source:
// https://github.com/NotAKidoS/Misc-Unity-Stuffs/tree/main/NAK.Contacts
// Importing that repository's real NAK.Contacts folder replaces these automatically.
";

        const string SharedTypesSource = Header + @"
using System;

namespace AvatarBridge
{
    // Marks these declarations as AvatarBridge's, so the patcher recognises its own work
    // and stands down if ChilloutVR ever ships the real types.
    public interface IGeneratedContactStub { }
}

namespace NAK.Contacts
{
    public enum ShapeType : byte { Sphere = 0, Capsule = 1, Box = 2 }

    public enum ReceiverType : byte
    {
        Constant = 0,
        OnEnter = 1,
        ProximitySenderToReceiver = 2,
        ProximityReceiverToSender = 3,
        ProximityCenterToCenter = 4,
        CopyValueFromSender = 5,
        VelocityReceiver = 6,
        VelocitySender = 7,
        VelocityMagnitude = 8
    }

    [Flags]
    public enum ContentType : byte { World = 1, Avatar = 2, Prop = 4, Player = 8 }
}
";

        const string ContactBaseSource = Header + @"
using System;
using UnityEngine;

namespace NAK.Contacts
{
    [DefaultExecutionOrder(18200)]
    public abstract class ContactBase : MonoBehaviour, AvatarBridge.IGeneratedContactStub
    {
        // Field list read off the decompiled shipped client: every
        // field, in order, defaults included. The public repo drops
        // some of these; the client wins.
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
        // Empty on purpose, and load-bearing. Unity only draws the
        // enabled checkbox when an enable-able message exists, and
        // the checkbox is honest: the client's ContactBase registers
        // in OnEnable and de-registers in OnDisable.
        private void OnEnable() { }

        // Editor-only, and the other exception to these files having no behaviour. The client
        // draws these in game from its own copy; without something here a contact is invisible in
        // the scene view and there is no way to see or size the volume just authored.
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
}
";

        const string ContactSenderSource = Header + @"
namespace NAK.Contacts
{
    public class ContactSender : ContactBase { }
}
";

        const string ContactReceiverSource = Header + @"
namespace NAK.Contacts
{
    public class ContactReceiver : ContactBase
    {
        public ReceiverType receiverType;
    }
}
";

        const string ContactAnimatorSource = Header + @"
using UnityEngine;

namespace NAK.Contacts
{
    // Bridges a ContactReceiver to an animator parameter. The client's version subscribes to the
    // receiver's contact events and writes the collision's value onto the animator, which is why
    // a receiver alone drives nothing.
    public class ContactAnimator : MonoBehaviour, AvatarBridge.IGeneratedContactStub
    {
        public Animator animator;
        public string parameter;
    }
}
";

        const string ContactEditorSource = Header + @"
// Inspector adapted from the system author's own MIT-licensed editor:
// https://github.com/NotAKidoS/Misc-Unity-Stuffs/tree/main/NAK.Contacts (c) 2026 NotAKidoS.
// Lives inside the generated set, in Assembly-CSharp behind UNITY_EDITOR, so it can only ever
// compile when the stub types it draws exist. Same lifecycle, no cross-assembly window.
// Deliberately NOT in the NAK.Contacts namespace: if the real sources (and their own editor)
// ever land in this project, class names must not collide while both briefly exist.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using NAK.Contacts;

namespace AvatarBridge
{
    [CustomEditor(typeof(ContactBase), true)]
    public class NakContactStubEditor : Editor
    {
        static bool foldShape = true;
        static bool foldFiltering = true;
        static bool foldRole = true;
        static bool foldGizmos;

        SerializedProperty Prop(string name) => serializedObject.FindProperty(name);

        public override void OnInspectorGUI()
        {
            if (target == null) return;
            serializedObject.Update();
            var contact = (ContactBase)target;
            bool isReceiver = contact is ContactReceiver;

            foldShape = EditorGUILayout.Foldout(foldShape, ""Shape"", true, EditorStyles.foldoutHeader);
            if (foldShape)
            {
                EditorGUI.indentLevel++;
                var shape = Prop(""shapeType"");
                EditorGUILayout.PropertyField(shape);
                EditorGUILayout.PropertyField(Prop(""localPosition""));
                EditorGUILayout.PropertyField(Prop(""localRotation""));
                var shapeValue = (ShapeType)shape.enumValueIndex;
                if (shapeValue == ShapeType.Box)
                {
                    EditorGUILayout.PropertyField(Prop(""boxSize""));
                }
                else
                {
                    EditorGUILayout.PropertyField(Prop(""radius""));
                    if (shapeValue == ShapeType.Capsule)
                        EditorGUILayout.PropertyField(Prop(""height""));
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            foldFiltering = EditorGUILayout.Foldout(foldFiltering, ""Filtering"", true, EditorStyles.foldoutHeader);
            if (foldFiltering)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(Prop(""allowSelf""));
                EditorGUILayout.PropertyField(Prop(""allowOthers""));
                EditorGUILayout.PropertyField(Prop(""localOnly""));
                if (isReceiver)
                {
                    EditorGUILayout.PropertyField(Prop(""contentTypes""));
                    if ((Prop(""contentTypes"").intValue & (int)ContentType.Player) == 0)
                        EditorGUILayout.HelpBox(
                            ""Player is not in Content Types — other players' hands and fingers "" +
                            ""are Player-type senders, so they will NOT trigger this receiver."",
                            MessageType.Warning);
                }
                EditorGUILayout.PropertyField(Prop(""collisionTags""), true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            foldRole = EditorGUILayout.Foldout(foldRole, isReceiver ? ""Receiver"" : ""Sender"", true, EditorStyles.foldoutHeader);
            if (foldRole)
            {
                EditorGUI.indentLevel++;
                var value = Prop(""contactValue"");
                if (isReceiver)
                {
                    var typeProp = Prop(""receiverType"");
                    EditorGUILayout.PropertyField(typeProp);
                    switch ((ReceiverType)typeProp.enumValueIndex)
                    {
                        case ReceiverType.Constant:
                            EditorGUILayout.PropertyField(value, new GUIContent(""Value""));
                            EditorGUILayout.HelpBox(""Returns this value while there is any contact."", MessageType.Info);
                            break;
                        case ReceiverType.OnEnter:
                            EditorGUILayout.PropertyField(value, new GUIContent(""Min Velocity""));
                            EditorGUILayout.HelpBox(""Returns 1 for one frame if the initial contact velocity is above the set min velocity."", MessageType.Info);
                            break;
                        case ReceiverType.CopyValueFromSender:
                            EditorGUILayout.PropertyField(value, new GUIContent(""Min Velocity""));
                            EditorGUILayout.HelpBox(""Returns the Sender value if the contact velocity is above the set min velocity."", MessageType.Info);
                            break;
                        case ReceiverType.ProximitySenderToReceiver:
                            EditorGUILayout.HelpBox(""Returns 0 to 1 measured from the Receiver's center to the Sender's surface."", MessageType.Info);
                            break;
                        case ReceiverType.ProximityReceiverToSender:
                            EditorGUILayout.HelpBox(""Returns 0 to 1 measured from the Receiver's surface to the Sender's center."", MessageType.Info);
                            break;
                        case ReceiverType.ProximityCenterToCenter:
                            EditorGUILayout.HelpBox(""Returns 0 to 1 measured from the Receiver's center to the Sender's center."", MessageType.Info);
                            break;
                        case ReceiverType.VelocityReceiver:
                            EditorGUILayout.HelpBox(""Returns the velocity of the Receiver while there is any contact."", MessageType.Info);
                            break;
                        case ReceiverType.VelocitySender:
                            EditorGUILayout.HelpBox(""Returns the velocity of the fastest Sender making contact."", MessageType.Info);
                            break;
                        case ReceiverType.VelocityMagnitude:
                            EditorGUILayout.HelpBox(""Returns the combined velocity of the Receiver and fastest Sender making contact."", MessageType.Info);
                            break;
                    }
                }
                else
                {
                    EditorGUILayout.PropertyField(value);
                    EditorGUILayout.HelpBox(""The value for a Receiver to copy if configured as CopyValueFromSender. If unsure, leave as 1."", MessageType.Info);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            foldGizmos = EditorGUILayout.Foldout(foldGizmos, ""Gizmos"", true, EditorStyles.foldoutHeader);
            if (foldGizmos)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(Prop(""drawGizmos""));
                EditorGUILayout.PropertyField(Prop(""gizmoColor""));
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
";
    }
}
#endif

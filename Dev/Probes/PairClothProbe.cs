#if AVATARBRIDGE_YAPS && AVATARBRIDGE_MAGICA
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Which cloth the live tissue takes for a pair's own: one rooted on each sided chain, one rooted
    // on an unsided part bone above both (Boob_Root), and not one elsewhere (hair on the head).
    //   -executeMethod AvatarBridge.Regression.PairClothProbe.Run -pairOut <file>
    public static class PairClothProbe
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var lines = new List<string>();
            int bad = 0;
            var root = new GameObject("Avatar").transform;
            try
            {
                Transform Child(Transform parent, string name) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
                var chest = Child(root, "Chest");
                var head = Child(chest, "Head");
                var mid = Child(chest, "Boob_Root");
                foreach (var side in new[] { "L", "R" }) Child(Child(mid, "Boob_" + side), $"Boob_{side}.001");
                var sided = Child(chest, "Breast.L");
                Child(sided, "Breast.L.001");
                void Cloth(string name, params Transform[] roots)
                {
                    var host = Child(root, name).gameObject;
                    var cloth = host.AddComponent<MagicaCloth2.MagicaCloth>();
                    var so = new SerializedObject(cloth);
                    var list = so.FindProperty("serializeData.rootBones");
                    list.arraySize = roots.Length;
                    for (int i = 0; i < roots.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = roots[i];
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                Cloth("Cloth on Boob_Root", mid);
                Cloth("Cloth on Breast.L", sided);
                Cloth("Cloth on Head", head);
                var found = YapsJelly.PairCloth(root, YapsJelly.Breast);
                foreach (var (name, want) in new[] { ("Cloth on Boob_Root", true), ("Cloth on Breast.L", true), ("Cloth on Head", false) })
                {
                    bool got = found.Exists(b => b.name == name);
                    if (got != want) bad++;
                    lines.Add($"{name}: {(got ? "taken" : "left")}{(got != want ? " WRONG" : "")}");
                }
            }
            catch (Exception e) { lines.Add("EXCEPTION " + e); bad++; }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                lines.Add(bad == 0 ? "[PairCloth] PASS" : $"[PairCloth] FAIL ({bad})");
                File.WriteAllLines(args[Array.IndexOf(args, "-pairOut") + 1], lines);
                Debug.Log(string.Join("\n", lines));
                EditorApplication.Exit(0);
            }
        }
    }
}
#endif

using System;
using System.IO;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// FBT Mayfair > Add Curtain Opener To Selected Curtain.
/// Select your curtain in the scene and click it. Adds a separate "Curtain Opener" object next to the curtain
/// (your curtain itself is not changed), with the opening/closing sounds.
/// </summary>
public static class CurtainOpenerSetup
{
    private const string Gen = "Assets/FBTCurtain/Generated";
    private const string ShapeName = "Key 1";
    private static readonly System.Random rng = new System.Random(3);

    [MenuItem("FBT Mayfair/Add Curtain Opener To Selected Curtain", priority = 20)]
    public static void Setup()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorUtility.DisplayDialog("FBT Mayfair", "Unity is still compiling or importing. Wait until it finishes, then try again.", "OK");
            return;
        }
        GameObject sel = Selection.activeGameObject;
        SkinnedMeshRenderer smr = sel != null ? sel.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        if (smr == null || smr.sharedMesh == null || smr.sharedMesh.blendShapeCount == 0)
        {
            EditorUtility.DisplayDialog("FBT Mayfair", "Select your curtain in the Hierarchy first (the object with the Skinned Mesh Renderer that has \"Key 1\"), then click this again.", "OK");
            return;
        }
        try
        {
            EnsureProgram();
            int index = smr.sharedMesh.GetBlendShapeIndex(ShapeName);
            if (index < 0) index = 0;

            var go = new GameObject("Curtain Opener (" + smr.gameObject.name + ")");
            Undo.RegisterCreatedObjectUndo(go, "Add Curtain Opener");
            go.transform.SetParent(smr.transform.parent, false);
            go.transform.position = smr.bounds.center;

            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.volume = 0.8f;
            src.spatialBlend = 1f;
            src.minDistance = 1f;
            src.maxDistance = 12f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.dopplerLevel = 0f;

            var opener = UdonSharpUndo.AddComponent<CurtainOpener>(go);
            if (opener == null) throw new Exception("UdonSharp could not add CurtainOpener. Wait until Unity has finished compiling, then try again.");
            opener.Curtain = smr;
            opener.BlendShapeIndex = index;
            opener.ClosedWeight = 0f;
            opener.OpenWeight = 50f;
            opener.OpenDistance = 3f;
            opener.OpenSeconds = 2.5f;
            opener.Sound = src;
            opener.OpenClip = CurtainSound("curtain_open", 2.4f, 1f);
            opener.CloseClip = CurtainSound("curtain_close", 2.4f, 0.85f);
            Wire(opener);

            // Start closed in the editor too (in game the script does this itself)
            Undo.RecordObject(smr, "Close curtain");
            smr.SetBlendShapeWeight(index, 0f);

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
            EditorUtility.DisplayDialog("FBT Mayfair", "Curtain opener added for '" + smr.gameObject.name + "' (blend shape \"" + smr.sharedMesh.GetBlendShapeName(index) + "\").\n\n" +
                "It opens to 50 when someone is within 3 m, and closes again when everyone leaves. Change Open Distance / Open Seconds on the new object.\n\nSave the scene (Ctrl+S), then press Play.", "OK");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("FBT Mayfair - failed", e.Message + "\n\nPlease screenshot this and send it.", "OK");
        }
    }

    private static void EnsureProgram()
    {
        foreach (string g in AssetDatabase.FindAssets("t:UdonSharpProgramAsset"))
        {
            var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetDatabase.GUIDToAssetPath(g));
            if (pa != null && pa.sourceCsScript != null && pa.sourceCsScript.GetClass() == typeof(CurtainOpener)) return;
        }
        MonoScript script = null;
        foreach (string g in AssetDatabase.FindAssets("CurtainOpener t:MonoScript"))
        {
            var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(g));
            if (ms != null && ms.GetClass() == typeof(CurtainOpener)) { script = ms; break; }
        }
        if (script == null) throw new Exception("Could not find CurtainOpener.cs in the project.");
        var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        asset.sourceCsScript = script;
        Dir(Gen);
        AssetDatabase.CreateAsset(asset, Gen + "/CurtainOpener.asset");
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
    }

    // UdonSharp may still be compiling a freshly imported script: then finish this step by itself a moment later
    private static CurtainOpener pending;
    private static int tries;

    private static void Wire(CurtainOpener o)
    {
        try { UdonSharpEditorUtility.CopyProxyToUdon(o); }
        catch (Exception e)
        {
            if (e.Message.IndexOf("outdated", StringComparison.OrdinalIgnoreCase) < 0 && e.Message.IndexOf("compile", StringComparison.OrdinalIgnoreCase) < 0) throw;
            pending = o;
            tries = 0;
            EditorApplication.update -= Retry;
            EditorApplication.update += Retry;
        }
    }

    private static void Retry()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || ++tries % 30 != 0) return;
        try
        {
            if (pending != null) UdonSharpEditorUtility.CopyProxyToUdon(pending);
            EditorApplication.update -= Retry;
            if (pending != null) EditorSceneManager.MarkSceneDirty(pending.gameObject.scene);
            Debug.Log("[FBT Mayfair] Curtain opener ready. Save your scene (Ctrl+S).");
        }
        catch (Exception e)
        {
            if (tries < 3600) return;
            EditorApplication.update -= Retry;
            Debug.LogWarning("[FBT Mayfair] Could not finish the curtain opener setup (" + e.Message + "). Click the menu again once Unity is idle.");
        }
    }

    // ------------------------------------------------------------------ sound: fabric swish + curtain rings sliding on the rail

    private static AudioClip CurtainSound(string name, float dur, float pitch)
    {
        Dir(Gen);
        string path = Gen + "/" + name + ".wav";
        const int sr = 44100;
        int count = (int)(sr * dur);
        var s = new float[count];
        float lp1 = 0f, lp2 = 0f, ring = 0f, ringHz = 0f, ringT = 1f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)sr;
            float n = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp1 += 0.05f * pitch * (n - lp1);
            lp2 += 0.35f * pitch * (n - lp2);
            float fabric = (lp2 - lp1);
            // swell: quick start, long soft tail, with a slight flutter of the fabric
            float env = Mathf.Clamp01(t / 0.25f) * Mathf.Exp(-Mathf.Max(0f, t - 0.6f) / 0.7f) * (0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 5.5f * t));
            // metal rings clicking along the rail now and then
            if (t < dur * 0.7f && rng.NextDouble() < 0.0009) { ringT = 0f; ringHz = (2600f + (float)rng.NextDouble() * 1800f) * pitch; }
            ringT += 1f / sr;
            ring = Mathf.Sin(2f * Mathf.PI * ringHz * ringT) * Mathf.Exp(-ringT / 0.03f) * 0.18f;
            float fade = Mathf.Clamp01((dur - t) / 0.05f);
            s[i] = (fabric * 1.6f * env + ring) * fade;
        }
        using (var bw = new BinaryWriter(File.Create(path)))
        {
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + count * 2);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(sr);
            bw.Write(sr * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(count * 2);
            foreach (float v in s) bw.Write((short)(Mathf.Clamp(v, -1f, 1f) * 30000f));
        }
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    private static void Dir(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        Dir(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}

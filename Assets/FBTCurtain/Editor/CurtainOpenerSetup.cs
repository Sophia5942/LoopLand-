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
            opener.OpenClip = FindClip("Curtain_Open");
            opener.CloseClip = FindClip("Curtain_Close");
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

    // The sounds ship with the package (FBTCurtain/Sounds): a smooth theatrical "whoosh" with a soft settle at the end.
    private static AudioClip FindClip(string name)
    {
        foreach (string g in AssetDatabase.FindAssets(name + " t:AudioClip"))
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g));
            if (c != null && c.name == name) return c;
        }
        Debug.LogWarning("[FBT Mayfair] Sound '" + name + "' not found. Make sure the FBTCurtain/Sounds folder was imported.");
        return null;
    }

    private static void Dir(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        Dir(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}

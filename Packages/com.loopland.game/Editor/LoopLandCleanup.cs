using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LoopLand.EditorTools
{
    /// <summary>
    /// LoopLand > Remove Tower World: cleans up a scene that was built with the (removed) tower world, so the game is
    /// back on the floor: deletes the tower world, puts the game and its store back, shows the demo floor and light
    /// again, moves the spawn back next to the table, restores the default sky and lighting and removes the leftover
    /// Udon programs of the tower scripts.
    /// </summary>
    public static class LoopLandCleanup
    {
        [MenuItem("LoopLand/Remove Tower World (back to the floor)", priority = 30)]
        public static void RemoveTowerWorld()
        {
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == "LoopLand World") Undo.DestroyObjectImmediate(go);
                else if ((go.name == "LoopLand Floor" || go.name == "LoopLand Light") && !go.activeSelf)
                {
                    Undo.RecordObject(go, "Show demo setup");
                    go.SetActive(true);
                }
            }

            GameObject game = GameObject.Find("LoopLand");
            if (game != null)
            {
                Undo.RecordObject(game.transform, "Move LoopLand back to the floor");
                game.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Transform store = game.transform.Find("Store");
                if (store != null)
                {
                    Undo.RecordObject(store, "Move the store back");
                    store.localPosition = new Vector3(0f, 0f, -5.4f);
                    store.localRotation = Quaternion.Euler(0f, 180f, 0f);
                }
            }

            // the tower world moved the spawn out to its front walkway; bring it back next to the table
            foreach (var d in Object.FindObjectsByType<VRC.SDK3.Components.VRCSceneDescriptor>(FindObjectsSortMode.None))
            {
                if (d.transform.position.z > -40f) continue;
                Undo.RecordObject(d.transform, "Move spawn back");
                d.transform.SetPositionAndRotation(new Vector3(0f, 0f, -3.6f), Quaternion.identity);
            }

            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            GameObject light = GameObject.Find("LoopLand Light");
            RenderSettings.sun = light != null ? light.GetComponent<Light>() : null;
            DynamicGI.UpdateEnvironment();

            foreach (string name in new[] { "LoopLandElevator", "LoopLandMover", "LoopLandLift", "LoopLandSeat", "LoopLandAmbience" })
                AssetDatabase.DeleteAsset("Assets/LoopLand/Generated/Programs/" + name + ".asset");

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[LoopLand] Tower world removed: the game is back on the floor. Save the scene to keep it.");
        }
    }
}

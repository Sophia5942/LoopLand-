using UnityEditor;

namespace DrinkSystem.EditorTools
{
    /// <summary>After importing, asks once whether to build the drink machine into the open scene.</summary>
    [InitializeOnLoad]
    public static class DrinkSystemWelcome
    {
        private const string Key = "DrinkSystem.WelcomeShown.2_1_2";

        static DrinkSystemWelcome()
        {
            if (EditorPrefs.GetBool(Key, false)) return;
            EditorApplication.delayCall += Ask;
        }

        private static void Ask()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Ask;
                return;
            }
            EditorPrefs.SetBool(Key, true);
            if (EditorUtility.DisplayDialog("FBT Mayfair Drink System",
                "The drink system is imported.\n\nBuild the drink machine into the open scene now?\n\n" +
                "Tip: select your bar counter first, so it goes on top of it.\n" +
                "You can always build it later from the top menu: Drink System > Build Drink Machine In Scene.",
                "Build it now", "Later"))
            {
                DrinkMachineBuilder.Build();
            }
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>FBT Mayfair > Build Can Fridge + Bin opens this: tick the drinks you want a fridge row for, then Build.</summary>
public class FBTDrinkPicker : EditorWindow
{
    private const string Pref = "FBTMayfair.Drink.";
    private bool[] on;
    private Vector2 scroll;

    public static void Open()
    {
        var w = GetWindow<FBTDrinkPicker>(true, "FBT Mayfair - Pick Drinks");
        w.minSize = new Vector2(420f, 560f);
    }

    private void OnEnable()
    {
        var all = CanSystemBuilder.Drinks;
        on = new bool[all.Length];
        // first time: the original six
        for (int i = 0; i < all.Length; i++) on[i] = EditorPrefs.GetBool(Pref + all[i].Key, i < 6);
    }

    private void OnGUI()
    {
        var all = CanSystemBuilder.Drinks;
        if (on == null || on.Length != all.Length) OnEnable();
        EditorGUILayout.LabelField("Which drinks go in the fridge?", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Each ticked drink gets its own row (click box + display cans + 4 cans). 6 is a good number for one fridge.", EditorStyles.wordWrappedLabel);
        GUILayout.Space(6f);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        for (int i = 0; i < all.Length; i++) on[i] = EditorGUILayout.ToggleLeft(all[i].Name + "   -   " + all[i].Style, on[i]);
        EditorGUILayout.EndScrollView();
        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("All")) for (int i = 0; i < on.Length; i++) on[i] = true;
        if (GUILayout.Button("None")) for (int i = 0; i < on.Length; i++) on[i] = false;
        if (GUILayout.Button("Original 6")) for (int i = 0; i < on.Length; i++) on[i] = i < 6;
        GUILayout.EndHorizontal();
        int count = 0;
        for (int i = 0; i < on.Length; i++) if (on[i]) count++;
        if (count > 9) EditorGUILayout.HelpBox(count + " drinks = " + (count * 8) + " cans in the world. Fine on PC; on Quest keep it to about 9.", MessageType.Warning);
        GUILayout.Space(4f);
        if (GUILayout.Button("Build " + count + " drink row" + (count == 1 ? "" : "s"), GUILayout.Height(34f)))
        {
            var picked = new List<CanSystemBuilder.Drink>();
            for (int i = 0; i < all.Length; i++)
            {
                EditorPrefs.SetBool(Pref + all[i].Key, on[i]);
                if (on[i]) picked.Add(all[i]);
            }
            Close();
            CanSystemBuilder.BuildWith(picked.ToArray());
        }
    }
}

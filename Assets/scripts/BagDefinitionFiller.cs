#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class BagDefinitionFiller
{
    // ============================================================
    // Menu items
    // ============================================================

    [MenuItem("Tools/Bags/Fill: Default (Algebra)")]
    public static void FillDefault() => Fill("Default");

    [MenuItem("Tools/Bags/Fill: Numbers Only")]
    public static void FillNumbers() => Fill("Numbers");

    [MenuItem("Tools/Bags/Fill: Operators Only")]
    public static void FillOperators() => Fill("Operators");

    [MenuItem("Tools/Bags/Fill: Easy Start")]
    public static void FillEasy() => Fill("Easy");

    [MenuItem("Tools/Bags/Fill: Advanced (functions)")]
    public static void FillAdvanced() => Fill("Advanced");

    [MenuItem("Tools/Bags/Fill: Custom (= only)")]
    public static void FillCustom() => Fill("Custom");

    [MenuItem("Tools/Bags/Fill: Scrabble (100 tiles)")]
    public static void FillScrabble() => Fill("Scrabble");

    // ============================================================

    private static void Fill(string preset)
    {
        var def = Selection.activeObject as BagDefinition;
        if (def == null)
        {
            EditorUtility.DisplayDialog("Fill Bag",
                "Select a BagDefinition asset first.", "OK");
            return;
        }

        def.entries.Clear();

        switch (preset)
        {
            case "Default":   FillAlgebra(def);    break;
            case "Numbers":   FillNumbersPreset(def); break;
            case "Operators": FillOperatorsPreset(def); break;
            case "Easy":      FillEasyPreset(def); break;
            case "Advanced":  FillAdvancedPreset(def); break;
            case "Custom":    FillCustomPreset(def); break;
            case "Scrabble":  FillScrabblePreset(def); break;
        }

        EditorUtility.SetDirty(def);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int total = 0;
        foreach (var e in def.entries) total += e.count;
        Debug.Log($"Filled '{def.name}' with {def.entries.Count} distinct entries, {total} balls total ({preset}).");
    }

    // ============================================================
    // Presets
    // ============================================================

    private static void FillAlgebra(BagDefinition def)
    {
        Add(def, "1", 3);
        Add(def, "2", 3);
        Add(def, "3", 3);
        Add(def, "4", 2);
        Add(def, "5", 2);
        Add(def, "6", 1);
        Add(def, "7", 1);
        Add(def, "8", 1);
        Add(def, "9", 1);
        Add(def, "10", 1);
        Add(def, "+", 3);
        Add(def, "-", 3);
        Add(def, "*", 2);
        Add(def, "/", 2);
        Add(def, "^", 1);
        Add(def, "=", 4);
        Add(def, "x", 2);
        Add(def, "y", 2);
        Add(def, "(", 2);
        Add(def, ")", 2);
    }

    private static void FillNumbersPreset(BagDefinition def)
    {
        for (int n = 1; n <= 20; n++) Add(def, n.ToString(), 2);
        Add(def, "=", 4);
    }

    private static void FillOperatorsPreset(BagDefinition def)
    {
        Add(def, "+", 6);
        Add(def, "-", 6);
        Add(def, "*", 6);
        Add(def, "/", 6);
        Add(def, "^", 2);
        Add(def, "=", 4);
    }

    private static void FillEasyPreset(BagDefinition def)
    {
        Add(def, "1", 4);
        Add(def, "2", 4);
        Add(def, "3", 4);
        Add(def, "4", 3);
        Add(def, "5", 3);
        Add(def, "+", 5);
        Add(def, "-", 3);
        Add(def, "=", 4);
    }

    private static void FillAdvancedPreset(BagDefinition def)
    {
        FillAlgebra(def);
        Add(def, "pi", 1);
        Add(def, "e", 1);
        Add(def, "sin", 1);
        Add(def, "cos", 1);
        Add(def, "tan", 1);
        Add(def, "root", 1);
        Add(def, "log", 1);
        Add(def, "!", 1);
        Add(def, "%", 1);
        Add(def, "(", 2);
        Add(def, ")", 2);
    }

    private static void FillCustomPreset(BagDefinition def)
    {
        Add(def, "1", 8); //
        Add(def, "2", 6); //
        Add(def, "3", 5); //
        Add(def, "4", 4); //
        Add(def, "5", 3); 
        Add(def, "6", 3); 
        Add(def, "7", 1); 
        Add(def, "8", 2);  
        Add(def, "9", 1); 
        Add(def, "10", 1); 

         Add(def, "-1", 2); //
        Add(def, "-2", 1); //
        Add(def, "-3", 1); //
        Add(def, "-4", 0); //
        Add(def, "-5", 0); 
        Add(def, "-6", 1); 
        Add(def, "-7", 0); 
        Add(def, "-8", 0);  
        Add(def, "-9", 0); 
        Add(def, "-10", 0); 

        Add(def, "+", 8); //
        Add(def, "-", 6); //
        Add(def, "*", 2); //
        Add(def, "/", 4); 
        Add(def, "/2", 2); 
        Add(def, "/4", 0); 
        Add(def, "/5", 0); 
        Add(def, "/6", 0);

        Add(def, "^", 2);
        Add(def, "^2", 1);
        Add(def, "^3", 1);
        Add(def, "^4", 0);


        Add(def, "=", 12); //
        Add(def, "x", 0); 
        Add(def, "y", 0); 
      

    }

    private static void FillScrabblePreset(BagDefinition def)
    {
        // Standard Scrabble tile distribution (100 tiles total).
        // A-9, B-2, C-2, D-4, E-12, F-2, G-3, H-2, I-9, J-1, K-1, L-4,
        // M-2, N-6, O-8, P-2, Q-1, R-6, S-4, T-6, U-4, V-2, W-2, X-1,
        // Y-2, Z-1, blank-2.
        Add(def, "a", 9);
        Add(def, "b", 2);
        Add(def, "c", 2);
        Add(def, "d", 4);
        Add(def, "e", 12);
        Add(def, "f", 2);
        Add(def, "g", 3);
        Add(def, "h", 2);
        Add(def, "i", 9);
        Add(def, "j", 1);
        Add(def, "k", 1);
        Add(def, "l", 4);
        Add(def, "m", 2);
        Add(def, "n", 6);
        Add(def, "o", 8);
        Add(def, "p", 2);
        Add(def, "q", 1);
        Add(def, "r", 6);
        Add(def, "s", 4);
        Add(def, "t", 6);
        Add(def, "u", 4);
        Add(def, "v", 2);
        Add(def, "w", 2);
        Add(def, "x", 1);
        Add(def, "y", 2);
        Add(def, "z", 1);
        Add(def, "_", 2);   // blanks
    }

    // ============================================================

    private static void Add(BagDefinition def, string content, int count)
    {
        def.entries.Add(new BagDefinition.Entry { content = content, count = count });
    }
}
#endif
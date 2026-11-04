using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Bag/Bag Library", fileName = "BagLibrary")]
public class BagLibrary : ScriptableObject
{
    [System.Serializable]
    public class NamedBag
    {
        public string displayName = "New Bag";
        public BagDefinition definition;
    }

    public List<NamedBag> bags = new List<NamedBag>();

    public int Count => bags.Count;

    public BagDefinition Get(int index)
    {
        if (index < 0 || index >= bags.Count) return null;
        return bags[index]?.definition;
    }

    public string GetName(int index)
    {
        if (index < 0 || index >= bags.Count) return $"Bag {index}";
        string n = bags[index]?.displayName;
        return string.IsNullOrEmpty(n) ? $"Bag {index}" : n;
    }

    public int IndexOf(BagDefinition def)
    {
        for (int i = 0; i < bags.Count; i++)
            if (bags[i]?.definition == def) return i;
        return -1;
    }
}
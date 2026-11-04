using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Configurable pool of bag balls. Create via Assets → Create → Bag → Bag Definition.
/// Fill in content + count entries; the Bag Manager uses these as its starting pool.
/// </summary>
[CreateAssetMenu(menuName = "Bag/Bag Definition", fileName = "DefaultBag")]
public class BagDefinition : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        [Tooltip("Text displayed inside the ball, e.g. '3', '+', 'x'.")]
        public string content;

        [Min(0)]
        [Tooltip("How many copies of this ball start in the bag.")]
        public int count = 1;
    }

    public List<Entry> entries = new List<Entry>();
}
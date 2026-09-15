using UnityEngine;
using System.Collections.Generic;

public class MaterialConversionHistory : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string globalId;
        public int slotIndex;
        public Material convertedMaterial;
        public Material originalMaterial;
        public string objectNameHint;
    }

    public List<Entry> entries = new List<Entry>();
}

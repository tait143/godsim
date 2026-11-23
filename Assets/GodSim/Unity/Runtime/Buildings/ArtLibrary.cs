using System;
using System.Collections.Generic;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>Resolves exact reviewed building bindings. An unavailable binding or model leaves the procedural renderer in control.</summary>
    public sealed class ArtLibrary
    {
        public const string IndexResource = "GodSim_BuildingIndex";
        public const string ModelDirectory = "GodSimBuildingModels/";
        // glTFast has already converted glTF V to Unity's 1-V during import.
        internal const float AgeRowHeight = 96f / 1024f;
        private readonly Dictionary<(BuildingType, RaceId, AgeId), Entry[]> _bindings = new Dictionary<(BuildingType, RaceId, AgeId), Entry[]>();
        private readonly Func<string, BuildingArtModel> _loadModel;
        public Material Material { get; }

#pragma warning disable 0649 // JsonUtility populates the compact projection of the reviewed manifest.
        [Serializable] internal sealed class Index { public string schema; public Entry[] assets; public Binding[] bindings; }
        [Serializable] internal sealed class Binding { public string buildingType, race, age; public string[] modelIds; }
        [Serializable] internal sealed class Entry
        {
            public string id;
            public int baseAgeIndex;
            [NonSerialized] internal bool Loaded;
            [NonSerialized] internal BuildingArtModel Model;
        }
#pragma warning restore 0649

        public static ArtLibrary Load()
        {
            var text = Resources.Load<TextAsset>(IndexResource);
            Index index = null;
            if (text != null)
            {
                try { index = JsonUtility.FromJson<Index>(text.text); }
                catch (ArgumentException error) { Debug.LogWarning("Invalid building art index: " + error.Message); }
            }
            return new ArtLibrary(index, Resources.Load<Material>("GodSim_Buildings"), Resources.Load<BuildingArtModel>);
        }

        internal ArtLibrary(Index index, Material material, Func<string, BuildingArtModel> loadModel)
        {
            Material = material;
            _loadModel = loadModel;
            if (index?.schema != "godsim.buildings/2" || index.assets == null || index.bindings == null) return;
            var byId = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var entry in index.assets)
                if (entry != null && !string.IsNullOrEmpty(entry.id) && entry.baseAgeIndex >= 0 && entry.baseAgeIndex <= (int)AgeId.Ascension)
                    byId[entry.id] = entry;
            foreach (var binding in index.bindings)
            {
                if (binding?.modelIds == null || binding.modelIds.Length == 0 ||
                    !Enum.TryParse(binding.buildingType, true, out BuildingType type) ||
                    !Enum.TryParse(binding.race, true, out RaceId race) || !Enum.TryParse(binding.age, true, out AgeId age)) continue;
                var variants = new Entry[binding.modelIds.Length];
                for (int i = 0; i < variants.Length; i++)
                    if (binding.modelIds[i] != null) byId.TryGetValue(binding.modelIds[i], out variants[i]);
                _bindings[(type, race, age)] = variants;
            }
        }

        public bool TryGet(BuildingType type, RaceId race, AgeId age, int variant, out BuildingArtModel model, out float ageRowOffset)
        {
            model = null;
            ageRowOffset = 0f;
            if (Material == null || !_bindings.TryGetValue((type, race, age), out var variants)) return false;
            int slot = variant % variants.Length;
            var entry = variants[slot < 0 ? slot + variants.Length : slot];
            if (entry == null) return false;
            if (!entry.Loaded)
            {
                entry.Model = _loadModel(ModelDirectory + entry.id);
                entry.Loaded = true;
            }
            var loaded = entry.Model;
            if (loaded == null || loaded.Lod0 == null || loaded.Lod1 == null || loaded.Lod2 == null) return false;
            model = loaded;
            ageRowOffset = (entry.baseAgeIndex - (int)age) * AgeRowHeight;
            return true;
        }
    }
}

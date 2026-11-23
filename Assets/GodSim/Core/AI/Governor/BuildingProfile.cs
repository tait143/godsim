namespace GodSim.Core.AI
{
    /// <summary>
    /// What a building type can contribute, derived once from its <see cref="BuildingDefinition"/> so the
    /// governor never depends on specific catalog numbers: expected housing, net resource output at full
    /// staffing, services, storage, defense (defense service + soldier slots) and gold/commerce value.
    /// Immutable after construction.
    /// </summary>
    public sealed class BuildingProfile
    {
        /// <summary>Building type.</summary>
        public readonly BuildingType Type;
        /// <summary>Source definition.</summary>
        public readonly BuildingDefinition Def;
        /// <summary>Expected residents ((HousingMin + HousingMax) / 2), 0 for non-dwellings.</summary>
        public readonly float ExpectedHousing;
        /// <summary>Outputs minus inputs per tick at full staffing, per resource (negative = net consumer).</summary>
        public readonly float[] NetOutput = new float[Counts.Resources];
        /// <summary>Inputs per tick at full staffing, per resource.</summary>
        public readonly float[] Input = new float[Counts.Resources];
        /// <summary>Service points per tick at full staffing, per service.</summary>
        public readonly float[] Service = new float[Counts.Services];
        /// <summary>Added stockpile capacity.</summary>
        public readonly float Storage;
        /// <summary>Defense value: Defense service points plus soldier job slots.</summary>
        public readonly float Defense;
        /// <summary>Gold/commerce value: net gold output plus half the Commerce service.</summary>
        public readonly float GoldValue;
        /// <summary>Soldier job slots.</summary>
        public readonly int SoldierSlots;
        /// <summary>True if the building has a net positive Food output.</summary>
        public readonly bool ProducesFood;
        /// <summary>True if the building consumes any input resource.</summary>
        public readonly bool HasInputs;
        /// <summary>True for wonders (never ordered by the governor).</summary>
        public readonly bool IsWonder;

        /// <summary>Builds the profile of a definition (null-safe for its arrays).</summary>
        public BuildingProfile(BuildingDefinition def)
        {
            Guard.NotNull(def, nameof(def));
            Def = def;
            Type = def.Type;
            IsWonder = def.IsWonder || def.Category == BuildingCategory.Wonder;
            if (def.HousingMax > 0) ExpectedHousing = (MathUtil.Clamp(def.HousingMin, 0, def.HousingMax) + def.HousingMax) * 0.5f;
            float workers = def.HasJobs ? def.JobSlots : 1f;
            if (def.OutputsPerWorker != null)
                for (int i = 0; i < def.OutputsPerWorker.Length; i++)
                {
                    var o = def.OutputsPerWorker[i];
                    int r = (int)o.Type;
                    if (r < 0 || r >= Counts.Resources) continue;
                    NetOutput[r] += Positive(o.Amount) * workers;
                }
            if (def.InputsPerWorker != null)
                for (int i = 0; i < def.InputsPerWorker.Length; i++)
                {
                    var o = def.InputsPerWorker[i];
                    int r = (int)o.Type;
                    if (r < 0 || r >= Counts.Resources) continue;
                    float amount = Positive(o.Amount) * workers;
                    if (amount <= 0f) continue;
                    Input[r] += amount;
                    NetOutput[r] -= amount;
                    HasInputs = true;
                }
            if (def.Services != null)
                for (int i = 0; i < def.Services.Length; i++)
                {
                    int sv = (int)def.Services[i].Type;
                    if (sv < 0 || sv >= Counts.Services) continue;
                    Service[sv] += Positive(def.Services[i].Amount);
                }
            Storage = Positive(def.StorageCapacity);
            SoldierSlots = def.Job == JobType.Soldier ? System.Math.Max(0, def.JobSlots) : 0;
            Defense = Service[(int)ServiceType.Defense] + SoldierSlots;
            float gold = NetOutput[(int)ResourceType.Gold];
            GoldValue = (gold > 0f ? gold : 0f) + 0.5f * Service[(int)ServiceType.Commerce];
            ProducesFood = NetOutput[(int)ResourceType.Food] > 0f;
        }

        /// <summary>Raw (un-normalized) value of this building for a need slot of <see cref="SettlementNeeds"/>.</summary>
        public float Raw(int slot)
        {
            if (slot == SettlementNeeds.Housing) return ExpectedHousing;
            if (slot == SettlementNeeds.Storage) return Storage;
            if (slot == SettlementNeeds.Defense) return Defense;
            if (slot >= SettlementNeeds.FirstResource && slot < SettlementNeeds.FirstService)
            {
                int r = slot - SettlementNeeds.FirstResource;
                if (r == (int)ResourceType.Gold) return GoldValue;
                float v = NetOutput[r];
                return v > 0f ? v : 0f;
            }
            if (slot >= SettlementNeeds.FirstService && slot < SettlementNeeds.Count)
            {
                int sv = slot - SettlementNeeds.FirstService;
                if (sv == (int)ServiceType.Defense || sv == (int)ServiceType.Commerce) return 0f; // folded into Defense / Gold
                return Service[sv];
            }
            return 0f;
        }

        /// <summary>True if the building produces a positive net amount of the resource.</summary>
        public bool Produces(ResourceType r) => NetOutput[(int)r] > 0f;

        private static float Positive(float v) { v = MathUtil.Sanitize(v); return v > 0f ? v : 0f; }
    }
}

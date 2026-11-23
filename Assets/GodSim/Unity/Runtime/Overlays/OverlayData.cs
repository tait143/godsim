using System;
using System.Collections.Generic;
using GodSim.Core;

namespace GodSim.Unity
{
    /// <summary>
    /// Engine-free computation of overlay values from <see cref="WorldState"/> (read-only; never mutates state).
    /// Can run in one call (<see cref="Compute"/>) or time-sliced over frames:
    /// <see cref="Begin"/> (per-settlement aggregates), <see cref="ComputeRows"/> (any number of row slices),
    /// then <see cref="End"/> (danger propagation, statistics, normalization range).
    /// Allocation-free after the <see cref="OverlayField"/> buffers have grown to the world size.
    /// Settlement-level metrics are splatted onto the settlement's owned tiles (<c>WorldMap.Owner</c>) with a
    /// distance falloff from the settlement center (into the value for density, into the weight otherwise).
    /// </summary>
    public static class OverlayData
    {
        /// <summary>Civilization blackboard key written by the religion system when a civ founded its faith.</summary>
        public const string ReligionFoundedKey = "rel.founded";

        /// <summary>Weight at (and beyond) the territory radius; 1 at the settlement center.</summary>
        public const float MinFalloffWeight = 0.3f;

        /// <summary>Opacity weight of interior territory tiles (borders use 1).</summary>
        public const float TerritoryInteriorWeight = 0.7f;

        /// <summary>Danger lost per tile of distance from a source (fire 1.0 reaches ~8 tiles).</summary>
        public const float DangerDecayPerTile = 0.12f;

        /// <summary>Danger below this is treated as "safe" (transparent).</summary>
        public const float DangerVisibleThreshold = 0.03f;

        /// <summary>Danger of a burning tile / building.</summary>
        public const float FireDanger = 1f;

        /// <summary>Danger of a tile on a front between two civilizations at war.</summary>
        public const float WarFrontDanger = 0.8f;

        private const float Diagonal = 1.41421356f;

        // ================================================================== mode metadata

        /// <summary>True for modes whose value is a category id (Territory, Religion).</summary>
        public static bool IsCategorical(OverlayMode mode) => mode == OverlayMode.Territory || mode == OverlayMode.Religion;

        /// <summary>True for modes derived from settlements (splatted onto owned tiles).</summary>
        public static bool IsSettlementMetric(OverlayMode mode)
        {
            switch (mode)
            {
                case OverlayMode.Territory:
                case OverlayMode.PopulationDensity:
                case OverlayMode.Happiness:
                case OverlayMode.Health:
                case OverlayMode.Faith:
                case OverlayMode.Wealth:
                case OverlayMode.Age:
                case OverlayMode.Religion:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when the data changes only with terrain edits (<c>WorldMap.TerrainVersion</c>).</summary>
        public static bool IsTerrainStatic(OverlayMode mode) => mode == OverlayMode.Elevation;

        /// <summary>Color ramp used for a mode.</summary>
        public static ColorRampId RampFor(OverlayMode mode)
        {
            switch (mode)
            {
                case OverlayMode.Territory:
                case OverlayMode.Religion: return ColorRampId.Categorical;
                case OverlayMode.Happiness: return ColorRampId.DivergingRedWhiteBlue;
                case OverlayMode.Temperature: return ColorRampId.DivergingBlueWhiteRed;
                case OverlayMode.Danger: return ColorRampId.Magma;
                default: return ColorRampId.Viridis;
            }
        }

        /// <summary>Human-readable legend title.</summary>
        public static string Title(OverlayMode mode)
        {
            switch (mode)
            {
                case OverlayMode.Territory: return "Territory";
                case OverlayMode.PopulationDensity: return "Population density (people / tile)";
                case OverlayMode.Happiness: return "Happiness";
                case OverlayMode.Health: return "Health";
                case OverlayMode.Faith: return "Faith";
                case OverlayMode.Fertility: return "Soil fertility";
                case OverlayMode.Resources: return "Harvestable resources";
                case OverlayMode.Elevation: return "Elevation";
                case OverlayMode.Moisture: return "Moisture";
                case OverlayMode.Temperature: return "Temperature";
                case OverlayMode.Danger: return "Danger (armies, fire, war fronts)";
                case OverlayMode.Wealth: return "Wealth (gold per settlement)";
                case OverlayMode.Age: return "Age of civilization";
                case OverlayMode.Religion: return "Religion";
                default: return "";
            }
        }

        /// <summary>Fixed normalization range of a mode; false when the range is derived from data.</summary>
        public static bool TryGetFixedRange(OverlayMode mode, out float min, out float max)
        {
            switch (mode)
            {
                case OverlayMode.Happiness:
                case OverlayMode.Health:
                case OverlayMode.Faith:
                case OverlayMode.Fertility:
                case OverlayMode.Moisture:
                case OverlayMode.Temperature:
                case OverlayMode.Danger:
                    min = 0f; max = 1f; return true;
                case OverlayMode.Elevation:
                    min = -1f; max = 1f; return true;
                case OverlayMode.Age:
                    min = 0f; max = Math.Max(1, Counts.Ages - 1); return true;
                default:
                    min = 0f; max = 1f; return false;
            }
        }

        /// <summary>Formats a raw value of a mode for display (invariant, allocation per call: cache the result).</summary>
        public static string FormatValue(OverlayMode mode, float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "-";
            switch (mode)
            {
                case OverlayMode.Happiness:
                case OverlayMode.Health:
                case OverlayMode.Faith:
                case OverlayMode.Fertility:
                case OverlayMode.Moisture:
                case OverlayMode.Temperature:
                case OverlayMode.Danger:
                    return Clamp((int)Math.Round(v * 100.0, MidpointRounding.AwayFromZero), -100000, 100000) + "%";
                case OverlayMode.Elevation:
                case OverlayMode.PopulationDensity:
                    return OneDecimal(v);
                case OverlayMode.Age:
                {
                    int a = Clamp((int)Math.Round(v, MidpointRounding.AwayFromZero), 0, Counts.Ages - 1);
                    return ((AgeId)a).ToString();
                }
                default:
                    return Clamp((int)Math.Round(Math.Max(-1e9, Math.Min(1e9, v)), MidpointRounding.AwayFromZero), -1000000000, 1000000000).ToString();
            }
        }

        /// <summary>Legend end label: a descriptive word plus the value where helpful.</summary>
        public static string RangeLabel(OverlayMode mode, float v, bool isMax)
        {
            switch (mode)
            {
                case OverlayMode.Happiness: return isMax ? "Joyful" : "Miserable";
                case OverlayMode.Temperature: return isMax ? "Hot" : "Cold";
                case OverlayMode.Moisture: return isMax ? "Wet" : "Arid";
                case OverlayMode.Danger: return isMax ? "Deadly" : "Safe";
                case OverlayMode.Elevation: return isMax ? "Peaks" : "Abyss";
                case OverlayMode.Fertility: return isMax ? "Lush" : "Barren";
                case OverlayMode.Wealth: return FormatValue(mode, v) + " gold";
                case OverlayMode.Territory:
                case OverlayMode.Religion: return "";
                default: return FormatValue(mode, v);
            }
        }

        // ================================================================== computation

        /// <summary>Computes the whole field in one call. Null-safe: a null state/map yields an empty, complete field.</summary>
        public static void Compute(WorldState state, OverlayMode mode, OverlayField field)
        {
            if (field == null) return;
            if (!Begin(state, mode, field)) return;
            ComputeRows(state, field, 0, field.Height);
            End(state, field);
        }

        /// <summary>
        /// Starts a computation: sizes the field to the map and gathers per-settlement aggregates.
        /// Returns false (and marks the field complete and empty) when there is no state or map.
        /// </summary>
        public static bool Begin(WorldState state, OverlayMode mode, OverlayField field)
        {
            if (field == null) return false;
            field.Mode = mode;
            field.IsComplete = false;
            field.ValidCount = 0;
            field.DataMin = float.NaN;
            field.DataMax = float.NaN;
            field.SettlementIdLimit = 0;
            field.WarCount = 0;
            field.ResetDangerBounds();
            var map = state != null ? state.Map : null;
            if (map == null)
            {
                field.Resize(0, 0);
                field.IsComplete = true;
                return false;
            }
            field.Resize(map.Width, map.Height);
            if (IsSettlementMetric(mode) || mode == OverlayMode.Danger) GatherSettlements(state, mode, field);
            if (mode == OverlayMode.Danger) GatherWars(state, field);
            if (mode == OverlayMode.PopulationDensity) AccumulateWeightSums(map, field);
            return true;
        }

        /// <summary>Computes rows [y0, y1). Safe to call with out-of-range rows or a mismatched state (no-op).</summary>
        public static void ComputeRows(WorldState state, OverlayField field, int y0, int y1)
        {
            if (field == null || state == null || state.Map == null) return;
            var map = state.Map;
            if (map.Width != field.Width || map.Height != field.Height) return;
            if (y0 < 0) y0 = 0;
            if (y1 > field.Height) y1 = field.Height;
            if (y0 >= y1) return;
            switch (field.Mode)
            {
                case OverlayMode.Territory: RowsTerritory(map, field, y0, y1); break;
                case OverlayMode.PopulationDensity:
                case OverlayMode.Happiness:
                case OverlayMode.Health:
                case OverlayMode.Faith:
                case OverlayMode.Wealth:
                case OverlayMode.Age:
                case OverlayMode.Religion: RowsSettlement(map, field, y0, y1); break;
                case OverlayMode.Fertility: RowsTile(map, map.Fertility, true, field, y0, y1); break;
                case OverlayMode.Moisture: RowsTile(map, map.Moisture, true, field, y0, y1); break;
                case OverlayMode.Temperature: RowsTile(map, map.Temperature, false, field, y0, y1); break;
                case OverlayMode.Elevation: RowsTile(map, map.Elevation, false, field, y0, y1); break;
                case OverlayMode.Resources: RowsResources(map, field, y0, y1); break;
                case OverlayMode.Danger: RowsDanger(map, field, y0, y1); break;
                default: RowsEmpty(field, y0, y1); break;
            }
        }

        /// <summary>Finishes a computation: danger point sources + propagation, statistics and normalization range.</summary>
        public static void End(WorldState state, OverlayField field)
        {
            if (field == null) return;
            var map = state != null ? state.Map : null;
            bool sized = map != null && map.Width == field.Width && map.Height == field.Height;
            if (sized && field.Mode == OverlayMode.Danger) FinishDanger(state, field);

            float[] values = field.Values;
            int n = field.TileCount;
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            int valid = 0;
            for (int i = 0; i < n; i++)
            {
                float v = values[i];
                if (float.IsNaN(v)) continue;
                valid++;
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            field.ValidCount = valid;
            field.DataMin = valid > 0 ? lo : float.NaN;
            field.DataMax = valid > 0 ? hi : float.NaN;

            float min, max;
            if (!TryGetFixedRange(field.Mode, out min, out max))
            {
                min = 0f;
                float dataMax = valid > 0 ? hi : 0f;
                switch (field.Mode)
                {
                    case OverlayMode.PopulationDensity: max = Math.Max(dataMax, 0.5f); break;
                    case OverlayMode.Wealth: max = Math.Max(dataMax, 10f); break;
                    case OverlayMode.Territory:
                    case OverlayMode.Religion:
                        min = valid > 0 ? lo : 0f;
                        max = valid > 0 ? hi : 1f;
                        break;
                    default: max = dataMax > 0f ? dataMax : 1f; break;
                }
            }
            field.Min = min;
            field.Max = max;
            field.IsComplete = true;
        }

        /// <summary>Falloff weight at offset (dx, dy) from a settlement center: 1 at the center, <see cref="MinFalloffWeight"/> at/after the radius.</summary>
        public static float Falloff(int dx, int dy, float invRadius2)
        {
            float d = (dx * (float)dx + dy * (float)dy) * invRadius2;
            if (!(d < 1f)) d = 1f;
            return 1f - (1f - MinFalloffWeight) * d;
        }

        // ================================================================== gathering

        private static void GatherSettlements(WorldState state, OverlayMode mode, OverlayField f)
        {
            var items = state.Settlements.Items;
            int limit = Math.Max(1, state.Ids.NextSettlementId);
            for (int k = 0; k < items.Count; k++)
            {
                var s = items[k];
                if (s != null && s.Id >= limit && s.Id < OverlayField.MaxTrackedIds) limit = s.Id + 1;
            }
            if (limit > OverlayField.MaxTrackedIds) limit = OverlayField.MaxTrackedIds;
            f.EnsureSettlementCapacity(limit);
            Array.Clear(f.SValid, 0, limit);
            f.SettlementIdLimit = limit;

            var map = state.Map;
            for (int k = 0; k < items.Count; k++)
            {
                var s = items[k];
                if (s == null || !s.IsAlive) continue;
                int id = s.Id;
                if (id < 0 || id >= limit) continue;
                Civilization civ = s.CivilizationId >= 0 ? state.Civilizations.Get(s.CivilizationId) : null;
                int r = Math.Max(1, Math.Min(s.TerritoryRadius, 256));
                f.SValid[id] = true;
                f.SCenterX[id] = Clamp(s.Center.X, 0, map.Width - 1);
                f.SCenterY[id] = Clamp(s.Center.Y, 0, map.Height - 1);
                f.SInvRadius2[id] = 1f / (r * (float)r);
                f.SCiv[id] = civ != null ? civ.Id : -1;
                f.SWeightSum[id] = 0f;
                f.SCategory[id] = -1f;
                f.SMetric[id] = 0f;
                switch (mode)
                {
                    case OverlayMode.Territory:
                        f.SCategory[id] = civ != null ? civ.Id : -1f;
                        break;
                    case OverlayMode.PopulationDensity:
                        f.SMetric[id] = s.ResidentIds.Count;
                        break;
                    case OverlayMode.Happiness: f.SMetric[id] = Unit(s.Happiness, 0.5f); break;
                    case OverlayMode.Health: f.SMetric[id] = Unit(s.Health, 1f); break;
                    case OverlayMode.Faith: f.SMetric[id] = Unit(s.Faith, 0.5f); break;
                    case OverlayMode.Wealth:
                    {
                        float gold = s.Stockpile.Get(ResourceType.Gold);
                        f.SMetric[id] = float.IsNaN(gold) || float.IsInfinity(gold) || gold < 0f ? 0f : gold;
                        break;
                    }
                    case OverlayMode.Age:
                        f.SMetric[id] = civ != null ? Clamp((int)civ.Age, 0, Counts.Ages - 1) : float.NaN;
                        break;
                    case OverlayMode.Religion:
                        f.SCategory[id] = civ != null && civ.Memory.ContainsKey(ReligionFoundedKey) ? civ.Id : -1f;
                        f.SMetric[id] = Unit(s.Faith, 0.5f);
                        break;
                }
            }
        }

        private static void GatherWars(WorldState state, OverlayField f)
        {
            List<War> wars = state.Diplomacy.Wars;
            for (int k = 0; k < wars.Count; k++)
            {
                var w = wars[k];
                if (w == null || !w.IsActive) continue;
                f.AddWar(w.AttackerId, w.DefenderId);
            }
        }

        private static void AccumulateWeightSums(WorldMap map, OverlayField f)
        {
            int[] owner = map.Owner;
            int w = map.Width, h = map.Height, limit = f.SettlementIdLimit;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int id = owner[row + x];
                    if ((uint)id >= (uint)limit || !f.SValid[id]) continue;
                    f.SWeightSum[id] += Falloff(x - f.SCenterX[id], y - f.SCenterY[id], f.SInvRadius2[id]);
                }
            }
        }

        // ================================================================== rows

        private static void RowsEmpty(OverlayField f, int y0, int y1)
        {
            float[] values = f.Values, weights = f.Weights;
            for (int i = y0 * f.Width, end = y1 * f.Width; i < end; i++) { values[i] = float.NaN; weights[i] = 0f; }
        }

        private static void RowsTerritory(WorldMap map, OverlayField f, int y0, int y1)
        {
            int[] owner = map.Owner;
            float[] values = f.Values, weights = f.Weights;
            int w = map.Width, h = map.Height, limit = f.SettlementIdLimit;
            for (int y = y0; y < y1; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    int id = owner[i];
                    if ((uint)id >= (uint)limit || !f.SValid[id]) { values[i] = float.NaN; weights[i] = 0f; continue; }
                    bool border = (x > 0 && owner[i - 1] != id) || (x < w - 1 && owner[i + 1] != id)
                               || (y > 0 && owner[i - w] != id) || (y < h - 1 && owner[i + w] != id);
                    values[i] = f.SCategory[id];
                    weights[i] = border ? 1f : TerritoryInteriorWeight;
                }
            }
        }

        private static void RowsSettlement(WorldMap map, OverlayField f, int y0, int y1)
        {
            int[] owner = map.Owner;
            float[] values = f.Values, weights = f.Weights;
            int w = map.Width, limit = f.SettlementIdLimit;
            OverlayMode mode = f.Mode;
            for (int y = y0; y < y1; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    int id = owner[i];
                    if ((uint)id >= (uint)limit || !f.SValid[id]) { values[i] = float.NaN; weights[i] = 0f; continue; }
                    float fall = Falloff(x - f.SCenterX[id], y - f.SCenterY[id], f.SInvRadius2[id]);
                    float v, weight;
                    switch (mode)
                    {
                        case OverlayMode.PopulationDensity:
                        {
                            float sum = f.SWeightSum[id];
                            v = sum > 1e-6f ? f.SMetric[id] * fall / sum : 0f;
                            weight = 1f;
                            break;
                        }
                        case OverlayMode.Age:
                            v = f.SMetric[id];
                            weight = 1f;
                            break;
                        case OverlayMode.Religion:
                            v = f.SCategory[id];
                            weight = fall * (0.4f + 0.6f * f.SMetric[id]);
                            break;
                        default:
                            v = f.SMetric[id];
                            weight = fall;
                            break;
                    }
                    if (float.IsNaN(v) || float.IsInfinity(v)) { values[i] = float.NaN; weights[i] = 0f; continue; }
                    values[i] = v;
                    weights[i] = weight;
                }
            }
        }

        private static void RowsTile(WorldMap map, float[] source, bool landOnly, OverlayField f, int y0, int y1)
        {
            float[] values = f.Values, weights = f.Weights;
            Biome[] biomes = map.Biomes;
            for (int i = y0 * f.Width, end = y1 * f.Width; i < end; i++)
            {
                float v = source[i];
                if (float.IsNaN(v) || float.IsInfinity(v) || (landOnly && biomes[i].IsWater())) { values[i] = float.NaN; weights[i] = 0f; continue; }
                values[i] = v;
                weights[i] = 1f;
            }
        }

        private static void RowsResources(WorldMap map, OverlayField f, int y0, int y1)
        {
            float[] values = f.Values, weights = f.Weights, source = map.Resource;
            for (int i = y0 * f.Width, end = y1 * f.Width; i < end; i++)
            {
                float v = source[i];
                if (!(v > 0f) || float.IsInfinity(v)) { values[i] = float.NaN; weights[i] = 0f; continue; }
                values[i] = v;
                weights[i] = 1f;
            }
        }

        /// <summary>Marks danger sources (burning tiles, war fronts); propagation happens in <see cref="End"/>.</summary>
        private static void RowsDanger(WorldMap map, OverlayField f, int y0, int y1)
        {
            int[] owner = map.Owner;
            TileFlags[] flags = map.Flags;
            float[] values = f.Values, weights = f.Weights;
            int w = map.Width, h = map.Height, limit = f.SettlementIdLimit;
            bool anyWar = f.WarCount > 0;
            for (int y = y0; y < y1; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    weights[i] = 1f;
                    if ((flags[i] & TileFlags.Burning) != 0) { values[i] = FireDanger; f.IncludeDanger(x, y); continue; }
                    float v = 0f;
                    if (anyWar)
                    {
                        int id = owner[i];
                        if ((uint)id < (uint)limit && f.SValid[id])
                        {
                            int civ = f.SCiv[id];
                            if (civ >= 0 &&
                                (IsEnemyTile(f, owner, x > 0 ? i - 1 : -1, id, civ, limit) ||
                                 IsEnemyTile(f, owner, x < w - 1 ? i + 1 : -1, id, civ, limit) ||
                                 IsEnemyTile(f, owner, y > 0 ? i - w : -1, id, civ, limit) ||
                                 IsEnemyTile(f, owner, y < h - 1 ? i + w : -1, id, civ, limit)))
                            {
                                v = WarFrontDanger;
                                f.IncludeDanger(x, y);
                            }
                        }
                    }
                    values[i] = v;
                }
            }
        }

        private static bool IsEnemyTile(OverlayField f, int[] owner, int n, int ownId, int ownCiv, int limit)
        {
            if (n < 0) return false;
            int other = owner[n];
            if (other == ownId || (uint)other >= (uint)limit || !f.SValid[other]) return false;
            int civ = f.SCiv[other];
            return civ != ownCiv && f.AtWar(ownCiv, civ);
        }

        private static void FinishDanger(WorldState state, OverlayField f)
        {
            var map = state.Map;
            float[] values = f.Values, weights = f.Weights;
            int w = f.Width, h = f.Height, n = f.TileCount;

            // Point sources: armies (by state and strength) and burning buildings.
            var armies = state.Diplomacy.Armies.Items;
            for (int k = 0; k < armies.Count; k++)
            {
                var a = armies[k];
                if (a == null || !a.IsAlive || !map.InBounds(a.Position)) continue;
                float intensity;
                switch (a.State)
                {
                    case ArmyState.Besieging: intensity = 1f; break;
                    case ArmyState.Marching: intensity = 0.9f; break;
                    case ArmyState.Retreating: intensity = 0.45f; break;
                    case ArmyState.Disbanding: intensity = 0.25f; break;
                    default: intensity = 0.55f; break;
                }
                float strength = a.Strength;
                float scale = float.IsNaN(strength) || strength <= 0f ? 0.6f : 0.6f + 0.4f * Math.Min(1f, strength / 50f);
                if (a.WarId < 0) scale *= 0.8f;
                int i = map.Index(a.Position);
                float v = intensity * scale;
                if (v > values[i]) values[i] = v;
                f.IncludeDanger(a.Position.X, a.Position.Y);
            }
            var buildings = state.Buildings.Items;
            for (int k = 0; k < buildings.Count; k++)
            {
                var b = buildings[k];
                if (b == null || b.IsDestroyed || !b.IsOnFire) continue;
                int bw = Clamp(b.Width, 1, 16), bh = Clamp(b.Height, 1, 16);
                for (int y = b.Origin.Y; y < b.Origin.Y + bh; y++)
                    for (int x = b.Origin.X; x < b.Origin.X + bw; x++)
                        if (map.InBounds(x, y)) { values[y * w + x] = FireDanger; f.IncludeDanger(x, y); }
            }

            // Two-pass chamfer propagation (8-neighbour): value = max(source - decay * distance), limited to the
            // sources' bounding box grown by the decay reach (exact: chamfer paths stay inside a convex box).
            if (f.DMaxX >= f.DMinX)
            {
                int reach = (int)Math.Ceiling(FireDanger / DangerDecayPerTile) + 1;
                int bx0 = Math.Max(0, f.DMinX - reach), by0 = Math.Max(0, f.DMinY - reach);
                int bx1 = Math.Min(w - 1, f.DMaxX + reach), by1 = Math.Min(h - 1, f.DMaxY + reach);
                Propagate(values, w, bx0, by0, bx1, by1);
            }

            for (int i = 0; i < n; i++)
            {
                float v = values[i];
                if (!(v >= DangerVisibleThreshold)) { values[i] = float.NaN; weights[i] = 0f; continue; }
                if (v > 1f) v = 1f;
                values[i] = v;
                weights[i] = 0.35f + 0.65f * v;
            }
        }

        private static void Propagate(float[] values, int w, int x0, int y0, int x1, int y1)
        {
            float d1 = DangerDecayPerTile, d2 = DangerDecayPerTile * Diagonal;
            for (int y = y0; y <= y1; y++)
            {
                int row = y * w;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x;
                    float v = values[i];
                    if (x > x0) v = Math.Max(v, values[i - 1] - d1);
                    if (y > y0)
                    {
                        v = Math.Max(v, values[i - w] - d1);
                        if (x > x0) v = Math.Max(v, values[i - w - 1] - d2);
                        if (x < x1) v = Math.Max(v, values[i - w + 1] - d2);
                    }
                    values[i] = v;
                }
            }
            for (int y = y1; y >= y0; y--)
            {
                int row = y * w;
                for (int x = x1; x >= x0; x--)
                {
                    int i = row + x;
                    float v = values[i];
                    if (x < x1) v = Math.Max(v, values[i + 1] - d1);
                    if (y < y1)
                    {
                        v = Math.Max(v, values[i + w] - d1);
                        if (x < x1) v = Math.Max(v, values[i + w + 1] - d2);
                        if (x > x0) v = Math.Max(v, values[i + w - 1] - d2);
                    }
                    values[i] = v;
                }
            }
        }

        // ================================================================== helpers

        private static float Unit(float v, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        private static string OneDecimal(float v)
        {
            long tenths = (long)Math.Round(Math.Max(-1e9, Math.Min(1e9, v * 10.0)), MidpointRounding.AwayFromZero);
            bool neg = tenths < 0;
            if (neg) tenths = -tenths;
            string s = (tenths / 10) + "." + (tenths % 10);
            return neg ? "-" + s : s;
        }
    }
}

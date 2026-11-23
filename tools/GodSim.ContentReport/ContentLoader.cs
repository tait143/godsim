using System;
using System.Collections.Generic;
using System.Reflection;
using GodSim.Core;

namespace GodSim.ContentReport
{
    /// <summary>Result of building the database: what loaded, what failed and whether Freeze succeeded.</summary>
    internal sealed class LoadResult
    {
        public GameDatabase Db;
        public bool Frozen;
        public readonly List<string> CatalogsLoaded = new List<string>();
        /// <summary>Catalogs that are missing from the build or threw while registering.</summary>
        public readonly List<string> CatalogErrors = new List<string>();
        /// <summary>Freeze()/Validate() problems (structural content errors).</summary>
        public readonly List<string> ValidationErrors = new List<string>();
    }

    /// <summary>
    /// Builds the <see cref="GameDatabase"/> the same way <c>Composition.DefaultComposition</c> does:
    /// RaceCatalog, AgeCatalog, TechCatalog, BuildingCatalog, GodPowerCatalog, then Freeze.
    /// Catalogs are located by name so the isolated check project (content folders only) can drop a folder
    /// that is mid-edit; a missing or throwing catalog becomes a content error instead of a crash, and a failed
    /// Freeze still leaves a resolved (unfrozen) database so the report can show what exists.
    /// </summary>
    internal static class ContentLoader
    {
        /// <summary>Composition order (contract section 6).</summary>
        public static readonly string[][] Catalogs =
        {
            new[] { "Races", "GodSim.Core.Races.RaceCatalog" },
            new[] { "Ages", "GodSim.Core.Progression.AgeCatalog" },
            new[] { "Techs", "GodSim.Core.Progression.TechCatalog" },
            new[] { "Buildings", "GodSim.Core.Buildings.BuildingCatalog" },
            new[] { "God powers", "GodSim.Core.Divine.GodPowerCatalog" },
        };

        public static LoadResult Load()
        {
            var result = new LoadResult { Db = new GameDatabase() };
            foreach (var c in Catalogs)
            {
                string label = c[0], typeName = c[1];
                var type = FindType(typeName);
                var method = type?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameDatabase) }, null);
                if (method == null)
                {
                    result.CatalogErrors.Add(label + ": " + typeName + ".Register(GameDatabase) is not compiled into this build.");
                    continue;
                }
                try
                {
                    method.Invoke(null, new object[] { result.Db });
                    result.CatalogsLoaded.Add(label);
                }
                catch (TargetInvocationException ex)
                {
                    var inner = ex.InnerException ?? ex;
                    result.CatalogErrors.Add(label + ": " + typeName + ".Register threw " + inner.GetType().Name + ": " + inner.Message);
                }
            }

            try
            {
                result.Db.Freeze();
                result.Frozen = true;
            }
            catch (Exception ex)
            {
                // Freeze stops at the first unknown prerequisite; resolve what we can and collect the full list.
                ResolvePrerequisites(result.Db, result.ValidationErrors);
                foreach (var e in result.Db.Validate()) AddUnique(result.ValidationErrors, e);
                if (result.ValidationErrors.Count == 0) result.ValidationErrors.Add("Freeze failed: " + ex.Message);
            }
            return result;
        }

        private static Type FindType(string fullName)
        {
            var t = typeof(GameDatabase).Assembly.GetType(fullName, false);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static void ResolvePrerequisites(GameDatabase db, List<string> errors)
        {
            foreach (var t in db.Techs)
            {
                var ids = new List<int>();
                foreach (var k in t.PrerequisiteKeys ?? new string[0])
                {
                    int id = db.TechIdOf(k);
                    if (id < 0) AddUnique(errors, "Tech '" + t.Key + "' has unknown prerequisite '" + k + "'.");
                    else ids.Add(id);
                }
                t.Prerequisites = ids.ToArray();
            }
        }

        private static void AddUnique(List<string> list, string s) { if (!list.Contains(s)) list.Add(s); }
    }
}

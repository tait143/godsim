using System.Collections.Generic;

namespace GodSim.Core.Security
{
    /// <summary>
    /// Composition entry point for the security module. Besides the systems returned here, Composition should install
    /// <c>new GodCommandValidator(config)</c> as the <see cref="CommandQueue"/> validator, run
    /// <see cref="InputSanitizer.ClampConfig"/> on untrusted configs, and check content with
    /// <see cref="ContentValidator.Analyze"/> before freezing the database.
    /// </summary>
    public static class SecurityModule
    {
        /// <summary>Systems owned by the security module: currently only the <see cref="IntegritySystem"/>.</summary>
        public static IEnumerable<ISimSystem> CreateSystems()
        {
            return new ISimSystem[] { new IntegritySystem() };
        }
    }
}

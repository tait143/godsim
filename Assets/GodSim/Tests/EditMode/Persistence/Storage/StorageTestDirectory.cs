using System;
using System.IO;

namespace GodSim.Tests.Persistence.Storage
{
    /// <summary>Unique scratch directory under the system temp path, removed on Dispose.</summary>
    internal sealed class StorageTestDirectory : IDisposable
    {
        public StorageTestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "godsim-storage-tests", System.IO.Path.GetRandomFileName().Replace(".", ""));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Combine(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, true);
            }
            catch (Exception)
            {
                // Best effort: a leftover temp directory must not fail the test run.
            }
        }
    }
}

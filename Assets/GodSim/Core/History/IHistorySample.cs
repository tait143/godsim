namespace GodSim.Core.History
{
    /// <summary>
    /// Implemented by sample structs stored in a <see cref="DownsamplingBuffer{T}"/>. When the buffer drops a
    /// sample (downsampling, or replacing a provisional tail), the next kept sample absorbs the dropped one so
    /// that additive fields (births, deaths, ...) still sum to the true totals over the whole timeline.
    /// </summary>
    internal interface IHistorySample<T> where T : struct
    {
        /// <summary>Returns this sample with the additive fields of <paramref name="earlier"/> folded in.</summary>
        T AbsorbEarlier(T earlier);
    }
}

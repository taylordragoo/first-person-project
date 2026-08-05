using System.Collections.Generic;

namespace FPSProject.Multiplayer.Core.Diagnostics
{
    public static class MultiplayerPerformanceMath
    {
        public static float Percentile(IReadOnlyList<float> values, float percentile)
        {
            if (values == null || values.Count == 0) return 0f;
            var sorted = new List<float>(values);
            sorted.Sort();
            float clamped = UnityEngine.Mathf.Clamp01(percentile);
            int index = UnityEngine.Mathf.Clamp(
                UnityEngine.Mathf.CeilToInt(clamped * sorted.Count) - 1,
                0, sorted.Count - 1);
            return sorted[index];
        }
    }
}

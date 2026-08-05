using FPSProject.Multiplayer.Core.Diagnostics;
using NUnit.Framework;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class MultiplayerPerformanceMathTests
    {
        [Test]
        public void Percentile_ReturnsNearestRankValue()
        {
            float[] values = { 5f, 1f, 4f, 2f, 3f };
            Assert.AreEqual(5f, MultiplayerPerformanceMath.Percentile(values, 0.95f));
            Assert.AreEqual(3f, MultiplayerPerformanceMath.Percentile(values, 0.5f));
        }

        [Test]
        public void Percentile_EmptyInputReturnsZero()
        {
            Assert.AreEqual(0f, MultiplayerPerformanceMath.Percentile(null, 0.95f));
        }
    }
}

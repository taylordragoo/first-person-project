using FPSProject.Multiplayer.Core.Weapons;
using NUnit.Framework;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class NetworkShotDamageBudgetTests
    {
        [Test]
        public void DistinctPelletsCanDamageSameTargetToCap_ButDuplicateContactCannot()
        {
            var budget = new NetworkShotDamageBudget<object>(96f);
            var target = new object();

            for (int pellet = 0; pellet < 8; pellet++)
            {
                budget.BeginContactPath();
                Assert.IsTrue(budget.TryReserve(target, 12f), $"Pellet {pellet} was rejected.");
                Assert.IsFalse(budget.TryReserve(target, 12f),
                    "The same pellet/contact path resolved the target twice.");
            }

            Assert.AreEqual(96f, budget.TotalDamage);

            budget.BeginContactPath();
            Assert.IsFalse(budget.TryReserve(target, 12f),
                "A ninth pellet exceeded the shot-wide damage cap.");
            Assert.AreEqual(96f, budget.TotalDamage);
        }
    }
}

using UnityEngine;

namespace FPSProject.Multiplayer.Core.Match
{
    public interface IMatchTeamProvider
    {
        MatchTeam TeamValue { get; }
    }

    public static class MatchTeamResolver
    {
        public static bool TryGetTeam(GameObject source, out MatchTeam team)
        {
            team = MatchTeam.Unassigned;
            if (source == null) return false;

            IMatchTeamProvider parentProvider = source.GetComponentInParent<IMatchTeamProvider>(true);
            if (parentProvider != null && MatchRules.IsPlayableTeam(parentProvider.TeamValue))
            {
                team = parentProvider.TeamValue;
                return true;
            }

            IMatchTeamProvider childProvider = source.GetComponentInChildren<IMatchTeamProvider>(true);
            if (childProvider != null && MatchRules.IsPlayableTeam(childProvider.TeamValue))
            {
                team = childProvider.TeamValue;
                return true;
            }

            return false;
        }

        public static bool AreFriendly(GameObject left, GameObject right)
        {
            return TryGetTeam(left, out MatchTeam leftTeam)
                && TryGetTeam(right, out MatchTeam rightTeam)
                && leftTeam == rightTeam;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Authored candidates -> occupancy filter -> safety score.
// Arena adaptation of Matthew Jack's tactical position-selection pattern (Game AI Pro, ch. 26).
public static class ArenaSpawnSelection
{
    public struct Placement
    {
        public ArenaParticipant participant;
        public Vector3 position;
        public Quaternion rotation;
    }

    public static bool TryChoose(ArenaMatch match, ArenaParticipant incoming,
        Dictionary<Transform, float> history, out Transform selected)
        => TryChooseFrom(match, incoming, match.spawnPoints, history, out selected);

    public static bool TryChooseFrom(ArenaMatch match, ArenaParticipant incoming,
        IEnumerable<Transform> points, Dictionary<Transform, float> history, out Transform selected)
    {
        selected = null;
        float best = float.NegativeInfinity;
        bool foundSafe = false;
        foreach (var point in points)
        {
            if (!HasSpace(match, incoming, point.position)) continue;
            float enemyDistance = 40f;
            int exposed = 0;
            foreach (var other in match.participants)
            {
                if (other == incoming || other.health.IsDead || other.IsAwaitingSpawn || other.team == incoming.team) continue;
                enemyDistance = Mathf.Min(enemyDistance, Vector3.Distance(point.position, other.transform.position));
                if (VisibleToEnemy(other, point.position + Vector3.up * 1.25f)) exposed++;
            }
            bool safe = enemyDistance >= match.settings.unsafeEnemyDistance && exposed == 0;
            float score = enemyDistance - exposed * 25f;
            if (history.TryGetValue(point, out float usedAt))
                score -= Mathf.Max(0f, match.settings.recentSpawnTime - (Time.time - usedAt)) * 2f;
            score += Random.value * 2f;
            if (foundSafe && !safe || safe == foundSafe && score <= best) continue;
            selected = point; best = score; foundSafe = safe;
        }
        // When every candidate is exposed, use the least dangerous free location.
        // When all are occupied, the participant keeps its pending respawn and retries.
        return selected;
    }

    public static bool HasSpace(ArenaMatch match, ArenaParticipant incoming, Vector3 position, bool opening = false)
    {
        if (!opening)
            foreach (var other in match.participants)
                if (other != incoming && other.gameObject.activeInHierarchy &&
                    Vector3.Distance(position, other.transform.position) < match.settings.spawnClearance) return false;
        Vector3 center = position + Vector3.up;
        float radius = .4f, halfSegment = .5f;
        if (incoming.player)
        {
            var controller = incoming.player.controller;
            center = position + controller.center; radius = controller.radius;
            halfSegment = Mathf.Max(0f, controller.height * .5f - radius);
        }
        foreach (var collider in Physics.OverlapCapsule(center - Vector3.up * halfSegment, center + Vector3.up * halfSegment,
            radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (collider.transform.IsChildOf(incoming.transform)) continue;
            var actor = collider.GetComponentInParent<ArenaParticipant>();
            if (opening && actor && actor.match == match) continue;
            return false;
        }
        return true;
    }

    public static bool TryPlanOpening(ArenaMatch match, out List<Placement> placements,
        out Transform redAnchor, out Transform blueAnchor)
    {
        placements = new List<Placement>(); redAnchor = blueAnchor = null;
        var red = System.Array.FindAll(match.participants, p => p.team == ArenaParticipant.Team.Red);
        var blue = System.Array.FindAll(match.participants, p => p.team == ArenaParticipant.Team.Blue);
        if (match.participants.Length == 0 || match.spawnPoints.Length == 0) return false;
        if (match.redOpeningSpawn && match.blueOpeningSpawn)
        {
            redAnchor = match.redOpeningSpawn;
            blueAnchor = match.blueOpeningSpawn;
            if (red.Length > 0 && !TryFormation(match, redAnchor.position, red, placements) ||
                blue.Length > 0 && !TryFormation(match, blueAnchor.position, blue, placements)) return false;
            for (int i = 0; i < placements.Count; i++)
            {
                var placement = placements[i];
                placement.rotation = placement.participant.team == ArenaParticipant.Team.Red
                    ? redAnchor.rotation : blueAnchor.rotation;
                placements[i] = placement;
            }
            return true;
        }
        int start = Random.Range(0, match.spawnPoints.Length);
        // A one-team setup is useful for navigation/sprint tests without combat.
        if (red.Length == 0 || blue.Length == 0)
        {
            var team = red.Length > 0 ? red : blue;
            for (int i = 0; i < match.spawnPoints.Length; i++)
            {
                var anchor = match.spawnPoints[(start + i) % match.spawnPoints.Length];
                var plan = new List<Placement>();
                if (!TryFormation(match, anchor.position, team, plan)) continue;
                for (int j = 0; j < plan.Count; j++)
                {
                    var placement = plan[j]; placement.rotation = anchor.rotation; plan[j] = placement;
                }
                placements = plan;
                if (red.Length > 0) redAnchor = anchor; else blueAnchor = anchor;
                return true;
            }
            return false;
        }
        if (match.spawnPoints.Length < 2) return false;
        for (int i = 0; i < match.spawnPoints.Length; i++)
        {
            var first = match.spawnPoints[(start + i) % match.spawnPoints.Length];
            var redPlan = new List<Placement>();
            if (!TryFormation(match, first.position, red, redPlan)) continue;
            float best = float.NegativeInfinity;
            foreach (var second in match.spawnPoints)
            {
                float distance = Vector3.Distance(first.position, second.position);
                if (second == first || distance < Mathf.Max(match.settings.openingTeamSeparation, match.settings.openingTeamRadius * 2f)) continue;
                var plan = new List<Placement>(redPlan);
                if (!TryFormation(match, second.position, blue, plan)) continue;
                // Prefer separated, occluded openings instead of immediate sight-line duels.
                float score = distance + Random.value * 5f;
                if (!VisibleBetween(first.position + Vector3.up * 1.6f, second.position + Vector3.up * 1.6f)) score += 20f;
                if (score <= best) continue;
                best = score; placements = plan; redAnchor = first; blueAnchor = second;
            }
            if (blueAnchor)
            {
                for (int j = 0; j < placements.Count; j++)
                {
                    var placement = placements[j];
                    Vector3 opposing = placement.participant.team == ArenaParticipant.Team.Red ? blueAnchor.position : redAnchor.position;
                    placement.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(opposing - placement.position, Vector3.up));
                    placements[j] = placement;
                }
                return true;
            }
        }
        return false;
    }

    static bool TryFormation(ArenaMatch match, Vector3 anchor, ArenaParticipant[] team, List<Placement> plan)
    {
        int placed = 0;
        float spacing = Mathf.Max(2f, match.settings.spawnClearance + .4f);
        var agent = team[0].GetComponent<NavMeshAgent>();
        var filter = new NavMeshQueryFilter { agentTypeID = agent ? agent.agentTypeID : NavMesh.GetSettingsByIndex(0).agentTypeID,
            areaMask = NavMesh.AllAreas };
        for (float radius = 0f; radius <= match.settings.openingTeamRadius; radius += spacing)
        {
            int count = radius == 0f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * radius / spacing);
            for (int i = 0; i < count && placed < team.Length; i++)
            {
                Vector3 proposed = anchor + Quaternion.Euler(0, i * 360f / count, 0) * Vector3.forward * radius;
                if (!NavMesh.SamplePosition(proposed, out var hit, .75f, filter)) continue;
                if (Vector3.Distance(anchor, hit.position) > match.settings.openingTeamRadius + .05f
                    || Mathf.Abs(hit.position.y - anchor.y) > .6f || NavMesh.Raycast(anchor, hit.position, out _, filter)) continue;
                bool crowded = false;
                foreach (var reserved in plan)
                    if (Vector3.Distance(reserved.position, hit.position) < match.settings.spawnClearance) { crowded = true; break; }
                if (crowded || !HasSpace(match, team[placed], hit.position, true)) continue;
                plan.Add(new Placement { participant = team[placed++], position = hit.position });
            }
            if (placed == team.Length) return true;
        }
        return false;
    }

    static bool VisibleToEnemy(ArenaParticipant enemy, Vector3 destination)
    {
        Vector3 origin = enemy.transform.position + Vector3.up * 1.6f;
        return VisibleBetween(origin, destination);
    }

    static bool VisibleBetween(Vector3 origin, Vector3 destination)
    {
        Vector3 delta = destination - origin;
        foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.collider.GetComponentInParent<SoldierHealth>()) return false;
        // Conservative visibility: don't assume an enemy won't turn around.
        return true;
    }
}

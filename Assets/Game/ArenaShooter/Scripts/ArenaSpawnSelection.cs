using System.Collections.Generic;
using UnityEngine;

// Authored candidates -> occupancy filter -> safety score.
// Arena adaptation of Matthew Jack's tactical position-selection pattern (Game AI Pro, ch. 26).
public static class ArenaSpawnSelection
{
    public static bool TryChoose(ArenaMatch match, ArenaParticipant incoming,
        Dictionary<Transform, float> history, out Transform selected)
    {
        selected = null;
        float best = float.NegativeInfinity;
        bool foundSafe = false;
        var points = incoming.team == ArenaParticipant.Team.Red ? match.redSpawns : match.blueSpawns;
        foreach (var point in points)
        {
            if (!HasSpace(match, incoming, point.position)) continue;
            float enemyDistance = 40f;
            int exposed = 0;
            foreach (var other in match.participants)
            {
                if (other == incoming || other.health.IsDead || other.team == incoming.team) continue;
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

    static bool HasSpace(ArenaMatch match, ArenaParticipant incoming, Vector3 position)
    {
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
            if (!collider.transform.IsChildOf(incoming.transform)) return false;
        return true;
    }

    static bool VisibleToEnemy(ArenaParticipant enemy, Vector3 destination)
    {
        Vector3 origin = enemy.transform.position + Vector3.up * 1.6f;
        Vector3 delta = destination - origin;
        foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.collider.GetComponentInParent<SoldierHealth>()) return false;
        // Conservative visibility: don't assume an enemy won't turn around.
        return true;
    }
}

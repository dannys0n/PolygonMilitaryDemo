using UnityEngine;
using UnityEngine.AI;

// Generate -> filter -> score, adapted from Matthew Jack's Tactical Position Selection:
// https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter26_Tactical_Position_Selection.pdf
// Bounded queries run at decision intervals, not every animation frame.
public static class ArenaAITactics
{
    public struct Cover { public Vector3 position, peek; }
    public static bool FindCover(ArenaAIInput bot, Vector3 threat, NavMeshPath path, out Cover cover)
    {
        cover = default; float best = float.NegativeInfinity;
        for (int ring = 1; ring <= 3; ring++)
            for (int spoke = 0; spoke < 12; spoke++)
            {
                Vector3 proposed = bot.transform.position + Quaternion.Euler(0, spoke * 30f, 0)
                    * Vector3.forward * (bot.coverSearchRadius * ring / 3f);
                if (!Reachable(bot, proposed, path, out var position, out float travel)) continue;
                float threatDistance = Vector3.Distance(position, threat);
                if (threatDistance < 4f || threatDistance > bot.attackRange + 5f || !RoomForTeam(bot, position)) continue;
                if (!WorldBlocked(bot, threat, position + Vector3.up * 1.25f)) continue;
                // Standing cover must also offer a nearby firing position.
                Vector3 side = Vector3.Cross(Vector3.up, threat - position).normalized;
                bool hasPeek = false; Vector3 peek = position;
                for (int sign = -1; sign <= 1 && !hasPeek; sign += 2)
                    for (int step = 1; step <= 3 && !hasPeek; step++)
                    {
                        Vector3 candidate = position + side * sign * step * .8f;
                        if (!Reachable(bot, candidate, path, out var p, out _)) continue;
                        if (NavMesh.Raycast(position, p, out _, bot.agent.areaMask)) continue;
                        if (WorldBlocked(bot, threat, p + Vector3.up * 1.25f) || !RoomForTeam(bot, p)) continue;
                        peek = p; hasPeek = true;
                    }
                if (!hasPeek) continue;
                float score = -travel - Mathf.Abs(threatDistance - bot.attackRange * .7f) * .4f;
                if (score <= best) continue;
                best = score; cover = new Cover { position = position, peek = peek };
            }
        return best > float.NegativeInfinity;
    }

    public static bool FindCombatPosition(ArenaAIInput bot, Vector3 threat, NavMeshPath path, out Vector3 position)
    {
        position = bot.transform.position; float best = float.NegativeInfinity;
        Vector3 toward = Vector3.ProjectOnPlane(threat - position, Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, toward);
        int preferred = Random.value < .5f ? -1 : 1;
        for (int i = 0; i < 6; i++)
        {
            int sign = i % 2 == 0 ? preferred : -preferred;
            Vector3 proposed = bot.transform.position + side * sign * bot.strafeDistance
                + toward * ((i / 2 - 1) * bot.strafeDistance * .5f);
            if (!Reachable(bot, proposed, path, out var point, out float travel) || !RoomForTeam(bot, point)) continue;
            if (NavMesh.Raycast(bot.transform.position, point, out _, bot.agent.areaMask)) continue;
            if (Vector3.Distance(bot.transform.position, point) < .8f) continue;
            float exposure = WorldBlocked(bot, point + Vector3.up * 1.25f, threat) ? -10f : 0f;
            float score = exposure - travel * .2f + (sign == preferred ? .5f : 0f);
            if (score > best) { best = score; position = point; }
        }
        return best > float.NegativeInfinity;
    }

    public static bool Reachable(ArenaAIInput bot, Vector3 proposed, NavMeshPath path, out Vector3 point, out float length)
    {
        point = default; length = 0f;
        if (!NavMesh.SamplePosition(proposed, out var hit, 1f, bot.agent.areaMask)) return false;
        point = hit.position;
        // A complete navigation path decides connectivity, including ramps between floors.
        var controller = bot.life.controller; float radius = controller.radius;
        Vector3 center = point + controller.center;
        float halfSegment = Mathf.Max(0f, controller.height * .5f - radius);
        foreach (var collider in Physics.OverlapCapsule(center - Vector3.up * halfSegment, center + Vector3.up * halfSegment,
                     radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!collider.transform.IsChildOf(bot.transform) && !(collider is CharacterController)) return false;
        if (!NavMesh.CalculatePath(bot.transform.position, point, bot.agent.areaMask, path) || path.status != NavMeshPathStatus.PathComplete) return false;
        var corners = path.corners;
        for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
        return true;
    }

    static bool RoomForTeam(ArenaAIInput bot, Vector3 point)
    {
        foreach (var other in bot.participant.match.participants)
        {
            if (other == bot.participant || other.health.IsDead || other.team != bot.participant.team) continue;
            if (Vector3.Distance(point, other.transform.position) < 1.5f) return false;
            var ai = other.GetComponent<ArenaAIInput>();
            if (ai && ai.HasCover && Vector3.Distance(point, ai.CoverPosition) < 1.5f) return false;
        }
        return true;
    }

    public static bool WorldBlocked(ArenaAIInput bot, Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.collider.transform.IsChildOf(bot.transform) && !(hit.collider is CharacterController)
                && !hit.collider.GetComponentInParent<SoldierHealth>()) return true;
        return false;
    }

    public static bool ClearShot(ArenaAIInput bot, Vector3 from, Vector3 to, ArenaParticipant target)
    {
        Vector3 delta = to - from; float closest = delta.magnitude; Collider blocker = null;
        foreach (var hit in Physics.RaycastAll(from, delta.normalized, closest, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (hit.collider.transform.IsChildOf(bot.transform) || hit.collider is CharacterController) continue;
            if (hit.collider.isTrigger && !hit.collider.GetComponent<SoldierHitRegion>()) continue;
            if (hit.distance <= closest) { closest = hit.distance; blocker = hit.collider; }
        }
        return !blocker || blocker.GetComponentInParent<ArenaParticipant>() == target;
    }
}

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Loss-bounded authoring-time reduction for baked weapon motion. Does not remove bindings,
/// resample motion, change clip settings/events or touch object-reference curves.
/// The caller owns saving the clip; use a clone to compare before committing an asset.
/// </summary>
public static class AnimationClipCompactor
{
    // Local-space metres: 0.01 mm per component. These are deliberately much smaller
    // than visible finger/trigger contact changes, including after hierarchical scaling.
    public const float PositionError = 0.00001f;
    public const float ScaleError = 0.000001f;

    public sealed class Stats
    {
        public int curves;
        public int keysBefore;
        public int keysAfter;
        public int reducedCurves;
        public int preservedRotationCurves;
        public int preservedOtherCurves;
        public double maximumPositionError;
        public double maximumScaleError;
        public int removedKeys => keysBefore - keysAfter;
    }

    public sealed class Validation
    {
        public bool valid = true;
        public int curves;
        public int samples;
        public double maximumPositionError;
        public double maximumScaleError;
        public readonly List<string> failures = new List<string>();
    }

    /// <summary>
    /// Reduces a supplied clip in memory. All candidates are validated before any changes
    /// are applied. Nonconstant quaternion/Euler tracks stay untouched: independently
    /// simplifying their components would not provide an angular-error guarantee.
    /// </summary>
    public static Stats Optimize(AnimationClip clip)
    {
        if (!clip) throw new ArgumentNullException(nameof(clip));
        var stats = new Stats();
        var changedBindings = new List<EditorCurveBinding>();
        var changedCurves = new List<AnimationCurve>();
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var original = AnimationUtility.GetEditorCurve(clip, binding);
            stats.curves++;
            stats.keysBefore += original.length;
            var keys = original.keys;
            AnimationCurve candidate = null;
            bool rotation = IsRotation(binding);
            double tolerance = Tolerance(binding);
            if (keys.Length > 2)
            {
                if (ExactlyConstant(keys))
                    candidate = LinearCurve(new[] { keys[0], keys[keys.Length - 1] }, original);
                else if (tolerance > 0 && CanAnalyze(keys))
                    candidate = Reduce(original, tolerance * 0.95);
            }
            if (candidate != null && candidate.length < original.length)
            {
                double error = Math.Max(MaximumError(original, candidate), MaximumSampleError(original, candidate));
                // Unity evaluates curves in floats. Even a mathematically constant large
                // scale value can differ by one ULP after reducing its keys. Preserve the
                // original curve whenever native evaluation exceeds the same strict bound.
                if (!double.IsNaN(error) && error <= tolerance)
                {
                    changedBindings.Add(binding);
                    changedCurves.Add(candidate);
                    stats.reducedCurves++;
                    if (IsPosition(binding)) stats.maximumPositionError = Math.Max(stats.maximumPositionError, error);
                    if (IsScale(binding)) stats.maximumScaleError = Math.Max(stats.maximumScaleError, error);
                    stats.keysAfter += candidate.length;
                    continue;
                }
            }
            stats.keysAfter += original.length;
            if (rotation) stats.preservedRotationCurves++;
            else if (tolerance <= 0) stats.preservedOtherCurves++;
        }
        if (changedBindings.Count > 0)
            AnimationUtility.SetEditorCurves(clip, changedBindings.ToArray(), changedCurves.ToArray());
        return stats;
    }

    /// <summary>
    /// Checks original key times and interval midpoints as well as the analytic extrema
    /// of the difference on every original interval. No asset writes are performed.
    /// </summary>
    public static Validation Validate(AnimationClip original, AnimationClip compacted)
    {
        if (!original || !compacted) throw new ArgumentNullException("Both clips are required");
        var report = new Validation();
        var before = AnimationUtility.GetCurveBindings(original);
        var after = AnimationUtility.GetCurveBindings(compacted);
        if (before.Length != after.Length) Fail(report, "Float binding count changed");
        foreach (var binding in before)
        {
            report.curves++;
            var source = AnimationUtility.GetEditorCurve(original, binding);
            var target = AnimationUtility.GetEditorCurve(compacted, binding);
            if (target == null) { Fail(report, "Missing " + Label(binding)); continue; }
            double tolerance = Tolerance(binding);
            double error;
            // Unmodified weighted/stepped/non-transform curves must remain identical.
            if (SameKeys(source, target)) error = 0;
            else if (!CanAnalyze(source.keys) || !CanAnalyze(target.keys))
            { Fail(report, "Unsupported curve changed: " + Label(binding)); continue; }
            else error = MaximumError(source, target);
            var keys = source.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                error = Math.Max(error, Math.Abs((double)source.Evaluate(keys[i].time) - target.Evaluate(keys[i].time)));
                report.samples++;
                if (i + 1 < keys.Length)
                {
                    float time = keys[i].time + (keys[i + 1].time - keys[i].time) * 0.5f;
                    error = Math.Max(error, Math.Abs((double)source.Evaluate(time) - target.Evaluate(time)));
                    report.samples++;
                }
            }
            // Float evaluation can differ by a few ULPs after mathematically exact linear
            // reduction. The analytical test remains the strict authoring-error bound.
            double evaluationSlack = tolerance > 0 ? tolerance * 0.02 : 0;
            if (double.IsNaN(error) || error > tolerance + evaluationSlack)
                Fail(report, Label(binding) + " error=" + error.ToString("G9") + " limit=" + tolerance.ToString("G9"));
            if (IsPosition(binding)) report.maximumPositionError = Math.Max(report.maximumPositionError, error);
            if (IsScale(binding)) report.maximumScaleError = Math.Max(report.maximumScaleError, error);
            if (keys.Length > 0 && (target.keys[0].time != keys[0].time || target.keys[target.length - 1].time != keys[keys.Length - 1].time))
                Fail(report, "Endpoint time changed: " + Label(binding));
            if (source.preWrapMode != target.preWrapMode || source.postWrapMode != target.postWrapMode)
                Fail(report, "Wrap mode changed: " + Label(binding));
        }
        if (JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(original)) != JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(compacted)))
            Fail(report, "Clip settings changed");
        var originalEvents = AnimationUtility.GetAnimationEvents(original);
        var compactedEvents = AnimationUtility.GetAnimationEvents(compacted);
        if (originalEvents.Length != compactedEvents.Length) Fail(report, "Animation event count changed");
        else for (int i = 0; i < originalEvents.Length; i++)
        {
            var a = originalEvents[i]; var b = compactedEvents[i];
            if (a.time != b.time || a.functionName != b.functionName || a.floatParameter != b.floatParameter ||
                a.intParameter != b.intParameter || a.stringParameter != b.stringParameter ||
                a.objectReferenceParameter != b.objectReferenceParameter || a.messageOptions != b.messageOptions)
                Fail(report, "Animation event changed at " + i);
        }
        var originalObjects = AnimationUtility.GetObjectReferenceCurveBindings(original);
        var compactedObjects = AnimationUtility.GetObjectReferenceCurveBindings(compacted);
        if (originalObjects.Length != compactedObjects.Length) Fail(report, "Object-reference binding count changed");
        foreach (var binding in originalObjects)
        {
            var a = AnimationUtility.GetObjectReferenceCurve(original, binding);
            var b = AnimationUtility.GetObjectReferenceCurve(compacted, binding);
            if (a == null || b == null || a.Length != b.Length) { Fail(report, "Object-reference curve changed: " + Label(binding)); continue; }
            for (int i = 0; i < a.Length; i++)
                if (a[i].time != b[i].time || a[i].value != b[i].value) Fail(report, "Object-reference key changed: " + Label(binding));
        }
        return report;
    }

    static void Fail(Validation report, string failure) { report.valid = false; report.failures.Add(failure); }
    static string Label(EditorCurveBinding binding) => binding.path + ":" + binding.propertyName;
    static bool IsPosition(EditorCurveBinding binding) => binding.type == typeof(Transform) && binding.propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal);
    static bool IsScale(EditorCurveBinding binding) => binding.type == typeof(Transform) && binding.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal);
    static bool IsRotation(EditorCurveBinding binding) => binding.type == typeof(Transform) && (binding.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal) || binding.propertyName.IndexOf("Euler", StringComparison.OrdinalIgnoreCase) >= 0);
    static double Tolerance(EditorCurveBinding binding) => IsPosition(binding) ? PositionError : IsScale(binding) ? ScaleError : 0;
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    static bool CanAnalyze(Keyframe[] keys)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            if (!Finite(keys[i].value) || !Finite(keys[i].time) || keys[i].weightedMode != WeightedMode.None) return false;
            if (i + 1 < keys.Length && (keys[i + 1].time <= keys[i].time || !Finite(keys[i].outTangent) || !Finite(keys[i + 1].inTangent))) return false;
        }
        return true;
    }

    static bool ExactlyConstant(Keyframe[] keys)
    {
        if (!CanAnalyze(keys) || keys.Length == 0) return false;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i].value != keys[0].value) return false;
            if (i + 1 < keys.Length && (keys[i].outTangent != 0 || keys[i + 1].inTangent != 0)) return false;
        }
        return true;
    }

    static bool SameKeys(AnimationCurve a, AnimationCurve b)
    {
        var x = a.keys; var y = b.keys;
        if (x.Length != y.Length) return false;
        for (int i = 0; i < x.Length; i++) if (!x[i].Equals(y[i])) return false;
        return true;
    }

    static AnimationCurve LinearCurve(Keyframe[] keys, AnimationCurve source)
    {
        var curve = new AnimationCurve(keys) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode };
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyBroken(curve, i, true);
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        return curve;
    }

    static AnimationCurve Reduce(AnimationCurve source, double tolerance)
    {
        var keys = source.keys;
        var keep = new bool[keys.Length]; keep[0] = keep[keys.Length - 1] = true;
        var spans = new Stack<(int first, int last)>();
        spans.Push((0, keys.Length - 1));
        while (spans.Count > 0)
        {
            var span = spans.Pop();
            int worstSegment = span.first;
            double worstTime = 0, worst = 0;
            double slope = ((double)keys[span.last].value - keys[span.first].value) / ((double)keys[span.last].time - keys[span.first].time);
            for (int i = span.first; i < span.last; i++)
            {
                double atStart = keys[span.first].value + slope * ((double)keys[i].time - keys[span.first].time);
                double error = SegmentError(keys[i], keys[i + 1], atStart, slope, out double time);
                if (error > worst) { worst = error; worstSegment = i; worstTime = time; }
            }
            if (worst <= tolerance) continue;
            // Some authored curves have overshoot between adjacent keys. Keep the original
            // curve when a line cannot reproduce that segment within the bound.
            if (span.last <= span.first + 1) return null;
            int split = worstTime <= 0.5 ? worstSegment : worstSegment + 1;
            split = Math.Max(span.first + 1, Math.Min(span.last - 1, split));
            keep[split] = true;
            spans.Push((span.first, split)); spans.Push((split, span.last));
        }
        var retained = new List<Keyframe>();
        for (int i = 0; i < keys.Length; i++) if (keep[i]) retained.Add(keys[i]);
        if (retained.Count == keys.Length) return null;
        return LinearCurve(retained.ToArray(), source);
    }

    static double MaximumSampleError(AnimationCurve original, AnimationCurve candidate)
    {
        var keys = original.keys;
        double maximum = 0;
        for (int i = 0; i < keys.Length; i++)
        {
            float time = keys[i].time;
            maximum = Math.Max(maximum, Math.Abs((double)original.Evaluate(time) - candidate.Evaluate(time)));
            if (i + 1 < keys.Length)
            {
                time += (keys[i + 1].time - time) * 0.5f;
                maximum = Math.Max(maximum, Math.Abs((double)original.Evaluate(time) - candidate.Evaluate(time)));
            }
        }
        return maximum;
    }

    // Optimized keys are a subset of original keys, so every original interval is
    // contained in one candidate segment. Cubic difference extrema are analytic.
    static double MaximumError(AnimationCurve original, AnimationCurve candidate)
    {
        var a = original.keys; var b = candidate.keys;
        if (a.Length == 0 || b.Length == 0) return a.Length == b.Length ? 0 : double.PositiveInfinity;
        if (a[0].time != b[0].time || a[a.Length - 1].time != b[b.Length - 1].time) return double.PositiveInfinity;
        double maximum = 0; int next = 1;
        for (int i = 0; i + 1 < a.Length; i++)
        {
            while (next < b.Length - 1 && b[next].time <= a[i].time) next++;
            var start = b[next - 1]; var end = b[next];
            double slope = ((double)end.value - start.value) / ((double)end.time - start.time);
            double atStart = start.value + slope * ((double)a[i].time - start.time);
            maximum = Math.Max(maximum, SegmentError(a[i], a[i + 1], atStart, slope, out _));
        }
        return maximum;
    }

    static double SegmentError(Keyframe start, Keyframe end, double lineAtStart, double lineSlope, out double at)
    {
        double h = (double)end.time - start.time;
        double a = 2d * start.value - 2d * end.value + h * ((double)start.outTangent + end.inTangent);
        double b = -3d * start.value + 3d * end.value - h * (2d * start.outTangent + end.inTangent);
        double c = h * (start.outTangent - lineSlope);
        double d = start.value - lineAtStart;
        double maximum = Math.Abs(d); double maximumAt = 0;
        void Check(double u)
        {
            if (u < 0 || u > 1) return;
            double error = Math.Abs(((a * u + b) * u + c) * u + d);
            if (error > maximum) { maximum = error; maximumAt = u; }
        }
        Check(1);
        if (Math.Abs(a) < 1e-30)
        {
            if (Math.Abs(b) >= 1e-30) Check(-c / (2 * b));
        }
        else
        {
            double discriminant = 4 * b * b - 12 * a * c;
            if (discriminant >= 0)
            {
                double root = Math.Sqrt(discriminant);
                Check((-2 * b + root) / (6 * a));
                Check((-2 * b - root) / (6 * a));
            }
        }
        at = maximumAt;
        return maximum;
    }
}

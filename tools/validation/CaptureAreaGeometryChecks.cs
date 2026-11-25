using System;
using System.Linq;
using UnityEngine;

// Standalone regression checks for the same geometry class used by gameplay,
// bots, editor outlines and runtime rendering. No Unity native calls required.
public static class CaptureAreaGeometryChecks
{
    public static void Main()
    {
        Check("legacy rectangle", new[] { new Rect(-5, -3.5f, 10, 7) }, 70, 34, 4);
        Check("L corridor", new[] { new Rect(0, 0, 2, 1), new Rect(0, 1, 1, 1) }, 3, 8, 6);
        Check("overlapping blocks", new[] { new Rect(0, 0, 2, 2), new Rect(1, 0, 2, 2) }, 6, 10, 4);
        Check("duplicate blocks", new[] { new Rect(0, 0, 2, 2), new Rect(0, 0, 2, 2) }, 4, 8, 4);
        Check("small narrow connector", new[] { new Rect(-1, 0, 1, 1), new Rect(0, .25f, .1f, .5f), new Rect(.1f, 0, 1, 1) }, 2.05f, 7.2f, 12);
        Check("enclosed hole", new[] { new Rect(0, 0, 3, 1), new Rect(0, 2, 3, 1), new Rect(0, 1, 1, 1), new Rect(2, 1, 1, 1) }, 8, 16, 8);
        Check("floating-point shared edge", new[] { new Rect(0, 0, 1, 1), new Rect(1.00002f, 0, .99998f, 1) }, 2, 6, 4);
        Check("corner touching", new[] { new Rect(0, 0, 1, 1), new Rect(1, 1, 1, 1) }, 2, 8, 8);
        var empty = new CaptureAreaGeometry(Array.Empty<Rect>());
        Require(empty.IsEmpty && !empty.Contains(Vector2.zero), "empty area");
        // Actual six user-authored blocks relative to CapturePoint_4 (17,-4).
        var authored = new[] {
            Rect.MinMaxRect(-6.25f, .25f, 6, 3.5f),
            Rect.MinMaxRect(-.25f, -2.75f, 6, .25f),
            Rect.MinMaxRect(-.25f, -3.25f, 2, -2.75f),
            Rect.MinMaxRect(-1.5f, -5.25f, 3.5f, -3.25f),
            Rect.MinMaxRect(6, 2, 6.5f, 3.5f),
            Rect.MinMaxRect(6.5f, .75f, 9.5f, 3.5f),
        };
        Check("actual point 4 draft", authored, 78.6875f);
        Require(!new CaptureAreaGeometry(authored).Contains(new Vector2(8, -3)), "outside corridor notch must not capture");
        Console.WriteLine("PASS all capture area regression checks");
    }

    static void Check(string name, Rect[] regions, float expectedArea, float perimeter = -1, int edges = -1)
    {
        var geometry = new CaptureAreaGeometry(regions);
        Require(Math.Abs(geometry.area - expectedArea) < .002f, name + " area: " + geometry.area);
        if (edges >= 0) Require(geometry.outline.Length == edges, name + " edge count: " + geometry.outline.Length);
        if (perimeter >= 0) Require(Math.Abs(geometry.outline.Sum(e => Vector2.Distance(e.a, e.b)) - perimeter) < .002f, name + " perimeter");
        var rng = new System.Random(709);
        int count = 0;
        for (int i = 0; i < 10000; i++)
        {
            var bounds = geometry.bounds;
            var point = new Vector2(bounds.xMin - .1f + (float)rng.NextDouble() * (bounds.width + .2f),
                bounds.yMin - .1f + (float)rng.NextDouble() * (bounds.height + .2f));
            // Exclude the sub-millimeter snap tolerance from independent comparisons.
            if (regions.Any(r => Math.Abs(point.x - r.xMin) < .001f || Math.Abs(point.x - r.xMax) < .001f
                || Math.Abs(point.y - r.yMin) < .001f || Math.Abs(point.y - r.yMax) < .001f)) continue;
            bool expected = regions.Any(r => r.Contains(point));
            Require(expected == geometry.Contains(point), name + " footprint mismatch");
            var sample = geometry.Sample((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
            Require(geometry.Contains(sample), name + " bot sample outside area");
            count++;
        }
        foreach (var edge in geometry.outline)
        {
            var midpoint = (edge.a + edge.b) * .5f;
            var delta = (edge.b - edge.a).normalized;
            var normal = new Vector2(-delta.y, delta.x) * .002f;
            Require(geometry.Contains(midpoint + normal) != geometry.Contains(midpoint - normal), name + " internal/missing edge");
        }
        Console.WriteLine($"PASS {name}: area {geometry.area}, {geometry.outline.Length} outer edges, {count} independent occupancy/sample checks");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

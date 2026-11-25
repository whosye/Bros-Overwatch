using System;
using System.Collections.Generic;
using UnityEngine;

// Exact union of axis-aligned rectangles in capture-point X/Z space. Both the
// visible outline and occupancy use the same non-overlapping cells.
public sealed class CaptureAreaGeometry
{
    public readonly struct Edge
    {
        public readonly Vector2 a, b;
        public Edge(Vector2 a, Vector2 b) { this.a = a; this.b = b; }
    }

    public readonly Rect[] cells;
    public readonly Edge[] outline;
    public readonly Rect bounds;
    public readonly float area;
    public bool IsEmpty => cells.Length == 0;

    public CaptureAreaGeometry(IReadOnlyList<Rect> regions)
    {
        var rects = new List<Rect>();
        var xs = new List<float>();
        var zs = new List<float>();
        for (int i = 0; regions != null && i < regions.Count; i++)
        {
            var r = regions[i];
            // Authoring gizmos leave floating-point noise at supposedly shared edges.
            float x0 = Snap(Mathf.Min(r.x, r.x + r.width)), x1 = Snap(Mathf.Max(r.x, r.x + r.width));
            float z0 = Snap(Mathf.Min(r.y, r.y + r.height)), z1 = Snap(Mathf.Max(r.y, r.y + r.height));
            if (!Finite(x0) || !Finite(x1) || !Finite(z0) || !Finite(z1) || x1 - x0 < .001f || z1 - z0 < .001f)
                continue;
            rects.Add(Rect.MinMaxRect(x0, z0, x1, z1));
            xs.Add(x0); xs.Add(x1); zs.Add(z0); zs.Add(z1);
        }
        xs.Sort(); zs.Sort();
        Unique(xs); Unique(zs);
        if (rects.Count == 0)
        {
            cells = Array.Empty<Rect>(); outline = Array.Empty<Edge>(); bounds = default; area = 0;
            return;
        }

        int nx = xs.Count - 1, nz = zs.Count - 1;
        var covered = new bool[nx, nz];
        var tiles = new List<Rect>();
        float surface = 0;
        for (int x = 0; x < nx; x++)
        for (int z = 0; z < nz; z++)
        {
            var middle = new Vector2((xs[x] + xs[x + 1]) * .5f, (zs[z] + zs[z + 1]) * .5f);
            bool inside = rects.Exists(r => r.Contains(middle));
            covered[x, z] = inside;
            if (!inside) continue;
            var cell = Rect.MinMaxRect(xs[x], zs[z], xs[x + 1], zs[z + 1]);
            tiles.Add(cell);
            surface += cell.width * cell.height;
        }

        // Merge collinear boundary edges, keeping the occupied side consistent.
        var edges = new List<Edge>();
        for (int x = 0; x <= nx; x++)
        {
            int start = -1, side = 0;
            for (int z = 0; z <= nz; z++)
            {
                int next = z == nz ? 0 : ((x > 0 && covered[x - 1, z] ? 1 : 0) - (x < nx && covered[x, z] ? 1 : 0));
                if (next == side) continue;
                if (side != 0) edges.Add(new Edge(new Vector2(xs[x], zs[start]), new Vector2(xs[x], zs[z])));
                start = z; side = next;
            }
        }
        for (int z = 0; z <= nz; z++)
        {
            int start = -1, side = 0;
            for (int x = 0; x <= nx; x++)
            {
                int next = x == nx ? 0 : ((z > 0 && covered[x, z - 1] ? 1 : 0) - (z < nz && covered[x, z] ? 1 : 0));
                if (next == side) continue;
                if (side != 0) edges.Add(new Edge(new Vector2(xs[start], zs[z]), new Vector2(xs[x], zs[z])));
                start = x; side = next;
            }
        }
        cells = tiles.ToArray(); outline = edges.ToArray(); area = surface;
        bounds = Rect.MinMaxRect(xs[0], zs[0], xs[xs.Count - 1], zs[zs.Count - 1]);
    }

    public bool Contains(Vector2 point)
    {
        foreach (var cell in cells)
            if (point.x >= cell.xMin && point.x <= cell.xMax && point.y >= cell.yMin && point.y <= cell.yMax)
                return true;
        return false;
    }

    // Area-weighted sampling gives bots destinations inside the actual corridor,
    // including branches away from the original marker's center.
    public Vector2 Sample(float select, float u, float v)
    {
        if (IsEmpty) return Vector2.zero;
        float remaining = Mathf.Clamp01(select) * area;
        var chosen = cells[cells.Length - 1];
        foreach (var cell in cells)
        {
            remaining -= cell.width * cell.height;
            if (remaining <= 0) { chosen = cell; break; }
        }
        return new Vector2(Mathf.Lerp(chosen.xMin, chosen.xMax, Mathf.Clamp01(u)),
            Mathf.Lerp(chosen.yMin, chosen.yMax, Mathf.Clamp01(v)));
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static float Snap(float value) => (float)(Math.Round(value * 1000.0) / 1000.0);
    static void Unique(List<float> values)
    {
        for (int i = values.Count - 1; i > 0; i--)
            if (values[i] == values[i - 1]) values.RemoveAt(i);
    }
}

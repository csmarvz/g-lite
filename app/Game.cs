using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

abstract class Game
{
    public const int W = 15, H = 5;
    public int Score;
    public bool Over;
    public abstract int Interval { get; }
    public abstract string Help { get; }
    public abstract void Step();
    public abstract void Key(Keys k);
    public abstract int At(int y, int x);
}

class Blocks : Game
{
    static readonly int[][] Shapes =
    {
        new[] { 0, 0, 1, 0, 2, 0, 3, 0 },
        new[] { 0, 0, 1, 0, 0, 1, 1, 1 },
        new[] { 0, 0, 1, 0, 2, 0, 1, 1 },
        new[] { 1, 0, 2, 0, 0, 1, 1, 1 },
        new[] { 0, 0, 1, 0, 1, 1, 2, 1 },
        new[] { 0, 0, 0, 1, 1, 1, 2, 1 },
        new[] { 2, 0, 0, 1, 1, 1, 2, 1 },
    };
    static readonly int[] Colors = { 0xFF3B3B, 0xFF9A1F, 0xFFE03B, 0x3BE06A, 0x3BD5FF, 0x4A6BFF, 0xC04AFF };
    public readonly int[,] Cells = new int[H, W];
    readonly Random rng = new Random();
    int[] piece;
    int px, py, color;

    public Blocks() { Spawn(); }

    public override int Interval { get { return Math.Max(120, 650 - Score * 30); } }
    public override string Help { get { return "↑↓ move   ← rotate   → down   Space drop   Esc stop   ·   lines " + Score; } }

    public override void Key(Keys k)
    {
        if (k == Keys.Up) Move(-1);
        if (k == Keys.Down) Move(1);
        if (k == Keys.Left) Rotate();
        if (k == Keys.Right) Fall();
        if (k == Keys.Space) while (Fall()) { }
    }

    public override void Step() { Fall(); }

    bool Fits(int[] p, int dx, int dy)
    {
        for (int i = 0; i < 4; i++)
        {
            int x = p[2 * i] + dx, y = p[2 * i + 1] + dy;
            if (x < 0 || x >= W || y < 0 || y >= H || Cells[y, x] != 0) return false;
        }
        return true;
    }

    void Spawn()
    {
        int k = rng.Next(Shapes.Length);
        piece = Shapes[k]; color = Colors[k]; px = 0; py = rng.Next(H - 1);
        if (!Fits(piece, px, py)) Over = true;
    }

    public void Move(int dy) { if (!Over && Fits(piece, px, py + dy)) py += dy; }

    public void Rotate()
    {
        if (Over) return;
        var r = new int[8];
        for (int i = 0; i < 4; i++) { r[2 * i] = piece[2 * i + 1]; r[2 * i + 1] = 3 - piece[2 * i]; }
        int mx = 9, my = 9;
        for (int i = 0; i < 4; i++) { mx = Math.Min(mx, r[2 * i]); my = Math.Min(my, r[2 * i + 1]); }
        for (int i = 0; i < 4; i++) { r[2 * i] -= mx; r[2 * i + 1] -= my; }
        foreach (var kick in new[] { 0, -1, 1, -2, 2 })
            if (Fits(r, px, py + kick)) { piece = r; py += kick; return; }
    }

    bool Fall()
    {
        if (Over) return false;
        if (Fits(piece, px + 1, py)) { px++; return true; }
        for (int i = 0; i < 4; i++) Cells[piece[2 * i + 1] + py, piece[2 * i] + px] = color;
        ClearFull();
        Spawn();
        return false;
    }

    public void ClearFull()
    {
        for (int x = W - 1; x >= 0; x--)
        {
            bool full = true;
            for (int y = 0; y < H; y++) full &= Cells[y, x] != 0;
            if (!full) continue;
            for (int c = x; c > 0; c--) for (int y = 0; y < H; y++) Cells[y, c] = Cells[y, c - 1];
            for (int y = 0; y < H; y++) Cells[y, 0] = 0;
            Score++;
            x++;
        }
    }

    public override int At(int y, int x)
    {
        if (!Over)
            for (int i = 0; i < 4; i++) if (piece[2 * i] + px == x && piece[2 * i + 1] + py == y) return color;
        return Cells[y, x];
    }
}

class Snake : Game
{
    readonly List<Point> body = new List<Point> { new Point(4, 2), new Point(3, 2), new Point(2, 2) };
    readonly Random rng = new Random();
    Point dir = new Point(1, 0), next = new Point(1, 0), food;

    public Snake() { Feed(); }

    public override int Interval { get { return Math.Max(110, 320 - Score * 12); } }
    public override string Help { get { return "arrows steer   Esc stop   ·   length " + body.Count; } }

    void Feed()
    {
        var free = new List<Point>();
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (!body.Contains(new Point(x, y))) free.Add(new Point(x, y));
        if (free.Count == 0) Over = true; else food = free[rng.Next(free.Count)];
    }

    public override void Key(Keys k)
    {
        var d = k == Keys.Up ? new Point(0, -1) : k == Keys.Down ? new Point(0, 1) : k == Keys.Left ? new Point(-1, 0) : k == Keys.Right ? new Point(1, 0) : dir;
        if (d.X != -dir.X || d.Y != -dir.Y) next = d;
    }

    public override void Step()
    {
        if (Over) return;
        dir = next;
        var head = new Point(body[0].X + dir.X, body[0].Y + dir.Y);
        bool eat = head == food;
        if (!eat) body.RemoveAt(body.Count - 1);
        if (head.X < 0 || head.X >= W || head.Y < 0 || head.Y >= H || body.Contains(head)) { Over = true; return; }
        body.Insert(0, head);
        if (eat) { Score++; Feed(); }
    }

    public override int At(int y, int x)
    {
        var p = new Point(x, y);
        if (body[0] == p) return 0xB8FF6A;
        if (body.Contains(p)) return 0x2FBF4A;
        return food == p && !Over ? 0xFF3030 : 0;
    }
}

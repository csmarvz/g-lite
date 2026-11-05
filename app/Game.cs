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

abstract class KeyGame : Game
{
    protected readonly int[,] keyAt = new int[H, W];
    protected readonly List<int> board = new List<int>();

    protected KeyGame(List<Point>[] cells)
    {
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) keyAt[y, x] = -1;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == null) continue;
            board.Add(i);
            foreach (var p in cells[i]) keyAt[p.Y, p.X] = i;
        }
    }

    protected Point Move(Point p, int dx, int dy, bool wrap)
    {
        int from = keyAt[p.Y, p.X], x = p.X, y = p.Y;
        for (int i = 0; i < W * H; i++)
        {
            x += dx;
            y += dy;
            if (wrap) { x = (x + W) % W; y = (y + H) % H; }
            else if (x < 0 || x >= W || y < 0 || y >= H) return p;
            if (keyAt[y, x] >= 0 && keyAt[y, x] != from) return new Point(x, y);
        }
        return p;
    }
}

class Snake : KeyGame
{
    readonly List<int> body = new List<int>();
    readonly Random rng = new Random();
    Point head = new Point(4, 2), dir = new Point(1, 0), next = new Point(1, 0);
    int food;

    public Snake(List<Point>[] cells) : base(cells)
    {
        for (int x = 4; x >= 2; x--) body.Add(keyAt[2, x]);
        Feed();
    }

    public override int Interval { get { return Math.Max(110, 320 - Score * 12); } }
    public override string Help { get { return "arrows steer   Esc stop   ·   length " + body.Count; } }

    void Feed()
    {
        var free = new List<int>();
        foreach (int k in board) if (!body.Contains(k)) free.Add(k);
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
        head = Move(head, dir.X, dir.Y, true);
        int k = keyAt[head.Y, head.X];
        bool eat = k == food;
        if (!eat) body.RemoveAt(body.Count - 1);
        if (body.Contains(k)) { Over = true; return; }
        body.Insert(0, k);
        if (eat) { Score++; Feed(); }
    }

    public override int At(int y, int x)
    {
        int k = keyAt[y, x];
        if (k < 0) return 0;
        if (body[0] == k) return 0xB8FF6A;
        if (body.Contains(k)) return 0x2FBF4A;
        return food == k && !Over ? 0xFF3030 : 0;
    }
}

class Mines : KeyGame
{
    const int Count = 10;
    public static readonly int[] LegendColors = { 0x707070, 0x3B7BFF, 0x3BE06A, 0xFFE03B, 0xC04AFF, 0xFF9A1F };
    public static readonly string[] LegendNames = { "0", "1", "2", "3", "4+", "flag" };
    public readonly bool[] Mine;
    readonly int[] state;
    readonly List<int>[] near;
    readonly Random rng = new Random();
    public bool Placed;
    Point cur = new Point(W / 2, H / 2);
    int flags;
    bool blink;

    public Mines(RectangleF[] keys, List<Point>[] cells) : base(cells)
    {
        Mine = new bool[keys.Length];
        state = new int[keys.Length];
        near = new List<int>[keys.Length];
        for (int i = 0; i < keys.Length; i++) near[i] = new List<int>();
        foreach (int i in board)
        {
            var a = keys[i];
            a.Inflate(5, 5);
            foreach (int j in board) if (j != i && a.IntersectsWith(keys[j])) near[i].Add(j);
        }
    }

    public override int Interval { get { return 400; } }
    public override string Help { get { return "arrows move   Space open   F flag   Esc stop   ·   mines " + (Count - flags); } }

    public override void Step() { blink = !blink; }

    public override void Key(Keys k)
    {
        if (Over) return;
        if (k == Keys.Left) cur = Move(cur, -1, 0, false);
        if (k == Keys.Right) cur = Move(cur, 1, 0, false);
        if (k == Keys.Up) cur = Move(cur, 0, -1, false);
        if (k == Keys.Down) cur = Move(cur, 0, 1, false);
        int c = keyAt[cur.Y, cur.X];
        if (k == Keys.Space) Open(c);
        if (k == Keys.F && state[c] != 1) { state[c] = 2 - state[c]; flags += state[c] == 2 ? 1 : -1; }
        blink = true;
    }

    void Place(int first)
    {
        Placed = true;
        for (int n = 0; n < Count; )
        {
            int k = board[rng.Next(board.Count)];
            if (Mine[k] || k == first || near[first].Contains(k)) continue;
            Mine[k] = true;
            n++;
        }
    }

    public int Near(int k)
    {
        int n = 0;
        foreach (int j in near[k]) if (Mine[j]) n++;
        return n;
    }

    public void Open(int k)
    {
        if (state[k] != 0) return;
        if (!Placed) Place(k);
        if (Mine[k]) { Over = true; return; }
        var todo = new Stack<int>();
        todo.Push(k);
        while (todo.Count > 0)
        {
            int i = todo.Pop();
            if (state[i] != 0) continue;
            state[i] = 1;
            Score++;
            if (Near(i) == 0) foreach (int j in near[i]) if (state[j] == 0 && !Mine[j]) todo.Push(j);
        }
        bool left = false;
        foreach (int i in board) left |= !Mine[i] && state[i] != 1;
        if (!left) Over = true;
    }

    public bool Opened(int k) { return state[k] == 1; }

    public override int At(int y, int x)
    {
        int k = keyAt[y, x];
        if (k < 0) return 0;
        if (blink && !Over && k == keyAt[cur.Y, cur.X]) return 0xFFFFFF;
        if (Over && Mine[k] && state[k] != 2) return 0xFF3030;
        if (state[k] == 2) return LegendColors[5];
        if (state[k] != 1) return 0;
        return LegendColors[Math.Min(Near(k), 4)];
    }
}

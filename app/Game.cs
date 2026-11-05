using System;

class Blocks
{
    public const int W = 15, H = 5;
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
    public int Lines;
    public bool Over;

    public Blocks() { Spawn(); }

    public int Interval { get { return Math.Max(120, 650 - Lines * 30); } }

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

    public bool Step()
    {
        if (Over) return false;
        if (Fits(piece, px + 1, py)) { px++; return true; }
        for (int i = 0; i < 4; i++) Cells[piece[2 * i + 1] + py, piece[2 * i] + px] = color;
        ClearFull();
        Spawn();
        return false;
    }

    public void Drop() { while (Step()) { } }

    public void ClearFull()
    {
        for (int x = W - 1; x >= 0; x--)
        {
            bool full = true;
            for (int y = 0; y < H; y++) full &= Cells[y, x] != 0;
            if (!full) continue;
            for (int c = x; c > 0; c--) for (int y = 0; y < H; y++) Cells[y, c] = Cells[y, c - 1];
            for (int y = 0; y < H; y++) Cells[y, 0] = 0;
            Lines++;
            x++;
        }
    }

    public int At(int y, int x)
    {
        if (!Over)
            for (int i = 0; i < 4; i++) if (piece[2 * i] + px == x && piece[2 * i + 1] + py == y) return color;
        return Cells[y, x];
    }
}

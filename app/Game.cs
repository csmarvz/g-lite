using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

abstract class Game
{
    public const int Columns = 15, Rows = 5;
    public int Score;
    public bool Over;
    public abstract int Interval { get; }
    public abstract string Help { get; }
    public abstract void Step();
    public abstract void Key(Keys key);
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
    public readonly int[,] Cells = new int[Rows, Columns];
    readonly Random random = new Random();
    int[] piece;
    int pieceX, pieceY, color;

    public Blocks() { Spawn(); }

    public override int Interval { get { return Math.Max(120, 650 - Score * 30); } }
    public override string Help { get { return "↑↓ move   ← rotate   → down   Space drop   Esc stop      lines " + Score; } }

    public override void Key(Keys key)
    {
        if (key == Keys.Up) Move(-1);
        if (key == Keys.Down) Move(1);
        if (key == Keys.Left) Rotate();
        if (key == Keys.Right) Fall();
        if (key == Keys.Space) while (Fall()) { }
    }

    public override void Step() { Fall(); }

    bool Fits(int[] shape, int dx, int dy)
    {
        for (int i = 0; i < 4; i++)
        {
            int x = shape[2 * i] + dx, y = shape[2 * i + 1] + dy;
            if (x < 0 || x >= Columns || y < 0 || y >= Rows || Cells[y, x] != 0) return false;
        }
        return true;
    }

    void Spawn()
    {
        int shape = random.Next(Shapes.Length);
        piece = Shapes[shape]; color = Colors[shape]; pieceX = 0; pieceY = random.Next(Rows - 1);
        if (!Fits(piece, pieceX, pieceY)) Over = true;
    }

    public void Move(int dy) { if (!Over && Fits(piece, pieceX, pieceY + dy)) pieceY += dy; }

    public void Rotate()
    {
        if (Over) return;
        var rotated = new int[8];
        for (int i = 0; i < 4; i++) { rotated[2 * i] = piece[2 * i + 1]; rotated[2 * i + 1] = 3 - piece[2 * i]; }
        int minX = 9, minY = 9;
        for (int i = 0; i < 4; i++) { minX = Math.Min(minX, rotated[2 * i]); minY = Math.Min(minY, rotated[2 * i + 1]); }
        for (int i = 0; i < 4; i++) { rotated[2 * i] -= minX; rotated[2 * i + 1] -= minY; }
        foreach (var kick in new[] { 0, -1, 1, -2, 2 })
            if (Fits(rotated, pieceX, pieceY + kick)) { piece = rotated; pieceY += kick; return; }
    }

    bool Fall()
    {
        if (Over) return false;
        if (Fits(piece, pieceX + 1, pieceY)) { pieceX++; return true; }
        for (int i = 0; i < 4; i++) Cells[piece[2 * i + 1] + pieceY, piece[2 * i] + pieceX] = color;
        ClearFull();
        Spawn();
        return false;
    }

    public void ClearFull()
    {
        for (int x = Columns - 1; x >= 0; x--)
        {
            bool full = true;
            for (int y = 0; y < Rows; y++) full &= Cells[y, x] != 0;
            if (!full) continue;
            for (int column = x; column > 0; column--) for (int y = 0; y < Rows; y++) Cells[y, column] = Cells[y, column - 1];
            for (int y = 0; y < Rows; y++) Cells[y, 0] = 0;
            Score++;
            x++;
        }
    }

    public override int At(int y, int x)
    {
        if (!Over)
            for (int i = 0; i < 4; i++) if (piece[2 * i] + pieceX == x && piece[2 * i + 1] + pieceY == y) return color;
        return Cells[y, x];
    }
}

abstract class KeyGame : Game
{
    protected readonly int[,] keyAt = new int[Rows, Columns];
    protected readonly List<int> board = new List<int>();

    protected KeyGame(List<Point>[] cells)
    {
        for (int y = 0; y < Rows; y++) for (int x = 0; x < Columns; x++) keyAt[y, x] = -1;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == null) continue;
            board.Add(i);
            foreach (var cell in cells[i]) keyAt[cell.Y, cell.X] = i;
        }
    }

    protected Point Move(Point start, int dx, int dy, bool wrap)
    {
        int from = keyAt[start.Y, start.X], x = start.X, y = start.Y;
        for (int i = 0; i < Columns * Rows; i++)
        {
            x += dx;
            y += dy;
            if (wrap) { x = (x + Columns) % Columns; y = (y + Rows) % Rows; }
            else if (x < 0 || x >= Columns || y < 0 || y >= Rows) return start;
            if (keyAt[y, x] >= 0 && keyAt[y, x] != from) return new Point(x, y);
        }
        return start;
    }
}

class Snake : KeyGame
{
    readonly List<int> body = new List<int>();
    readonly Random random = new Random();
    Point head = new Point(4, 2), direction = new Point(1, 0), next = new Point(1, 0);
    int food;

    public Snake(List<Point>[] cells) : base(cells)
    {
        for (int x = 4; x >= 2; x--) body.Add(keyAt[2, x]);
        Feed();
    }

    public override int Interval { get { return Math.Max(110, 320 - Score * 12); } }
    public override string Help { get { return "arrows steer   Esc stop      length " + body.Count; } }

    void Feed()
    {
        var free = new List<int>();
        foreach (int key in board) if (!body.Contains(key)) free.Add(key);
        if (free.Count == 0) Over = true; else food = free[random.Next(free.Count)];
    }

    public override void Key(Keys key)
    {
        var wanted = key == Keys.Up ? new Point(0, -1)
            : key == Keys.Down ? new Point(0, 1)
            : key == Keys.Left ? new Point(-1, 0)
            : key == Keys.Right ? new Point(1, 0)
            : direction;
        if (wanted.X != -direction.X || wanted.Y != -direction.Y) next = wanted;
    }

    public override void Step()
    {
        if (Over) return;
        direction = next;
        head = Move(head, direction.X, direction.Y, true);
        int key = keyAt[head.Y, head.X];
        bool eat = key == food;
        if (!eat) body.RemoveAt(body.Count - 1);
        if (body.Contains(key)) { Over = true; return; }
        body.Insert(0, key);
        if (eat) { Score++; Feed(); }
    }

    public override int At(int y, int x)
    {
        int key = keyAt[y, x];
        if (key < 0) return 0;
        if (body[0] == key) return 0xB8FF6A;
        if (body.Contains(key)) return 0x2FBF4A;
        return food == key && !Over ? 0xFF3030 : 0;
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
    readonly Random random = new Random();
    public bool Placed;
    Point cursor = new Point(Columns / 2, Rows / 2);
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
            var area = keys[i];
            area.Inflate(5, 5);
            foreach (int j in board) if (j != i && area.IntersectsWith(keys[j])) near[i].Add(j);
        }
    }

    public override int Interval { get { return 400; } }
    public override string Help { get { return "arrows move   Space open   F flag   Esc stop      mines " + (Count - flags); } }

    public override void Step() { blink = !blink; }

    public override void Key(Keys key)
    {
        if (Over) return;
        if (key == Keys.Left) cursor = Move(cursor, -1, 0, false);
        if (key == Keys.Right) cursor = Move(cursor, 1, 0, false);
        if (key == Keys.Up) cursor = Move(cursor, 0, -1, false);
        if (key == Keys.Down) cursor = Move(cursor, 0, 1, false);
        int selected = keyAt[cursor.Y, cursor.X];
        if (key == Keys.Space) Open(selected);
        if (key == Keys.F && state[selected] != 1) { state[selected] = 2 - state[selected]; flags += state[selected] == 2 ? 1 : -1; }
        blink = true;
    }

    void Place(int first)
    {
        Placed = true;
        for (int minesPlaced = 0; minesPlaced < Count; )
        {
            int key = board[random.Next(board.Count)];
            if (Mine[key] || key == first || near[first].Contains(key)) continue;
            Mine[key] = true;
            minesPlaced++;
        }
    }

    public int MinesAround(int key)
    {
        int mines = 0;
        foreach (int j in near[key]) if (Mine[j]) mines++;
        return mines;
    }

    public void Open(int key)
    {
        if (state[key] != 0) return;
        if (!Placed) Place(key);
        if (Mine[key]) { Over = true; return; }
        var todo = new Stack<int>();
        todo.Push(key);
        while (todo.Count > 0)
        {
            int i = todo.Pop();
            if (state[i] != 0) continue;
            state[i] = 1;
            Score++;
            if (MinesAround(i) == 0) foreach (int j in near[i]) if (state[j] == 0 && !Mine[j]) todo.Push(j);
        }
        bool left = false;
        foreach (int i in board) left |= !Mine[i] && state[i] != 1;
        if (!left) Over = true;
    }

    public bool Opened(int key) { return state[key] == 1; }

    public override int At(int y, int x)
    {
        int key = keyAt[y, x];
        if (key < 0) return 0;
        if (blink && !Over && key == keyAt[cursor.Y, cursor.X]) return 0xFFFFFF;
        if (Over && Mine[key] && state[key] != 2) return 0xFF3030;
        if (state[key] == 2) return LegendColors[5];
        if (state[key] != 1) return 0;
        return LegendColors[Math.Min(MinesAround(key), 4)];
    }
}

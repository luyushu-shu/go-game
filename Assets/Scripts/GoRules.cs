using System.Collections.Generic;

public enum GoPhase { Play, Scoring, Over }

// 围棋规则引擎：提子、打劫（全局同形禁着）、自杀拦截、双停数子。
// 纯 C# 实现，不依赖 Unity，便于单测与 AI 推演。
public class GoRules
{
    public const int EMPTY = 0, BLACK = 1, WHITE = 2;

    // 贴目数，可在开局设置中修改
    public float Komi = 7.5f;

    public int Size { get; private set; }
    public int[] Board { get; private set; }
    public int Turn { get; private set; }
    public int Passes { get; private set; }
    public int MoveCount { get; private set; }
    public int LastMove { get; private set; }
    public GoPhase Phase { get; private set; }
    public string Result { get; private set; }
    public readonly int[] Captures = new int[3];
    public readonly HashSet<int> Dead = new HashSet<int>();

    struct Snapshot
    {
        public int[] board; public int turn; public int capB; public int capW;
        public int passes; public int moveCount; public int lastMove; public long hashAfter;
    }
    readonly List<Snapshot> history = new List<Snapshot>();
    readonly HashSet<long> posHashes = new HashSet<long>();

    // Zobrist 哈希表：打劫判定与 AI 搜索共用
    public static readonly long[] Zob = new long[19 * 19 * 3];
    static GoRules()
    {
        var r = new System.Random(20260916);
        for (int i = 0; i < Zob.Length; i++)
            Zob[i] = ((long)r.Next() << 32) ^ (uint)r.Next();
    }

    public GoRules(int size) { Reset(size); }

    public void Reset(int size)
    {
        Size = size;
        Board = new int[size * size];
        Turn = BLACK; Passes = 0; MoveCount = 0; LastMove = -1;
        Captures[BLACK] = Captures[WHITE] = 0;
        Phase = GoPhase.Play; Result = null; Dead.Clear();
        history.Clear(); posHashes.Clear();
        posHashes.Add(Hash(Board));
    }

    // 让子局：预置黑子，白方先下。
    public void SetupHandicap(int[] points)
    {
        foreach (int p in points)
            if (p >= 0 && p < Board.Length) Board[p] = BLACK;
        Turn = WHITE;
        LastMove = -1;
        posHashes.Clear();
        posHashes.Add(Hash(Board));
    }

    public int Idx(int x, int y) { return y * Size + x; }
    public static int Opp(int c) { return c == BLACK ? WHITE : BLACK; }

    public IEnumerable<int> Neighbors(int i)
    {
        int x = i % Size, y = i / Size;
        if (x > 0) yield return i - 1;
        if (x < Size - 1) yield return i + 1;
        if (y > 0) yield return i - Size;
        if (y < Size - 1) yield return i + Size;
    }

    public void GroupOn(int[] b, int start, List<int> stones, HashSet<int> libs)
    {
        stones.Clear(); libs.Clear();
        int color = b[start];
        var seen = new HashSet<int> { start };
        var stack = new Stack<int>(); stack.Push(start);
        while (stack.Count > 0)
        {
            int s = stack.Pop(); stones.Add(s);
            foreach (int n in Neighbors(s))
            {
                if (b[n] == EMPTY) libs.Add(n);
                else if (b[n] == color && !seen.Contains(n)) { seen.Add(n); stack.Push(n); }
            }
        }
    }

    public long Hash(int[] b)
    {
        long h = 0;
        for (int i = 0; i < b.Length; i++)
            if (b[i] != EMPTY) h ^= Zob[i * 3 + b[i]];
        return h;
    }

    public bool RepeatsPosition(int[] b) { return posHashes.Contains(Hash(b)); }

    // 导出局面哈希（供 AI 搜索做打劫判定）
    public HashSet<long> ExportHashes() { return new HashSet<long>(posHashes); }

    // 模拟落子：返回新盘面与被提子列表；自杀/非空点返回 false。
    public bool Simulate(int[] b, int x, int y, int color, out int[] newBoard, out List<int> captured)
    {
        newBoard = null; captured = null;
        if (x < 0 || y < 0 || x >= Size || y >= Size) return false;
        int i = Idx(x, y);
        if (b[i] != EMPTY) return false;
        var nb = (int[])b.Clone();
        nb[i] = color;
        captured = new List<int>();
        var stones = new List<int>(); var libs = new HashSet<int>();
        foreach (int n in Neighbors(i))
        {
            if (nb[n] == Opp(color))
            {
                GroupOn(nb, n, stones, libs);
                if (libs.Count == 0)
                    foreach (int s in stones)
                        if (nb[s] != EMPTY) { nb[s] = EMPTY; captured.Add(s); }
            }
        }
        GroupOn(nb, i, stones, libs);
        if (libs.Count == 0) return false;
        newBoard = nb;
        return true;
    }

    public bool Play(int x, int y, out string msg)
    {
        msg = null;
        if (Phase != GoPhase.Play || Result != null) { msg = "对局未在进行中"; return false; }
        if (!Simulate(Board, x, y, Turn, out int[] nb, out List<int> cap)) { msg = "此处不能落子（无气或已有子）"; return false; }
        long h = Hash(nb);
        if (posHashes.Contains(h)) { msg = "打劫，此处暂不能落子"; return false; }
        history.Add(new Snapshot
        {
            board = Board, turn = Turn, capB = Captures[BLACK], capW = Captures[WHITE],
            passes = Passes, moveCount = MoveCount, lastMove = LastMove, hashAfter = h
        });
        Board = nb;
        Captures[Turn] += cap.Count;
        posHashes.Add(h);
        LastMove = Idx(x, y); Passes = 0; MoveCount++;
        Turn = Opp(Turn);
        return true;
    }

    public void Pass()
    {
        if (Phase != GoPhase.Play || Result != null) return;
        history.Add(new Snapshot
        {
            board = Board, turn = Turn, capB = Captures[BLACK], capW = Captures[WHITE],
            passes = Passes, moveCount = MoveCount, lastMove = LastMove, hashAfter = -1
        });
        Passes++; LastMove = -1; Turn = Opp(Turn);
        if (Passes >= 2) { Phase = GoPhase.Scoring; Dead.Clear(); }
    }

    public void Resign()
    {
        if (Result != null) return;
        Result = Turn == BLACK ? "白方胜 · 黑方认输" : "黑方胜 · 白方认输";
        Phase = GoPhase.Over;
    }

    public bool Undo(int maxSteps)
    {
        if (history.Count == 0) return false;
        for (int k = 0; k < maxSteps && history.Count > 0; k++)
        {
            var s = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            Board = s.board; Turn = s.turn;
            Captures[BLACK] = s.capB; Captures[WHITE] = s.capW;
            Passes = s.passes; MoveCount = s.moveCount; LastMove = s.lastMove;
        }
        Phase = GoPhase.Play; Result = null; Dead.Clear();
        posHashes.Clear();
        posHashes.Add(Hash(new int[Size * Size]));
        foreach (var e in history) if (e.hashAfter >= 0) posHashes.Add(e.hashAfter);
        return true;
    }

    public void ResumePlay()
    {
        if (Phase != GoPhase.Scoring) return;
        Phase = GoPhase.Play; Passes = 0; Dead.Clear();
    }

    public void ToggleDead(int x, int y)
    {
        if (Phase != GoPhase.Scoring) return;
        int i = Idx(x, y);
        if (Board[i] == EMPTY) return;
        var stones = new List<int>(); var libs = new HashSet<int>();
        GroupOn(Board, i, stones, libs);
        bool allDead = true;
        foreach (int s in stones) if (!Dead.Contains(s)) { allDead = false; break; }
        foreach (int s in stones) { if (allDead) Dead.Remove(s); else Dead.Add(s); }
    }

    // 数子法：盘上活子 + 单方空点，白加贴目。
    public void ComputeScore(out float blackScore, out float whiteScore,
        out int blackStones, out int blackTerr, out int whiteStones, out int whiteTerr)
    {
        var b = (int[])Board.Clone();
        foreach (int i in Dead) b[i] = EMPTY;
        blackStones = whiteStones = blackTerr = whiteTerr = 0;
        foreach (int v in b) { if (v == BLACK) blackStones++; else if (v == WHITE) whiteStones++; }
        var seen = new bool[b.Length];
        for (int i = 0; i < b.Length; i++)
        {
            if (b[i] != EMPTY || seen[i]) continue;
            var region = new List<int>();
            int border = 0;
            var stack = new Stack<int>(); stack.Push(i); seen[i] = true;
            while (stack.Count > 0)
            {
                int s = stack.Pop(); region.Add(s);
                foreach (int n in Neighbors(s))
                {
                    if (b[n] == EMPTY) { if (!seen[n]) { seen[n] = true; stack.Push(n); } }
                    else border |= b[n];
                }
            }
            if (border == BLACK) blackTerr += region.Count;
            else if (border == WHITE) whiteTerr += region.Count;
        }
        blackScore = blackStones + blackTerr;
        whiteScore = whiteStones + whiteTerr + Komi;
    }

    public void ConfirmEnd()
    {
        if (Phase != GoPhase.Scoring) return;
        ComputeScore(out float bs, out float ws, out _, out _, out _, out _);
        float diff = bs > ws ? bs - ws : ws - bs;
        Result = (bs > ws ? "黑方胜 " : "白方胜 ") + diff.ToString("0.0") + " 目";
        Phase = GoPhase.Over;
    }
}

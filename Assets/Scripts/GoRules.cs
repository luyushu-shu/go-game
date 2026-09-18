using System.Collections.Generic;

public enum GoPhase { Play, Scoring, Over }

// 局面快照，供打谱进度存档
[System.Serializable]
public class GoStateData
{
    public int size;
    public int[] board;
    public string boardCsv;  // JsonUtility 对 int[] 反序列化不稳定，用 CSV 备份
    public int turn, passes, moveCount, lastMove;
    public int capBlack, capWhite;
    public int phase;
    public string result;
    public int[] dead;
    public float komi;
    public string moveAtCsv;
    public string moveLogCsv;  // x,y,color 序列，用于恢复旧存档手数
}

// 围棋规则引擎：提子、打劫（全局同形禁着）、自杀拦截、双停数子。
// 纯 C# 实现，不依赖 Unity，便于单测与 AI 推演。
public class GoRules
{
    public const int EMPTY = 0, BLACK = 1, WHITE = 2;

    // 贴目数，可在开局设置中修改
    public float Komi = 7.5f;

    public int Size { get; private set; }
    public int[] Board { get; private set; }
    public int[] MoveAt { get; private set; }
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
        public int[] board, moveAt; public int turn; public int capB; public int capW;
        public int passes; public int moveCount; public int lastMove; public long hashAfter;
    }
    readonly List<Snapshot> history = new List<Snapshot>();
    readonly HashSet<long> posHashes = new HashSet<long>();

    struct MoveRec { public int x, y, c; }
    readonly List<MoveRec> moveLog = new List<MoveRec>();

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
        MoveAt = new int[size * size];
        Turn = BLACK; Passes = 0; MoveCount = 0; LastMove = -1;
        Captures[BLACK] = Captures[WHITE] = 0;
        Phase = GoPhase.Play; Result = null; Dead.Clear();
        history.Clear(); moveLog.Clear(); posHashes.Clear();
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

    public int LoggedMoveCount => moveLog.Count;

    public bool GetLoggedMove(int i, out int x, out int y, out bool isPass)
    {
        x = y = 0; isPass = false;
        if (i < 0 || i >= moveLog.Count) return false;
        var m = moveLog[i];
        isPass = m.x < 0;
        x = m.x; y = m.y;
        return true;
    }

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
            board = Board, moveAt = (int[])MoveAt.Clone(), turn = Turn,
            capB = Captures[BLACK], capW = Captures[WHITE],
            passes = Passes, moveCount = MoveCount, lastMove = LastMove, hashAfter = h
        });
        moveLog.Add(new MoveRec { x = x, y = y, c = Turn });
        Board = nb;
        foreach (int c in cap) MoveAt[c] = 0;
        LastMove = Idx(x, y);
        MoveAt[LastMove] = MoveCount + 1;
        Captures[Turn] += cap.Count;
        posHashes.Add(h);
        Passes = 0; MoveCount++;
        Turn = Opp(Turn);
        return true;
    }

    public void Pass()
    {
        if (Phase != GoPhase.Play || Result != null) return;
        history.Add(new Snapshot
        {
            board = Board, moveAt = (int[])MoveAt.Clone(), turn = Turn,
            capB = Captures[BLACK], capW = Captures[WHITE],
            passes = Passes, moveCount = MoveCount, lastMove = LastMove, hashAfter = -1
        });
        Passes++; LastMove = -1;
        moveLog.Add(new MoveRec { x = -1, y = -1, c = Turn });
        Turn = Opp(Turn);
        if (Passes >= 2) { Phase = GoPhase.Scoring; Dead.Clear(); }
    }

    public void Resign()
    {
        ResignBy(Turn);
    }

    public void ResignBy(int color)
    {
        if (Result != null) return;
        Result = color == BLACK ? "白中盘胜" : "黑中盘胜";
        Phase = GoPhase.Over;
    }

    public bool Undo(int maxSteps)
    {
        if (history.Count == 0) return false;
        for (int k = 0; k < maxSteps && history.Count > 0; k++)
        {
            var s = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            if (moveLog.Count > 0) moveLog.RemoveAt(moveLog.Count - 1);
            Board = s.board;
            MoveAt = s.moveAt != null ? (int[])s.moveAt.Clone() : new int[Size * Size];
            Turn = s.turn;
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

    // 形势判断：Bruno Bouzy 5/21 形态学估目（GNU Go estimate_score / Indigo 同款）。
    // 先对棋子影响力做 5 次膨胀、再 21 次腐蚀；残留正负值视为实地。
    // 5/10 得到更大的厚势（moyo）。对方棋子落在己方实地内视为被吃。
    public void EstimateSituation(
        out float blackScore, out float whiteScore,
        out int blackTerr, out int whiteTerr,
        out int blackMoyo, out int whiteMoyo,
        out int deadBlack, out int deadWhite,
        out int[] owner)
    {
        int n = Size * Size;
        var a = new int[n];
        var b = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (Board[i] == BLACK) a[i] = 128;
            else if (Board[i] == WHITE) a[i] = -128;
        }

        for (int k = 0; k < 5; k++) { BouzyDilate(a, b); Swap(ref a, ref b); }
        var afterDilate = (int[])a.Clone();
        for (int k = 0; k < 21; k++) { BouzyErode(a, b); Swap(ref a, ref b); }
        var terr = a;

        var moyo = (int[])afterDilate.Clone();
        for (int k = 0; k < 10; k++) { BouzyErode(moyo, b); Swap(ref moyo, ref b); }

        blackTerr = whiteTerr = blackMoyo = whiteMoyo = deadBlack = deadWhite = 0;
        int liveB = 0, liveW = 0;
        owner = new int[n];
        for (int i = 0; i < n; i++)
        {
            int stone = Board[i];
            if (stone == BLACK)
            {
                if (terr[i] < 0) { deadBlack++; owner[i] = WHITE; }
                else liveB++;
            }
            else if (stone == WHITE)
            {
                if (terr[i] > 0) { deadWhite++; owner[i] = BLACK; }
                else liveW++;
            }
            else
            {
                if (terr[i] > 0) { blackTerr++; owner[i] = BLACK; }
                else if (terr[i] < 0) { whiteTerr++; owner[i] = WHITE; }
                else if (moyo[i] > 0) { blackMoyo++; owner[i] = BLACK + 10; }
                else if (moyo[i] < 0) { whiteMoyo++; owner[i] = WHITE + 10; }
            }
        }
        blackMoyo = System.Math.Max(0, blackMoyo);
        whiteMoyo = System.Math.Max(0, whiteMoyo);

        blackScore = liveB + blackTerr + deadWhite;
        whiteScore = liveW + whiteTerr + deadBlack + Komi;
    }

    void BouzyDilate(int[] src, int[] dst)
    {
        for (int i = 0; i < src.Length; i++)
        {
            int pos = 0, neg = 0;
            foreach (int nb in Neighbors(i))
            {
                if (src[nb] > 0) pos++;
                else if (src[nb] < 0) neg++;
            }
            int v = src[i];
            if (v > 0) dst[i] = v + (neg == 0 ? pos : 0);
            else if (v < 0) dst[i] = v - (pos == 0 ? neg : 0);
            else
            {
                int n = 0;
                if (neg == 0) n += pos;
                if (pos == 0) n -= neg;
                dst[i] = n;
            }
        }
    }

    void BouzyErode(int[] src, int[] dst)
    {
        for (int i = 0; i < src.Length; i++)
        {
            int nonPos = 0, nonNeg = 0;
            foreach (int nb in Neighbors(i))
            {
                if (src[nb] <= 0) nonPos++;
                if (src[nb] >= 0) nonNeg++;
            }
            int v = src[i];
            if (v > 0) { v -= nonPos; if (v < 0) v = 0; }
            else if (v < 0) { v += nonNeg; if (v > 0) v = 0; }
            dst[i] = v;
        }
    }

    static void Swap(ref int[] a, ref int[] b) { var t = a; a = b; b = t; }

    public void ConfirmEnd()
    {
        if (Phase != GoPhase.Scoring) return;
        ComputeScore(out float bs, out float ws, out _, out _, out _, out _);
        float diff = bs > ws ? bs - ws : ws - bs;
        Result = (bs > ws ? "黑方胜 " : "白方胜 ") + diff.ToString("0.0") + " 目";
        Phase = GoPhase.Over;
    }

    static string BoardToCsv(int[] b)
    {
        var parts = new string[b.Length];
        for (int i = 0; i < b.Length; i++) parts[i] = b[i].ToString();
        return string.Join(",", parts);
    }

    static int[] CsvToBoard(string csv, int expectedLen)
    {
        if (string.IsNullOrEmpty(csv)) return null;
        var parts = csv.Split(',');
        if (parts.Length != expectedLen) return null;
        var b = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out b[i])) return null;
        return b;
    }

    public GoStateData ExportState()
    {
        var deadArr = new int[Dead.Count];
        Dead.CopyTo(deadArr);
        var boardCopy = (int[])Board.Clone();
        return new GoStateData
        {
            size = Size,
            board = boardCopy,
            boardCsv = BoardToCsv(boardCopy),
            turn = Turn,
            passes = Passes,
            moveCount = MoveCount,
            lastMove = LastMove,
            capBlack = Captures[BLACK],
            capWhite = Captures[WHITE],
            phase = (int)Phase,
            result = Result,
            dead = deadArr,
            komi = Komi,
            moveAtCsv = BoardToCsv(MoveAt),
            moveLogCsv = MoveLogToCsv()
        };
    }

    public string ExportMoveLogCsv() => MoveLogToCsv();

    public int CountLogMoves(string csv)
    {
        if (string.IsNullOrEmpty(csv)) return 0;
        int n = 0;
        foreach (string tok in csv.Split('|'))
            if (!string.IsNullOrEmpty(tok)) n++;
        return n;
    }

    // 从空盘（或已布让子）按棋谱复盘到第 ply 手，过程中不进入数子。
    public bool ReplayToPly(string csv, int ply)
    {
        var seq = new List<MoveRec>();
        if (!string.IsNullOrEmpty(csv))
        {
            foreach (string tok in csv.Split('|'))
            {
                if (string.IsNullOrEmpty(tok)) continue;
                var p = tok.Split(',');
                if (p.Length < 3) continue;
                if (!int.TryParse(p[0], out int x) || !int.TryParse(p[1], out int y) || !int.TryParse(p[2], out int c))
                    continue;
                seq.Add(new MoveRec { x = x, y = y, c = c });
            }
        }
        if (ply < 0) ply = 0;
        if (ply > seq.Count) ply = seq.Count;

        moveLog.Clear();
        history.Clear();
        Phase = GoPhase.Play;
        Result = null;
        Dead.Clear();

        for (int i = 0; i < ply; i++)
        {
            var m = seq[i];
            Turn = m.c == WHITE ? WHITE : BLACK;
            if (m.x < 0)
            {
                history.Add(new Snapshot
                {
                    board = Board, moveAt = (int[])MoveAt.Clone(), turn = Turn,
                    capB = Captures[BLACK], capW = Captures[WHITE],
                    passes = Passes, moveCount = MoveCount, lastMove = LastMove, hashAfter = -1
                });
                Passes++;
                LastMove = -1;
                moveLog.Add(new MoveRec { x = -1, y = -1, c = Turn });
                Turn = Opp(Turn);
                continue;
            }
            if (!Play(m.x, m.y, out _)) return false;
        }
        return true;
    }

    string MoveLogToCsv()
    {
        if (moveLog.Count == 0) return "";
        var parts = new string[moveLog.Count];
        for (int i = 0; i < moveLog.Count; i++)
            parts[i] = moveLog[i].x + "," + moveLog[i].y + "," + moveLog[i].c;
        return string.Join("|", parts);
    }

    void ImportMoveLog(string csv)
    {
        moveLog.Clear();
        if (string.IsNullOrEmpty(csv)) return;
        foreach (string tok in csv.Split('|'))
        {
            if (string.IsNullOrEmpty(tok)) continue;
            var p = tok.Split(',');
            if (p.Length < 3) continue;
            if (!int.TryParse(p[0], out int x) || !int.TryParse(p[1], out int y) || !int.TryParse(p[2], out int c))
                continue;
            moveLog.Add(new MoveRec { x = x, y = y, c = c });
        }
    }

    public bool NeedsMoveAtRebuild()
    {
        for (int i = 0; i < Board.Length; i++)
            if (Board[i] != EMPTY && MoveAt[i] <= 0) return true;
        return false;
    }

    public void RebuildMoveAtFromLog()
    {
        var ma = new int[Size * Size];
        var sim = new int[Size * Size];
        int step = 0;
        foreach (var m in moveLog)
        {
            if (m.x < 0) continue;
            if (!Simulate(sim, m.x, m.y, m.c, out int[] nb, out List<int> cap)) continue;
            step++;
            foreach (int c in cap) ma[c] = 0;
            System.Array.Copy(nb, sim, sim.Length);
            ma[Idx(m.x, m.y)] = step;
        }
        MoveAt = ma;
    }

    bool LogReproducesBoard()
    {
        if (moveLog.Count == 0) return false;
        var sim = new int[Size * Size];
        foreach (var m in moveLog)
        {
            if (m.x < 0) continue;
            if (!Simulate(sim, m.x, m.y, m.c, out int[] nb, out _)) return false;
            sim = nb;
        }
        if (sim.Length != Board.Length) return false;
        for (int i = 0; i < Board.Length; i++)
            if (sim[i] != Board[i]) return false;
        return true;
    }

    // 完整记录则按落子顺序编号；否则保留存档手数，并为缺号棋子补齐
    public void EnsureMoveAtForDisplay()
    {
        if (LogReproducesBoard()) RebuildMoveAtFromLog();
        FillMissingMoveNumbers();
    }

    void FillMissingMoveNumbers()
    {
        int stones = 0, maxAt = 0;
        var blank = new List<int>();
        var used = new HashSet<int>();
        for (int i = 0; i < Board.Length; i++)
        {
            if (Board[i] == EMPTY) { MoveAt[i] = 0; continue; }
            stones++;
            if (MoveAt[i] > 0) { used.Add(MoveAt[i]); if (MoveAt[i] > maxAt) maxAt = MoveAt[i]; }
            else blank.Add(i);
        }
        if (stones == 0) return;

        int hi = MoveCount;
        if (hi < stones) hi = stones;
        if (hi < maxAt) hi = maxAt;
        if (LastMove >= 0 && LastMove < Board.Length && Board[LastMove] != EMPTY)
        {
            used.Remove(MoveAt[LastMove]);
            MoveAt[LastMove] = hi;
            used.Add(hi);
            blank.Remove(LastMove);
        }
        MoveCount = hi;

        int next = 1;
        foreach (int i in blank)
        {
            while (used.Contains(next)) next++;
            MoveAt[i] = next;
            used.Add(next);
            next++;
        }
    }

    public bool ImportState(GoStateData d)
    {
        if (d == null || d.size <= 0) return false;
        int expected = d.size * d.size;
        int[] board = d.board;
        if (board == null || board.Length != expected)
            board = CsvToBoard(d.boardCsv, expected);
        if (board == null) return false;

        Reset(d.size);
        Komi = d.komi;
        System.Array.Copy(board, Board, expected);
        Turn = d.turn;
        Passes = d.passes;
        MoveCount = d.moveCount;
        LastMove = d.lastMove;
        Captures[BLACK] = d.capBlack;
        Captures[WHITE] = d.capWhite;
        Phase = (GoPhase)d.phase;
        Result = d.result;
        Dead.Clear();
        if (d.dead != null)
            foreach (int i in d.dead) Dead.Add(i);
        ImportMoveLog(d.moveLogCsv);
        int[] moveAt = CsvToBoard(d.moveAtCsv, expected);
        if (moveAt != null) MoveAt = moveAt;
        EnsureMoveAtForDisplay();
        history.Clear();
        posHashes.Clear();
        posHashes.Add(Hash(new int[Size * Size]));
        posHashes.Add(Hash(Board));
        return true;
    }

    // 打谱恢复：回到可落子的对局状态
    public void ResumeRecording()
    {
        Phase = GoPhase.Play;
        Passes = 0;
        Result = null;
        Dead.Clear();
    }
}

using System;
using System.Collections.Generic;

// MCTS(UCT) 围棋 AI，思路参考 Michi/Pachi：
//   选择：UCB1 沿树下行；扩展：随机合法手；模拟：轻量策略随机对局；回传胜负。
//   模拟落子策略（让 AI 像人一样思考局部）：
//     1. 对手棋串被打吃 → 提掉
//     2. 己方棋串被打吃 → 逃跑（逃了还是死就不逃）
//     3. 跟在最近一手附近走（3x3 邻域）
//     4. 全局随机，但避开填自己的眼、避开自投罗网
//   终局按数子法（子+空）判定胜负；劫争用 Zobrist 全局同形禁着。
// 搜索在后台线程进行，不卡界面。
public static class GoAI
{
    public static volatile bool SearchDone;
    public static int SearchId { get; private set; }
    public static int ResultX, ResultY;
    public static bool ResultIsPass;
    public static double BestWinRate;

    const int PASS = -1;
    static readonly Random rng = new Random();

    public static void BeginSearch(GoRules g, int color, float seconds)
    {
        int n = g.Size;
        int[] board = (int[])g.Board.Clone();
        var hist = g.ExportHashes();
        float komi = g.Komi;
        int ply = g.MoveCount;
        SearchId++;
        int myId = SearchId;
        SearchDone = false;
        System.Threading.Tasks.Task.Run(() =>
        {
            var fb = new FastBoard(n, board, color, hist, komi, ply);
            int best = Mcts(fb, seconds, out double wr);
            if (myId != SearchId) return;
            ResultIsPass = best == PASS;
            if (!ResultIsPass) { ResultX = best % n; ResultY = best / n; }
            BestWinRate = wr;
            SearchDone = true;
        });
    }

    /* ---------------- 快速棋盘（搜索/模拟用） ---------------- */

    class FastBoard
    {
        public readonly int n;
        public readonly int[] b;
        public int toMove;
        public int lastMove = -1;
        public int passes;
        public int ply;
        public readonly float komi;
        public long z;
        public HashSet<long> hist;
        public int lastCapCount;
        readonly int plyCap;

        readonly int[] mark;
        readonly int[] stack;
        readonly int[] grp;
        int markGen;

        struct UndoRec
        {
            public int move; public int[] captured; public int capCount;
            public long preZ, postZ; public int preLast, prePasses, mover;
        }
        readonly Stack<UndoRec> undo = new Stack<UndoRec>();

        public bool GameOver => passes >= 2 || ply > plyCap;

        public FastBoard(int n, int[] board, int toMove, HashSet<long> hist, float komi, int ply)
        {
            this.n = n; b = board; this.toMove = toMove; this.hist = hist; this.komi = komi; this.ply = ply;
            plyCap = n * n * 3 + 20;
            mark = new int[n * n]; stack = new int[n * n]; grp = new int[n * n];
            z = 0;
            for (int i = 0; i < b.Length; i++)
                if (b[i] != 0) z ^= GoRules.Zob[i * 3 + b[i]];
        }

        FastBoard(FastBoard o) : this(o.n, (int[])o.b.Clone(), o.toMove, new HashSet<long>(o.hist), o.komi, o.ply)
        {
            lastMove = o.lastMove; passes = o.passes;
        }

        public FastBoard WorkClone() { return new FastBoard(this); }

        public IEnumerable<int> Nb(int i)
        {
            int x = i % n, y = i / n;
            if (x > 0) yield return i - 1;
            if (x < n - 1) yield return i + 1;
            if (y > 0) yield return i - n;
            if (y < n - 1) yield return i + n;
        }

        public int LibsOf(int start, int cap)
        {
            markGen++; int gen = markGen;
            int color = b[start];
            int sp = 0, libs = 0;
            stack[sp++] = start; mark[start] = gen;
            while (sp > 0)
            {
                int s = stack[--sp];
                foreach (int q in Nb(s))
                {
                    if (b[q] == 0)
                    {
                        if (mark[q] != gen) { mark[q] = gen; if (++libs >= cap) return libs; }
                    }
                    else if (b[q] == color && mark[q] != gen) { mark[q] = gen; stack[sp++] = q; }
                }
            }
            return libs;
        }

        // 棋串唯一的气（仅当恰好一口气时返回该点，否则 -1）
        int SingleLiberty(int start)
        {
            markGen++; int gen = markGen;
            int color = b[start];
            int sp = 0, libs = 0, lib = -1;
            stack[sp++] = start; mark[start] = gen;
            while (sp > 0)
            {
                int s = stack[--sp];
                foreach (int q in Nb(s))
                {
                    if (b[q] == 0)
                    {
                        if (mark[q] != gen) { mark[q] = gen; libs++; lib = q; if (libs > 1) return -1; }
                    }
                    else if (b[q] == color && mark[q] != gen) { mark[q] = gen; stack[sp++] = q; }
                }
            }
            return libs == 1 ? lib : -1;
        }

        int CollectGroup(int start)
        {
            markGen++; int gen = markGen;
            int color = b[start];
            int sp = 0, cnt = 0;
            stack[sp++] = start; mark[start] = gen;
            while (sp > 0)
            {
                int s = stack[--sp];
                grp[cnt++] = s;
                foreach (int q in Nb(s))
                    if (b[q] == color && mark[q] != gen) { mark[q] = gen; stack[sp++] = q; }
            }
            return cnt;
        }

        public bool DoPlay(int m)
        {
            if (m == PASS)
            {
                undo.Push(new UndoRec { move = PASS, preZ = z, postZ = z, preLast = lastMove, prePasses = passes, mover = toMove });
                passes++; lastMove = -1; ply++; toMove = 3 - toMove; lastCapCount = 0;
                return true;
            }
            if (b[m] != 0) return false;
            int me = toMove, op = 3 - me;
            long preZ = z;
            b[m] = me; z ^= GoRules.Zob[m * 3 + me];
            int[] captured = null; int capCount = 0;
            foreach (int q in Nb(m))
            {
                if (b[q] == op && LibsOf(q, 1) == 0)
                {
                    int cnt = CollectGroup(q);
                    if (captured == null || capCount + cnt > captured.Length)
                        Array.Resize(ref captured, Math.Max(8, capCount + cnt));
                    for (int k = 0; k < cnt; k++)
                    {
                        int s = grp[k];
                        if (b[s] == op) { b[s] = 0; z ^= GoRules.Zob[s * 3 + op]; captured[capCount++] = s; }
                    }
                }
            }
            if (LibsOf(m, 1) == 0 || hist.Contains(z))
            {
                b[m] = 0;
                for (int k = 0; k < capCount; k++) b[captured[k]] = op;
                z = preZ;
                return false;
            }
            long postZ = z;
            hist.Add(postZ);
            undo.Push(new UndoRec
            {
                move = m, captured = captured, capCount = capCount,
                preZ = preZ, postZ = postZ, preLast = lastMove, prePasses = passes, mover = me
            });
            passes = 0; lastMove = m; ply++; toMove = op; lastCapCount = capCount;
            return true;
        }

        public void UndoLast()
        {
            var u = undo.Pop();
            toMove = u.mover; z = u.preZ; lastMove = u.preLast; passes = u.prePasses; ply--;
            if (u.move == PASS) return;
            hist.Remove(u.postZ);
            b[u.move] = 0;
            int op = 3 - u.mover;
            for (int k = 0; k < u.capCount; k++) b[u.captured[k]] = op;
        }

        bool IsOwnEye(int p, int me)
        {
            foreach (int q in Nb(p))
                if (b[q] != me) return false;
            return true;
        }

        // 模拟策略：返回已落在棋盘上的手（含 PASS）
        int ChoosePlayoutMove(Random r)
        {
            int me = toMove, op = 3 - me;
            int last = lastMove;
            if (last >= 0)
            {
                foreach (int q in Nb(last))
                    if (b[q] == op && LibsOf(q, 2) == 1)
                    {
                        int p = SingleLiberty(q);
                        if (p >= 0 && DoPlay(p)) return p;
                    }
                foreach (int q in Nb(last))
                    if (b[q] == me && LibsOf(q, 2) == 1)
                    {
                        int p = SingleLiberty(q);
                        if (p >= 0 && DoPlay(p))
                        {
                            if (LibsOf(p, 2) > 1) return p;
                            UndoLast();
                        }
                    }
                int lx = last % n, ly = last / n;
                for (int t = 0; t < 6; t++)
                {
                    int dx = r.Next(3) - 1, dy = r.Next(3) - 1;
                    if (dx == 0 && dy == 0) continue;
                    int px = lx + dx, py = ly + dy;
                    if (px < 0 || py < 0 || px >= n || py >= n) continue;
                    int p = py * n + px;
                    if (b[p] != 0 || IsOwnEye(p, me)) continue;
                    if (!DoPlay(p)) continue;
                    if (LibsOf(p, 2) == 1 && lastCapCount == 0) { UndoLast(); continue; }
                    return p;
                }
            }
            for (int t = 0; t < 40; t++)
            {
                int p = r.Next(n * n);
                if (b[p] != 0 || IsOwnEye(p, me)) continue;
                if (!DoPlay(p)) continue;
                if (LibsOf(p, 2) == 1 && lastCapCount == 0) { UndoLast(); continue; }
                return p;
            }
            DoPlay(PASS);
            return PASS;
        }

        public int PlayoutWinner(Random r)
        {
            while (!GameOver) ChoosePlayoutMove(r);
            return AreaWinner();
        }

        public int AreaWinner()
        {
            int black = 0, white = 0;
            for (int i = 0; i < b.Length; i++)
            {
                if (b[i] == 1) black++;
                else if (b[i] == 2) white++;
            }
            markGen++; int gen = markGen;
            for (int i = 0; i < b.Length; i++)
            {
                if (b[i] != 0 || mark[i] == gen) continue;
                int sp = 0, region = 0, border = 0;
                stack[sp++] = i; mark[i] = gen;
                while (sp > 0)
                {
                    int s = stack[--sp]; region++;
                    foreach (int q in Nb(s))
                    {
                        if (b[q] == 0) { if (mark[q] != gen) { mark[q] = gen; stack[sp++] = q; } }
                        else border |= b[q];
                    }
                }
                if (border == 1) black += region;
                else if (border == 2) white += region;
            }
            double w = white + komi;
            return black > w ? 1 : 2;
        }
    }

    /* ---------------- MCTS ---------------- */

    class Node
    {
        public int move;              // 导致本节点的那一手（-1=停一手）
        public int mover;             // 走这一手的一方
        public Node parent;
        public readonly List<Node> children = new List<Node>();
        public List<int> untried;
        public int visits;
        public double wins;           // 相对 mover 的胜场

        public Node BestChild()
        {
            double logv = Math.Log(Math.Max(1, visits));
            Node best = null; double bestU = double.MinValue;
            foreach (var c in children)
            {
                double u = c.wins / c.visits + 1.35 * Math.Sqrt(logv / c.visits);
                if (u > bestU) { bestU = u; best = c; }
            }
            return best;
        }
    }

    static List<int> ShuffledMoves(int n)
    {
        var list = new List<int>(n * n + 1);
        for (int i = 0; i < n * n; i++) list.Add(i);
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
        list.Add(PASS);
        return list;
    }

    static int Mcts(FastBoard root, float seconds, out double winRate)
    {
        var rootNode = new Node { move = -99, mover = 3 - root.toMove, untried = ShuffledMoves(root.n) };
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var path = new List<Node>(256);
        int iters = 0;

        while (DateTime.UtcNow < deadline || iters < 30)
        {
            var fb = root.WorkClone();
            var node = rootNode;
            path.Clear();
            path.Add(node);

            while (node.untried.Count == 0 && node.children.Count > 0 && !fb.GameOver)
            {
                node = node.BestChild();
                fb.DoPlay(node.move);
                path.Add(node);
            }

            if (!fb.GameOver && node.untried.Count > 0)
            {
                while (node.untried.Count > 0)
                {
                    int iu = rng.Next(node.untried.Count);
                    int m = node.untried[iu];
                    node.untried.RemoveAt(iu);
                    int mover = fb.toMove;
                    if (fb.DoPlay(m))
                    {
                        var child = new Node { move = m, mover = mover, parent = node, untried = ShuffledMoves(fb.n) };
                        node.children.Add(child);
                        node = child;
                        path.Add(child);
                        break;
                    }
                }
            }

            int winner = fb.GameOver ? fb.AreaWinner() : fb.PlayoutWinner(rng);
            for (int i = 0; i < path.Count; i++)
            {
                path[i].visits++;
                if (path[i].mover == winner) path[i].wins += 1.0;
            }
            iters++;
        }

        Node best = null;
        foreach (var c in rootNode.children)
            if (best == null || c.visits > best.visits) best = c;

        if (best == null) { winRate = 0; return PASS; }
        winRate = best.visits > 0 ? best.wins / best.visits : 0;
        return best.move;
    }
}

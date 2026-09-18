using System;
using System.Collections.Generic;

// MCTS 围棋 AI，算法对齐 Michi / Pachi（https://github.com/pasky/michi）：
//   选择：UCT + RAVE(AMAF)；扩展：一次生成全部合法手并加先验；
//   模拟：打吃/逃吃、3x3 邻域定式、拒绝自打吃；回传胜负。
//   树行走用落子/悔棋，避免每轮整盘拷贝。搜索在后台线程进行。
public static class GoAI
{
    public static volatile bool SearchDone;
    public static int SearchId { get; private set; }
    public static int ResultX, ResultY;
    public static bool ResultIsPass;
    public static double BestWinRate;

    const int PASS = -1;
    const int ExpandVisits = 8;
    const double RaveEquiv = 3500.0;
    const int PriorEven = 10;
    const int PriorSelfAtari = 10;
    const int PriorCaptureOne = 15;
    const int PriorCaptureMany = 30;
    const int PriorPat3 = 10;
    const int PriorEmpty = 10;
    static readonly int[] PriorCfg = { 24, 22, 8 };
    const double ProbCapture = 0.9;
    const double ProbPat3 = 0.95;
    const double ProbRejectSugg = 0.9;
    const double ProbRejectRand = 0.5;

    static readonly string[][] Pat3Src =
    {
        new[] { "XOX", "...", "???" },
        new[] { "XO.", "...", "?.?" },
        new[] { "XO?", "X..", "x.?" },
        new[] { ".O.", "X..", "..." },
        new[] { "XO?", "O.o", "?o?" },
        new[] { "XO?", "O.X", "???" },
        new[] { "?X?", "O.O", "ooo" },
        new[] { "OX?", "o.O", "???" },
        new[] { "X.?", "O.?", "   " },
        new[] { "OX?", "X.O", "   " },
        new[] { "?X?", "x.O", "   " },
        new[] { "?XO", "x.x", "   " },
        new[] { "?OX", "X.O", "   " },
    };

    static readonly HashSet<string> Pat3Set = BuildPat3();

    public static void BeginSearch(GoRules g, int color, float seconds)
    {
        int n = g.Size;
        int[] board = (int[])g.Board.Clone();
        var hist = g.ExportHashes();
        SearchId++;
        int myId = SearchId;
        SearchDone = false;
        System.Threading.Tasks.Task.Run(() =>
        {
            var rng = new Random();
            var fb = new FastBoard(n, board, color, hist, g.Komi, g.MoveCount, g.LastMove);
            int best = Mcts(fb, seconds, rng, out double wr);
            if (myId != SearchId) return;
            ResultIsPass = best == PASS;
            if (!ResultIsPass) { ResultX = best % n; ResultY = best / n; }
            BestWinRate = wr;
            SearchDone = true;
        });
    }

    /* ---------------- 3x3 模式（Michi pat3src） ---------------- */

    static HashSet<string> BuildPat3()
    {
        var set = new HashSet<string>();
        foreach (var src in Pat3Src)
        {
            string[] p = { Pad3(src[0]), Pad3(src[1]), Pad3(src[2]) };
            foreach (var rot in D4(p))
            {
                AddWild(set, string.Concat(rot));
                AddWild(set, string.Concat(SwapXO(rot)));
            }
        }
        return set;
    }

    static string Pad3(string s) => s.Length >= 3 ? s.Substring(0, 3) : (s + "   ").Substring(0, 3);

    static string[] SwapXO(string[] p)
    {
        string Map(string s) => s.Replace('X', 'Z').Replace('x', 'z').Replace('O', 'X').Replace('o', 'x').Replace('Z', 'O').Replace('z', 'o');
        return new[] { Map(p[0]), Map(p[1]), Map(p[2]) };
    }

    static IEnumerable<string[]> D4(string[] p)
    {
        for (int r = 0; r < 4; r++)
        {
            yield return p;
            yield return VertFlip(p);
            yield return HorizFlip(p);
            yield return HorizFlip(VertFlip(p));
            p = Rot90(p);
        }
    }

    static string[] Rot90(string[] p)
    {
        return new[]
        {
            "" + p[2][0] + p[1][0] + p[0][0],
            "" + p[2][1] + p[1][1] + p[0][1],
            "" + p[2][2] + p[1][2] + p[0][2]
        };
    }

    static string[] VertFlip(string[] p) => new[] { p[2], p[1], p[0] };
    static string[] HorizFlip(string[] p)
    {
        string Rev(string s) => "" + s[2] + s[1] + s[0];
        return new[] { Rev(p[0]), Rev(p[1]), Rev(p[2]) };
    }

    static void AddWild(HashSet<string> set, string pat)
    {
        foreach (var a in ExpandChar(pat, '?', ".XO "))
        foreach (var b in ExpandChar(a, 'x', ".O "))
        foreach (var c in ExpandChar(b, 'o', ".X "))
            set.Add(c.Replace('O', 'x'));
    }

    static List<string> ExpandChar(string p, char wild, string opts)
    {
        int i = p.IndexOf(wild);
        if (i < 0) return new List<string> { p };
        var acc = new List<string>();
        for (int k = 0; k < opts.Length; k++)
            acc.AddRange(ExpandChar(p.Substring(0, i) + opts[k] + p.Substring(i + 1), wild, opts));
        return acc;
    }

    /* ---------------- 快速棋盘 ---------------- */

    class FastBoard
    {
        public readonly int n;
        public readonly int[] b;
        public int toMove, lastMove, last2, passes, ply;
        public readonly float komi;
        public long z;
        public HashSet<long> hist;
        public int lastCapCount;
        readonly int plyCap;
        readonly int[] mark, stack, grp, nbBuf;
        readonly List<int> localBuf = new List<int>(24);
        int markGen;

        struct UndoRec
        {
            public int move, capCount, preLast, preLast2, prePasses, mover;
            public int[] captured;
            public long preZ, postZ;
        }
        readonly List<UndoRec> undo = new List<UndoRec>(128);

        public int UndoDepth => undo.Count;
        public bool GameOver => passes >= 2 || ply > plyCap;

        public FastBoard(int n, int[] board, int toMove, HashSet<long> hist, float komi, int ply, int last)
        {
            this.n = n; b = board; this.toMove = toMove; this.hist = hist; this.komi = komi; this.ply = ply;
            lastMove = last; last2 = -1;
            plyCap = n * n * 3 + 20;
            mark = new int[n * n]; stack = new int[n * n]; grp = new int[n * n]; nbBuf = new int[4];
            z = 0;
            for (int i = 0; i < b.Length; i++)
                if (b[i] != 0) z ^= GoRules.Zob[i * 3 + b[i]];
        }

        public int Nbs(int i)
        {
            int c = 0, x = i % n, y = i / n;
            if (x > 0) nbBuf[c++] = i - 1;
            if (x < n - 1) nbBuf[c++] = i + 1;
            if (y > 0) nbBuf[c++] = i - n;
            if (y < n - 1) nbBuf[c++] = i + n;
            return c;
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
                int nc = Nbs(s);
                for (int k = 0; k < nc; k++)
                {
                    int q = nbBuf[k];
                    if (b[q] == 0)
                    {
                        if (mark[q] != gen) { mark[q] = gen; if (++libs >= cap) return libs; }
                    }
                    else if (b[q] == color && mark[q] != gen) { mark[q] = gen; stack[sp++] = q; }
                }
            }
            return libs;
        }

        int SingleLiberty(int start)
        {
            markGen++; int gen = markGen;
            int color = b[start];
            int sp = 0, libs = 0, lib = -1;
            stack[sp++] = start; mark[start] = gen;
            while (sp > 0)
            {
                int s = stack[--sp];
                int nc = Nbs(s);
                for (int k = 0; k < nc; k++)
                {
                    int q = nbBuf[k];
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
                int nc = Nbs(s);
                for (int k = 0; k < nc; k++)
                {
                    int q = nbBuf[k];
                    if (b[q] == color && mark[q] != gen) { mark[q] = gen; stack[sp++] = q; }
                }
            }
            return cnt;
        }

        public bool DoPlay(int m)
        {
            if (m == PASS)
            {
                undo.Add(new UndoRec { move = PASS, preZ = z, postZ = z, preLast = lastMove, preLast2 = last2, prePasses = passes, mover = toMove });
                passes++; last2 = lastMove; lastMove = -1; ply++; toMove = 3 - toMove; lastCapCount = 0;
                return true;
            }
            if (m < 0 || m >= b.Length || b[m] != 0) return false;
            int me = toMove, op = 3 - me;
            long preZ = z;
            b[m] = me; z ^= GoRules.Zob[m * 3 + me];
            int[] captured = null; int capCount = 0;
            int nc = Nbs(m);
            int[] nbs = { 0, 0, 0, 0 };
            for (int k = 0; k < nc; k++) nbs[k] = nbBuf[k];
            for (int k = 0; k < nc; k++)
            {
                int q = nbs[k];
                if (b[q] == op && LibsOf(q, 1) == 0)
                {
                    int cnt = CollectGroup(q);
                    if (captured == null || capCount + cnt > captured.Length)
                        Array.Resize(ref captured, Math.Max(8, capCount + cnt));
                    for (int t = 0; t < cnt; t++)
                    {
                        int s = grp[t];
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
            undo.Add(new UndoRec
            {
                move = m, captured = captured, capCount = capCount,
                preZ = preZ, postZ = postZ, preLast = lastMove, preLast2 = last2, prePasses = passes, mover = me
            });
            passes = 0; last2 = lastMove; lastMove = m; ply++; toMove = op; lastCapCount = capCount;
            return true;
        }

        public void UndoLast()
        {
            var u = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            toMove = u.mover; z = u.preZ; lastMove = u.preLast; last2 = u.preLast2; passes = u.prePasses; ply--;
            if (u.move == PASS) return;
            hist.Remove(u.postZ);
            b[u.move] = 0;
            int op = 3 - u.mover;
            for (int k = 0; k < u.capCount; k++) b[u.captured[k]] = op;
        }

        public void UndoTo(int depth)
        {
            while (undo.Count > depth) UndoLast();
        }

        public bool IsTrueEye(int p, int me)
        {
            int x = p % n, y = p / n;
            int nc = Nbs(p);
            for (int k = 0; k < nc; k++)
                if (b[nbBuf[k]] != me) return false;
            int falseDiag = 0;
            if (x == 0 || y == 0 || x == n - 1 || y == n - 1) falseDiag = 1;
            int op = 3 - me;
            for (int dy = -1; dy <= 1; dy += 2)
            for (int dx = -1; dx <= 1; dx += 2)
            {
                int px = x + dx, py = y + dy;
                if (px < 0 || py < 0 || px >= n || py >= n) { falseDiag++; continue; }
                if (b[py * n + px] == op) falseDiag++;
            }
            return falseDiag < 2;
        }

        public string Neighborhood33(int p, int me)
        {
            int x = p % n, y = p / n;
            var ch = new char[9];
            int t = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int px = x + dx, py = y + dy;
                if (px < 0 || py < 0 || px >= n || py >= n) ch[t++] = ' ';
                else
                {
                    int v = b[py * n + px];
                    ch[t++] = v == 0 ? '.' : (v == me ? 'X' : 'x');
                }
            }
            return new string(ch);
        }

        public bool MatchPat3(int p, int me)
        {
            return b[p] == 0 && Pat3Set.Contains(Neighborhood33(p, me));
        }

        public int LineHeight(int p)
        {
            int x = p % n, y = p / n;
            return Math.Min(Math.Min(x, y), Math.Min(n - 1 - x, n - 1 - y));
        }

        public bool EmptyArea(int p, int dist)
        {
            int x = p % n, y = p / n;
            for (int dy = -dist; dy <= dist; dy++)
            for (int dx = -dist; dx <= dist; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > dist) continue;
                int px = x + dx, py = y + dy;
                if (px < 0 || py < 0 || px >= n || py >= n) continue;
                if (b[py * n + px] != 0) return false;
            }
            return true;
        }

        public void CfgMap(int origin, int[] map)
        {
            int nn = n * n;
            for (int i = 0; i < nn; i++) map[i] = -1;
            if (origin < 0) return;
            var q = new Queue<int>();
            map[origin] = 0; q.Enqueue(origin);
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                int nc = Nbs(c);
                for (int k = 0; k < nc; k++)
                {
                    int d = nbBuf[k];
                    int nd = (b[d] != 0 && b[d] == b[c]) ? map[c] : map[c] + 1;
                    if (map[d] >= 0 && map[d] <= nd) continue;
                    map[d] = nd;
                    q.Enqueue(d);
                }
            }
        }

        public void LocalPoints(List<int> dst)
        {
            dst.Clear();
            AddAround(dst, lastMove);
            AddAround(dst, last2);
        }

        void AddAround(List<int> dst, int p)
        {
            if (p < 0) return;
            if (!dst.Contains(p)) dst.Add(p);
            int x = p % n, y = p / n;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int px = x + dx, py = y + dy;
                if (px < 0 || py < 0 || px >= n || py >= n) continue;
                int q = py * n + px;
                if (!dst.Contains(q)) dst.Add(q);
            }
        }

        public int CaptureLib(int stone)
        {
            if (b[stone] == 0) return -1;
            return LibsOf(stone, 2) == 1 ? SingleLiberty(stone) : -1;
        }

        public int GroupSize(int stone) => CollectGroup(stone);

        public bool IsSelfAtari(int p)
        {
            return LibsOf(p, 2) == 1 && lastCapCount == 0;
        }

        public int PlayoutWinner(Random r, int[] amaf)
        {
            while (!GameOver)
            {
                int m = ChoosePlayoutMove(r);
                if (m != PASS && amaf[m] == 0) amaf[m] = 3 - toMove;
            }
            return AreaWinner();
        }

        int ChoosePlayoutMove(Random r)
        {
            int me = toMove;
            LocalPoints(localBuf);
            var local = localBuf;

            if (r.NextDouble() <= ProbCapture)
            {
                for (int i = 0; i < local.Count; i++)
                {
                    int s = local[i];
                    if (b[s] == 0) continue;
                    int lib = CaptureLib(s);
                    if (lib < 0) continue;
                    if (TryHeuristic(lib, r, ProbRejectSugg)) return lib;
                }
            }
            if (r.NextDouble() <= ProbPat3)
            {
                for (int i = 0; i < local.Count; i++)
                {
                    int p = local[i];
                    if (b[p] != 0 || !MatchPat3(p, me)) continue;
                    if (TryHeuristic(p, r, ProbRejectSugg)) return p;
                }
            }
            for (int t = 0; t < n * n; t++)
            {
                int p = r.Next(n * n);
                if (b[p] != 0 || IsTrueEye(p, me)) continue;
                if (!DoPlay(p)) continue;
                if (IsSelfAtari(p) && r.NextDouble() <= ProbRejectRand) { UndoLast(); continue; }
                return p;
            }
            DoPlay(PASS);
            return PASS;
        }

        bool TryHeuristic(int p, Random r, double reject)
        {
            if (b[p] != 0) return false;
            if (!DoPlay(p)) return false;
            if (IsSelfAtari(p) && r.NextDouble() <= reject) { UndoLast(); return false; }
            return true;
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
                    int nc = Nbs(s);
                    for (int k = 0; k < nc; k++)
                    {
                        int q = nbBuf[k];
                        if (b[q] == 0) { if (mark[q] != gen) { mark[q] = gen; stack[sp++] = q; } }
                        else border |= b[q];
                    }
                }
                if (border == 1) black += region;
                else if (border == 2) white += region;
            }
            return black > white + komi ? 1 : 2;
        }
    }

    /* ---------------- MCTS + RAVE ---------------- */

    class Node
    {
        public int move, toPlay, mover;
        public Node parent;
        public List<Node> children;
        public int visits, amafVisits;
        public double wins, amafWins, priorV = PriorEven, priorW = PriorEven / 2.0;

        public double Urgency()
        {
            double v = visits + priorV;
            double exp = (wins + priorW) / v;
            if (amafVisits == 0) return exp;
            double rave = amafWins / amafVisits;
            double beta = amafVisits / (amafVisits + v + v * amafVisits / RaveEquiv);
            return beta * rave + (1.0 - beta) * exp;
        }
    }

    static int Mcts(FastBoard fb, float seconds, Random rng, out double winRate)
    {
        var root = new Node { move = -99, toPlay = fb.toMove, mover = 3 - fb.toMove };
        Expand(fb, root);
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var path = new List<Node>(64);
        int[] amaf = new int[fb.n * fb.n];
        int iters = 0;

        while (DateTime.UtcNow < deadline || iters < 40)
        {
            int depth0 = fb.UndoDepth;
            path.Clear();
            Array.Clear(amaf, 0, amaf.Length);
            var node = root;
            path.Add(node);

            while (node.children != null && node.children.Count > 0 && !fb.GameOver)
            {
                node = PickChild(node, rng);
                fb.DoPlay(node.move);
                if (node.move != PASS && amaf[node.move] == 0) amaf[node.move] = node.mover;
                path.Add(node);
                if (node.children == null && node.visits + 1 >= ExpandVisits)
                    Expand(fb, node);
            }

            int winner = fb.GameOver ? fb.AreaWinner() : fb.PlayoutWinner(rng, amaf);
            for (int i = 0; i < path.Count; i++)
            {
                var nd = path[i];
                nd.visits++;
                if (nd.mover == winner) nd.wins += 1.0;
                if (nd.children == null) continue;
                for (int k = 0; k < nd.children.Count; k++)
                {
                    var ch = nd.children[k];
                    if (ch.move == PASS) continue;
                    if (amaf[ch.move] != nd.toPlay) continue;
                    ch.amafVisits++;
                    if (winner == nd.toPlay) ch.amafWins += 1.0;
                }
            }
            fb.UndoTo(depth0);
            iters++;
        }

        Node best = null;
        if (root.children != null)
            foreach (var c in root.children)
                if (best == null || c.visits > best.visits) best = c;
        if (best == null) { winRate = 0; return PASS; }
        winRate = best.visits > 0 ? best.wins / best.visits : 0.5;
        return best.move;
    }

    static Node PickChild(Node node, Random rng)
    {
        Node best = null; double bestU = double.MinValue;
        int start = rng.Next(node.children.Count);
        for (int i = 0; i < node.children.Count; i++)
        {
            var c = node.children[(start + i) % node.children.Count];
            double u = c.Urgency();
            if (u > bestU) { bestU = u; best = c; }
        }
        return best;
    }

    static void Expand(FastBoard fb, Node node)
    {
        node.children = new List<Node>();
        int me = fb.toMove;
        int[] cfg = new int[fb.n * fb.n];
        fb.CfgMap(fb.lastMove, cfg);
        var seen = new HashSet<int>();

        void AddChild(int m, string kind, int groupAt)
        {
            int capSize = 0;
            if (kind == "capture" && groupAt >= 0 && fb.b[groupAt] != 0)
                capSize = fb.GroupSize(groupAt);
            if (m != PASS && !seen.Add(m))
            {
                ApplyKindPrior(FindChild(node, m), kind, capSize);
                return;
            }
            if (m != PASS && fb.b[m] != 0) return;
            if (!fb.DoPlay(m)) return;
            var ch = new Node { move = m, mover = me, toPlay = fb.toMove, parent = node };
            ApplyKindPrior(ch, kind, capSize);
            if (m != PASS && fb.IsSelfAtari(m))
            {
                ch.priorV += PriorSelfAtari;
                ch.priorW += 0;
            }
            node.children.Add(ch);
            fb.UndoLast();
        }

        var local = new List<int>(18);
        fb.LocalPoints(local);
        for (int i = 0; i < local.Count; i++)
        {
            int s = local[i];
            if (fb.b[s] == 0) continue;
            int lib = fb.CaptureLib(s);
            if (lib < 0) continue;
            AddChild(lib, "capture", s);
        }
        for (int i = 0; i < local.Count; i++)
        {
            int p = local[i];
            if (fb.b[p] == 0 && fb.MatchPat3(p, me)) AddChild(p, "pat3", -1);
        }

        int nn = fb.n * fb.n;
        for (int p = 0; p < nn; p++)
        {
            if (fb.b[p] != 0 || fb.IsTrueEye(p, me)) continue;
            AddChild(p, "other", -1);
        }
        AddChild(PASS, "pass", -1);

        foreach (var ch in node.children)
        {
            if (ch.move == PASS) continue;
            int d = cfg[ch.move];
            if (d >= 1 && d <= PriorCfg.Length)
            {
                ch.priorV += PriorCfg[d - 1];
                ch.priorW += PriorCfg[d - 1];
            }
            int h = fb.LineHeight(ch.move);
            if (h <= 2 && fb.EmptyArea(ch.move, 3))
            {
                ch.priorV += PriorEmpty;
                if (h == 2) ch.priorW += PriorEmpty;
            }
        }
    }

    static Node FindChild(Node node, int move)
    {
        for (int i = 0; i < node.children.Count; i++)
            if (node.children[i].move == move) return node.children[i];
        return null;
    }

    static void ApplyKindPrior(Node ch, string kind, int capSize)
    {
        if (ch == null) return;
        if (kind == "capture")
        {
            int pr = capSize > 1 ? PriorCaptureMany : PriorCaptureOne;
            ch.priorV += pr; ch.priorW += pr;
        }
        else if (kind == "pat3")
        {
            ch.priorV += PriorPat3; ch.priorW += PriorPat3;
        }
    }
}

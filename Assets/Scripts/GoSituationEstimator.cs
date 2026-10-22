using System;
using System.Collections.Generic;

// Independent, deterministic Monte Carlo estimator. No neural net or external engine.
// See docs/situation-estimator.md for sources, scoring semantics and limitations.
public static class GoSituationEstimator
{
    public sealed class Result
    {
        public int[] Owner;
        public float[] Confidence;
        public bool[] Safe;
        public int BlackTerritory, WhiteTerritory, DeadBlack, DeadWhite;
        public int BlackStones, WhiteStones, Unsettled;
        public float BlackScore, WhiteScore;
    }

    sealed class Chain
    {
        public int Color;
        public readonly List<int> Stones = new List<int>();
        public readonly HashSet<int> Liberties = new HashSet<int>();
    }

    static int[][] Neighbors(int size)
    {
        var result = new int[size * size][];
        for (int p = 0; p < result.Length; p++)
        {
            var a = new List<int>(4); int x = p % size, y = p / size;
            if (x > 0) a.Add(p - 1); if (x + 1 < size) a.Add(p + 1);
            if (y > 0) a.Add(p - size); if (y + 1 < size) a.Add(p + size);
            result[p] = a.ToArray();
        }
        return result;
    }

    static List<Chain> Chains(int[] board, int[][] adjacent, out int[] ids)
    {
        ids = new int[board.Length]; Array.Fill(ids, -1);
        var groups = new List<Chain>(); var pending = new Stack<int>();
        for (int p = 0; p < board.Length; p++)
        {
            if (board[p] == 0 || ids[p] >= 0) continue;
            var chain = new Chain { Color = board[p] }; int id = groups.Count;
            groups.Add(chain); pending.Push(p); ids[p] = id;
            while (pending.Count > 0)
            {
                int q = pending.Pop(); chain.Stones.Add(q);
                foreach (int n in adjacent[q])
                    if (board[n] == 0) chain.Liberties.Add(n);
                    else if (board[n] == chain.Color && ids[n] < 0) { ids[n] = id; pending.Push(n); }
            }
        }
        return groups;
    }

    // Benson-style fixed point: retain only blocks with two vital regions whose
    // entire boundary consists of retained blocks. Failure to prove life is UNKNOWN.
    public static bool[] FindSafeStones(int[] board, int size)
    {
        var adjacent = Neighbors(size);
        var chains = Chains(board, adjacent, out int[] ids);
        var regions = new List<List<int>>(); var borders = new List<HashSet<int>>();
        var seen = new bool[board.Length]; var queue = new Queue<int>();
        for (int p = 0; p < board.Length; p++)
        {
            if (board[p] != 0 || seen[p]) continue;
            var region = new List<int>(); var border = new HashSet<int>();
            int colors = 0; seen[p] = true; queue.Enqueue(p);
            while (queue.Count > 0)
            {
                int q = queue.Dequeue(); region.Add(q);
                foreach (int n in adjacent[q])
                    if (board[n] != 0) { colors |= board[n]; border.Add(ids[n]); }
                    else if (!seen[n]) { seen[n] = true; queue.Enqueue(n); }
            }
            if (colors == 1 || colors == 2) { regions.Add(region); borders.Add(border); }
        }
        var active = new bool[chains.Count]; Array.Fill(active, true);
        bool changed;
        do
        {
            changed = false; var remove = new List<int>();
            for (int c = 0; c < chains.Count; c++)
            {
                if (!active[c]) continue;
                int vital = 0;
                for (int r = 0; r < regions.Count; r++)
                {
                    if (!borders[r].Contains(c)) continue;
                    bool valid = true;
                    foreach (int b in borders[r]) if (!active[b]) valid = false;
                    foreach (int point in regions[r]) if (!chains[c].Liberties.Contains(point)) valid = false;
                    if (valid) vital++;
                }
                if (vital < 2) remove.Add(c);
            }
            foreach (int c in remove) { active[c] = false; changed = true; }
        } while (changed);
        var safe = new bool[board.Length];
        for (int c = 0; c < chains.Count; c++)
            if (active[c]) foreach (int p in chains[c].Stones) safe[p] = true;
        return safe;
    }

    static int Transform(int p, int size, int symmetry)
    {
        int x = p % size, y = p / size;
        if ((symmetry & 4) != 0) x = size - 1 - x;
        for (int r = 0; r < (symmetry & 3); r++) { int oldX = x; x = size - 1 - y; y = oldX; }
        return y * size + x;
    }

    public static Result Estimate(int[] position, int size, float komi, int batches = 3)
    {
        if (size < 2 || position == null || position.Length != size * size) throw new ArgumentException("Invalid board");
        var board = (int[])position.Clone(); var adjacent = Neighbors(size);
        var result = new Result { Owner = new int[board.Length], Confidence = new float[board.Length], Safe = FindSafeStones(board, size) };
        var chains = Chains(board, adjacent, out _);
        var protectedStones = (bool[])result.Safe.Clone();
        // A lightweight rollout cannot establish a strategic attack on a healthy
        // opening chain. Hold >2-liberty roots fixed; only unsettled tactical chains
        // participate in the death estimate. This assumption is not a life proof.
        foreach (var chain in chains)
            if (chain.Liberties.Count > 2) foreach (int p in chain.Stones) protectedStones[p] = true;
        var tally = new int[board.Length]; int samples = 0;
        // Complete rotation/reflection + color-exchange ensemble removes directional,
        // color and first-player sampling bias. The same input always gives the same map.
        for (int batch = 0; batch < Math.Max(1, batches); batch++)
        for (int symmetry = 0; symmetry < 8; symmetry++)
        for (int swap = 0; swap < 2; swap++)
        {
            var transformed = new int[board.Length]; var safe = new bool[board.Length];
            for (int p = 0; p < board.Length; p++)
            {
                int target = Transform(p, size, symmetry);
                transformed[target] = board[p] == 0 ? 0 : swap == 0 ? board[p] : 3 - board[p];
                safe[target] = protectedStones[p];
            }
            var simulation = new Playout(transformed, size, adjacent, safe, (uint)(731 + batch * 104729));
            int[] final = simulation.Run(); samples++;
            for (int p = 0; p < board.Length; p++)
            {
                int color = final[Transform(p, size, symmetry)];
                if (swap != 0 && color != 0) color = 3 - color;
                tally[p] += color == 1 ? 1 : color == 2 ? -1 : 0;
            }
        }
        foreach (var chain in chains)
        {
            bool dead = chain.Liberties.Count <= 2;
            foreach (int p in chain.Stones)
            {
                float opposite = (chain.Color == 1 ? -tally[p] : tally[p]) / (float)samples;
                if (result.Safe[p] || opposite < .85f) dead = false;
            }
            foreach (int p in chain.Stones)
            {
                result.Owner[p] = dead ? 3 - chain.Color : chain.Color;
                if (dead) { if (chain.Color == 1) result.DeadBlack++; else result.DeadWhite++; }
                else if (chain.Color == 1) result.BlackStones++; else result.WhiteStones++;
            }
        }
        // Do not project far into the open board merely because a noisy rollout did.
        int[] distance = new int[board.Length]; Array.Fill(distance, board.Length);
        var queue = new Queue<int>();
        for (int p = 0; p < board.Length; p++) if (board[p] != 0) { distance[p] = 0; queue.Enqueue(p); }
        while (queue.Count > 0)
        {
            int p = queue.Dequeue();
            foreach (int n in adjacent[p]) if (distance[n] > distance[p] + 1) { distance[n] = distance[p] + 1; queue.Enqueue(n); }
        }
        for (int p = 0; p < board.Length; p++)
        {
            result.Confidence[p] = tally[p] / (float)samples;
            if (board[p] != 0) continue;
            if (distance[p] <= 5 && Math.Abs(result.Confidence[p]) >= .65f)
                result.Owner[p] = tally[p] > 0 ? 1 : 2;
        }
        // Remove isolated predictions using the unmodified map, so scan order is irrelevant.
        var originalOwners = (int[])result.Owner.Clone();
        for (int p = 0; p < board.Length; p++)
        {
            if (board[p] != 0 || originalOwners[p] == 0) continue;
            bool connected = false;
            foreach (int n in adjacent[p]) if (originalOwners[n] == originalOwners[p]) connected = true;
            if (!connected) result.Owner[p] = 0;
        }
        for (int p = 0; p < board.Length; p++)
        {
            if (board[p] != 0) continue;
            if (result.Owner[p] == 1) result.BlackTerritory++;
            else if (result.Owner[p] == 2) result.WhiteTerritory++;
            else result.Unsettled++;
        }
        result.BlackScore = result.BlackStones + result.BlackTerritory + result.DeadWhite;
        result.WhiteScore = result.WhiteStones + result.WhiteTerritory + result.DeadBlack + komi;
        return result;
    }

    sealed class Playout
    {
        readonly int[] board, backup, group, seen, libertySeen;
        readonly int[][] adjacent; readonly bool[] safe; readonly int size;
        int stamp, groupCount, liberties, ko = -1; uint random;
        public Playout(int[] board, int size, int[][] adjacent, bool[] safe, uint seed)
        {
            this.board = board; this.size = size; this.adjacent = adjacent; this.safe = safe; random = seed;
            backup = new int[board.Length]; group = new int[board.Length];
            seen = new int[board.Length]; libertySeen = new int[board.Length];
        }
        int Next(int max) { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return (int)(random % (uint)max); }
        void Collect(int p)
        {
            stamp++; groupCount = 1; liberties = 0; group[0] = p; seen[p] = stamp;
            for (int head = 0; head < groupCount; head++)
                foreach (int n in adjacent[group[head]])
                    if (board[n] == 0) { if (libertySeen[n] != stamp) { libertySeen[n] = stamp; liberties++; } }
                    else if (board[n] == board[p] && seen[n] != stamp) { seen[n] = stamp; group[groupCount++] = n; }
        }
        bool Eye(int p, int color)
        {
            foreach (int n in adjacent[p]) if (board[n] != color) return false;
            int x = p % size, y = p / size, hostile = 0, diagonals = 0;
            for (int dx = -1; dx <= 1; dx += 2)
            for (int dy = -1; dy <= 1; dy += 2)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                diagonals++; if (board[ny * size + nx] == 3 - color) hostile++;
            }
            return hostile <= (diagonals == 4 ? 1 : 0);
        }
        bool Play(int p, int color)
        {
            if (board[p] != 0 || p == ko || Eye(p, color)) return false;
            Array.Copy(board, backup, board.Length); board[p] = color;
            int captured = 0, capturedPoint = -1;
            foreach (int n in adjacent[p])
            {
                if (board[n] != 3 - color) continue;
                Collect(n);
                if (liberties != 0) continue;
                for (int i = 0; i < groupCount; i++)
                    if (safe[group[i]]) { Array.Copy(backup, board, board.Length); return false; }
                captured += groupCount; capturedPoint = group[0];
                for (int i = 0; i < groupCount; i++) board[group[i]] = 0;
            }
            Collect(p);
            if (liberties == 0 || (liberties == 1 && captured == 0)) { Array.Copy(backup, board, board.Length); return false; }
            ko = captured == 1 && groupCount == 1 && liberties == 1 ? capturedPoint : -1;
            return true;
        }
        int MovePriority(int p, int color)
        {
            if (board[p] != 0 || p == ko || Eye(p, color)) return int.MinValue;
            int priority = Next(12);
            foreach (int n in adjacent[p])
            {
                if (board[n] == 0) continue;
                Collect(n);
                if (board[n] == color)
                    priority += liberties == 1 ? 40 : 4;
                else if (liberties == 1) priority += 60 + groupCount;
            }
            return priority;
        }
        public int[] Run()
        {
            int color = 1, passes = 0;
            for (int move = 0; move < board.Length * 3 && passes < 2; move++)
            {
                bool played = false; int start = Next(board.Length), best = -1, bestPriority = int.MinValue;
                // Semi-random policy: captures and escapes take precedence, then local
                // connection. Uniform filling alone destroys useful life/death evidence.
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    int p = Next(board.Length), priority = MovePriority(p, color);
                    if (priority > bestPriority) { best = p; bestPriority = priority; }
                }
                if (best >= 0) played = Play(best, color);
                for (int j = 0; !played && j < board.Length; j++)
                    if (Play((start + j) % board.Length, color)) { played = true; break; }
                passes = played ? 0 : passes + 1;
                if (!played) ko = -1;
                color = 3 - color;
            }
            var owner = (int[])board.Clone(); var visited = new bool[board.Length];
            var queue = new Queue<int>(); var region = new List<int>();
            for (int p = 0; p < board.Length; p++)
            {
                if (board[p] != 0 || visited[p]) continue;
                region.Clear(); queue.Enqueue(p); visited[p] = true; int border = 0;
                while (queue.Count > 0)
                {
                    int q = queue.Dequeue(); region.Add(q);
                    foreach (int n in adjacent[q])
                        if (board[n] != 0) border |= board[n];
                        else if (!visited[n]) { visited[n] = true; queue.Enqueue(n); }
                }
                if (border == 1 || border == 2) foreach (int q in region) owner[q] = border;
            }
            return owner;
        }
    }
}

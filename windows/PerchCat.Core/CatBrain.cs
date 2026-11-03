namespace PerchCat.Core;

internal enum StepKind { Hold, Walk, Jump, Eye, Say }

/// 步骤: Hold(动画名, 秒) / Walk(x) / Jump(x, y) / Eye(表情, 瞬时) / Say(气泡文字, 瞬时).
internal readonly record struct Step(StepKind Kind, string? Name = null, double A = 0, double B = 0)
{
    public static Step Hold(string anim, double sec) => new(StepKind.Hold, anim, sec);
    public static Step Walk(double x) => new(StepKind.Walk, null, x);
    public static Step Jump(double x, double y) => new(StepKind.Jump, null, x, y);
    public static Step Eye(string? e) => new(StepKind.Eye, e);
    public static Step Say(string? s) => new(StepKind.Say, s);
}

/// 猫的小状态机. 步骤队列 + 当前步骤; 队列空了就 Plan() 下一个活动.
/// 每次 Tick 传入当前平台列表, 脚下平台没了 (窗口被拖走/关掉) 就自由落体.
public sealed class CatBrain
{
    /// 动画表: 名字 -> 帧序列. 不在表里的名字就是单帧静态姿势.
    static readonly Dictionary<string, (string[] Frames, double Beat, bool Once)> Anims = new()
    {
        ["walk"] = (new[] { "walk0", "walk1", "walk2", "walk3", "walk4", "walk5", "walk6", "walk7" }, 0.125, false),
        ["antic"] = (new[] { "half", "crouch", "crouch", "crouch" }, 0.125, true),
        ["landing"] = (new[] { "land", "land", "land", "half", "half" }, 0.1, true),
        ["sleep"] = (new[] { "sleep0", "sleep1" }, 1.2, false),
        ["knead"] = (new[] { "knead0", "knead1" }, 0.5, false),
        ["puff"] = (new[] { "puff0", "puff1" }, 0.25, false),
        ["lick"] = (new[] { "lick0", "lick1", "lick2", "lick1" }, 0.25, false),
        ["shake"] = (new[] { "shake0", "shake1" }, 0.125, false),
    };
    static readonly HashSet<string> NoBlink = new() { "sleep", "knead", "puff", "lick", "landing", "shake" };

    sealed class PettingState
    {
        public double Elapsed;
        public double SinceStroke;
        public bool Asleep;         // 睡着被摸: 只动眼睛, 醒不来, 原来的睡眠步骤接着算
        public string Pose = "";    // 醒着被摸时的姿势, 踩奶之前不变
    }

    public double Scale { get; }
    public double SleepChance { get; set; }
    public double CrossChance { get; set; } = PlatformTuning.CrossChance;
    public IReadOnlyList<Rect> Screens { get; set; } = Array.Empty<Rect>();    // 全部屏幕; 空 = 不限制跳跃路线
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Dir { get; private set; } = 1;
    public string? Bubble { get; private set; }
    public bool IsFalling { get; private set; }

    readonly Func<double> rand;
    List<Step> queue = new();
    Step? cur;
    double t;
    Arc? arc;
    string anim = "stand";
    double animT;
    string? eye;
    double blinkLeft = 3.0;
    List<string> blinkSeq = new();
    double blinkT;
    double vy, vx;
    PettingState? petting;
    double cooldownLeft;
    LiftMotion? lift;
    bool fellAsleep;                // 睡着时脚下没了: 落地抖抖毛接着睡

    // 以下参数随 scale 变 (原版 scale=5 时 g=1700, 走速 56)
    public double Gravity => 340 * Scale;
    public double WalkSpeed => 11.2 * Scale;
    public double HalfWidth => 10 * Scale;
    public const double Clearance = 24.0;

    public CatBrain(double scale, double x, double y, double sleepChance = 0.55, Func<double>? rand = null)
    {
        Scale = scale; X = x; Y = y; SleepChance = sleepChance;
        this.rand = rand ?? (() => Random.Shared.NextDouble());
    }

    public bool IsSleeping => anim == "sleep" && !IsFalling;
    public bool IsAirborne => IsFalling || arc is not null || IsLifted;
    public bool IsJumping => arc is not null;
    public string CurrentAnim => anim;
    public bool IsPetted => petting is not null;
    public bool IsLifted => lift is not null;

    /// 当前该画的帧名, 如 "walk3" 或 "stand@half".
    public string Frame()
    {
        var b = anim;
        if (Anims.TryGetValue(anim, out var a))
        {
            var i = (int)((animT + 1e-6) / a.Beat);
            b = a.Frames[a.Once ? Math.Min(i, a.Frames.Length - 1) : i % a.Frames.Length];
        }
        var e = blinkSeq.Count > 0 ? blinkSeq[0] : eye;
        return e is not null ? b + "@" + e : b;
    }

    public void Poke()
    {
        if (IsAirborne) return;
        petting = null;
        queue = new List<Step> { Step.Eye(null), Step.Say("哈!"), Step.Hold("puff", 1.6), Step.Say(null) };
        queue.AddRange(Lick());
        cur = null;
    }

    /// 鼠标正在头上蹭. 空中、落地动作、炸毛、冷却期都不理.
    public void Stroke()
    {
        if (IsAirborne || cooldownLeft > 0 || anim is "fall" or "landing" or "puff" or "shake") return;
        if (petting is null)
        {
            var asleep = anim == "sleep";
            petting = new PettingState
            {
                Asleep = asleep,
                Pose = anim is "sit" or "lick" ? "sit" : anim == "knead" ? "knead" : "stand",
            };
            if (!asleep) { queue = new List<Step>(); cur = null; }
            Bubble = null; blinkSeq = new List<string>();
            ApplyPetLook();
        }
        petting!.SinceStroke = 0;
    }

    // 拎

    /// 被拎起来: 不管正在干什么都停下. 拎着时 (X, Y) 就是鼠标位置, Frame() 是 fall.
    public void BeginLift(double x, double y)
    {
        petting = null; eye = null; Bubble = null; blinkSeq = new List<string>();
        queue = new List<Step>(); cur = null; arc = null;
        IsFalling = false; vy = 0; vx = 0; fellAsleep = false;
        SetAnim("fall");
        X = x; Y = y;
        lift = new LiftMotion(x, Scale);
    }

    public void LiftTo(double x, double y)
    {
        if (!IsLifted) return;
        X = x; Y = y;
    }

    /// 松手: (x, y) 是落下起点的脚底位置, (vx, vy) 是甩出去的初速度.
    public void Release(double x, double y, double vx = 0, double vy = 0)
    {
        if (!IsLifted) return;
        lift = null;
        X = x; Y = y;
        StartFall();
        this.vx = vx; this.vy = vy;
    }

    public LiftPose? LiftPose(int stages) => lift?.Pose(stages);

    /// 脚下的窗口挪了: 猫跟着挪, 正要走去的目标也一起挪. 睡着的不跟, 脚下空了就掉下去.
    public void Carry(double dx, double dy)
    {
        if (IsAirborne || IsSleeping) return;
        X += dx; Y += dy;
        static Step Shifted(Step s, double d) => s.Kind == StepKind.Walk ? Step.Walk(s.A + d) : s;
        cur = cur is { } c ? Shifted(c, dx) : null;
        queue = queue.Select(s => Shifted(s, dx)).ToList();
    }

    public void Tick(double rawDt, IReadOnlyList<Platform> platforms)
    {
        var dt = Math.Min(Math.Max(rawDt, 0), 0.6);
        animT += dt;
        cooldownLeft = Math.Max(0, cooldownLeft - dt);
        if (lift is not null) { lift.Update(X, dt); return; }
        UpdateBlink(dt);
        if (arc is null) LiftOntoFloor(platforms);      // 先看地板: 否则猫在地板下面会被 rescue 直接瞬移上去
        if (arc is null) RescueIfLost(platforms);
        if (IsFalling) { Fall(dt, platforms); return; }
        if (arc is null)
        {
            if (Geometry.SupportAt(platforms, X, Y) is { } s) Y = s.Top;
            else { StartFall(); Fall(dt, platforms); return; }
        }
        if (IsPetted)
        {
            UpdatePetting(dt, platforms);
            if (IsPetted) return;
        }
        var guardN = 0;
        while (cur is null && guardN < 20)
        {
            guardN++;
            if (queue.Count == 0) Plan(platforms);
            var next = queue[0];
            queue.RemoveAt(0);
            Begin(next);
        }
        Advance(dt);
    }

    // 下落

    void StartFall()
    {
        fellAsleep = anim == "sleep";
        if (IsPetted) { eye = null; petting = null; }
        queue = new List<Step>(); cur = null; arc = null; Bubble = null; blinkSeq = new List<string>();
        IsFalling = true; vy = 0; vx = 0; SetAnim("fall");
    }

    void Fall(double dt, IReadOnlyList<Platform> ps)
    {
        var r = Geometry.FallStep(X, Y, vy, dt, Gravity, ps, vx, HalfWidth, Screens);
        X = r.X; Y = r.Y; vy = r.Vy; vx = r.Vx;
        if (r.Landed is not null) Land();
    }

    void Land()
    {
        IsFalling = false; vy = 0; vx = 0;
        queue = new List<Step> { Step.Hold("landing", 0.5) };
        if (fellAsleep) { queue.Add(Step.Hold("shake", 1)); queue.AddRange(BackToSleep()); }
        else queue.Add(Step.Hold("stand", 0.5));
        fellAsleep = false;
    }

    List<Step> BackToSleep() => new() { Step.Hold("sit", 0.5), Step.Hold("sleep", 20 + rand() * 40) };

    /// 脚下再也不会有东西接住 (屏幕被拔、布局变了): 放回最近的地板.
    void RescueIfLost(IReadOnlyList<Platform> ps)
    {
        if (ps.Any(p => p.Lo <= X && X <= p.Hi && p.Top <= Y + 3)) return;
        var floors = ps.Where(p => p.WindowId is null).ToList();
        var pool = floors.Count == 0 ? ps.ToList() : floors;
        if (pool.Count == 0) return;
        var f = pool.MinBy(p => Geometry.Gap(X, p));
        X = Math.Min(Math.Max(X, f.Lo + HalfWidth), f.Hi - HalfWidth); Y = f.Top;
        if (IsPetted) { eye = null; petting = null; }
        IsFalling = false; vy = 0; vx = 0; queue = new List<Step> { Step.Hold("stand", 0.5) }; cur = null; Bubble = null;
    }

    /// 地板升起来了 (任务栏回来了): 猫跳上去, 差一点点就直接站上去. 正在下落的直接接住.
    void LiftOntoFloor(IReadOnlyList<Platform> ps)
    {
        Platform? found = null;
        foreach (var p in ps)
            if (p.Base is { } b && p.Lo <= X && X <= p.Hi && b <= Y && Y < p.Top) { found = p; break; }
        if (found is not { } f) return;
        if (IsFalling) { Y = f.Top; Land(); return; }
        if (f.Top - Y <= 3) { Y = f.Top; return; }
        var asleep = anim == "sleep";
        if (IsPetted) { eye = null; petting = null; }
        Bubble = null;
        queue = new List<Step> { Step.Hold("landing", 0.5) };
        if (asleep) queue.AddRange(BackToSleep()); else queue.Add(Step.Hold("stand", 0.5));
        Begin(Step.Jump(X, f.Top));
    }

    // 摸

    void ApplyPetLook()
    {
        if (petting is not { } p) return;
        if (p.Asleep)
        {
            eye = p.Elapsed < PetTuning.StirHalf ? "half" : "squint";
        }
        else
        {
            eye = p.Elapsed < PetTuning.HappyAfter ? "squint" : "happy";
            SetAnim(p.Elapsed < PetTuning.KneadAfter ? p.Pose : "knead");
        }
    }

    void UpdatePetting(double dt, IReadOnlyList<Platform> ps)
    {
        if (petting is not { } p) return;
        p.Elapsed += dt;
        p.SinceStroke += dt;
        if (p.SinceStroke >= PetTuning.Release - 1e-6)
        {
            petting = null; eye = null;
            if (!p.Asleep) queue = new List<Step> { Step.Hold("stand", 0.5) };
        }
        else if (p.Elapsed >= PetTuning.FedUpAfter - 1e-6)
        {
            petting = null; eye = null; cur = null;
            cooldownLeft = PetTuning.Cooldown;
            queue = new List<Step> { Step.Say("哈!"), Step.Hold("puff", PetTuning.PuffSeconds), Step.Say(null) };
            queue.AddRange(WalkAway(ps));
        }
        else
        {
            ApplyPetLook();
        }
    }

    /// 往平台上空得多的那边走. 平台太窄就不走.
    List<Step> WalkAway(IReadOnlyList<Platform> ps)
    {
        if (Geometry.SupportAt(ps, X, Y) is not { } s) return new List<Step>();
        double lo = s.Lo + HalfWidth, hi = s.Hi - HalfWidth;
        if (!(hi > lo)) return new List<Step>();
        var d = PetTuning.WalkAwayCells * Scale;
        return new List<Step> { Step.Walk(X - lo > hi - X ? Math.Max(lo, X - d) : Math.Min(hi, X + d)), Step.Hold("stand", 0.5) };
    }

    // 步骤

    void SetAnim(string a)
    {
        if (anim != a) { anim = a; animT = 0; }
        if (NoBlink.Contains(a)) blinkSeq = new List<string>();
    }

    void Begin(Step s)
    {
        cur = s; t = 0;
        switch (s.Kind)
        {
            case StepKind.Hold: SetAnim(s.Name!); break;
            case StepKind.Walk:
                SetAnim("walk");
                if (Math.Abs(s.A - X) > 0.5) Dir = s.A > X ? 1 : -1;
                break;
            case StepKind.Jump:
                if (Math.Abs(s.A - X) > 1) Dir = s.A > X ? 1 : -1;
                arc = new Arc(X, Y, s.A, s.B, Clearance, Gravity);
                SetAnim("rise");
                break;
            case StepKind.Eye: eye = s.Name; cur = null; break;
            case StepKind.Say: Bubble = s.Name; cur = null; break;
        }
    }

    void Advance(double dt)
    {
        if (cur is not { } s) return;
        t += dt;
        switch (s.Kind)
        {
            case StepKind.Hold:
                if (t >= s.A - 1e-6) cur = null;
                break;
            case StepKind.Walk:
                var step = Math.Min(WalkSpeed * dt, Math.Abs(s.A - X));
                X += Dir * step;
                if (Math.Abs(s.A - X) <= 0.5) cur = null;
                break;
            case StepKind.Jump:
                if (arc is not { } a) { cur = null; return; }
                if (t >= a.T) { X = s.A; Y = s.B; arc = null; cur = null; }
                else { var p = a.Pos(t); X = p.X; Y = p.Y; SetAnim(a.Pose(t)); }
                break;
            default: cur = null; break;
        }
    }

    void UpdateBlink(double dt)
    {
        if (blinkSeq.Count > 0)
        {
            blinkT += dt;
            if (blinkT >= 0.1) { blinkT = 0; blinkSeq.RemoveAt(0); }
            return;
        }
        blinkLeft -= dt;
        if (blinkLeft <= 0)
        {
            blinkLeft = 2 + rand() * 4;
            if (!NoBlink.Contains(anim) && eye is null) { blinkSeq = new List<string> { "half", "closed" }; blinkT = 0; }
        }
    }

    // 规划

    T Pick<T>(IReadOnlyList<T> a) => a[Math.Min((int)(rand() * a.Count), a.Count - 1)];

    List<Step> Lick() => new()
    {
        Step.Hold("sit", 0.5), Step.Eye("half"), Step.Hold("lick", 3 + rand()), Step.Hold("sit", 0.5), Step.Eye(null), Step.Hold("stand", 0.5),
    };

    /// 选一次跳: 平时只在当前屏; 偶尔 (CrossChance) 换屏, 可能先走到平台边上再过去.
    /// 路线必须一直在屏幕上, 所以只会从相邻且有重叠的边过去.
    List<Step>? JumpSteps(IReadOnlyList<Platform> ps, Platform? support)
    {
        List<Step>? Route(double from, bool otherScreen)
        {
            var here = Geometry.ScreenIndex(X, Y, Screens);
            var r1 = rand(); var r2 = rand();
            var j = Geometry.PickJump(from, Y, ps, HalfWidth, 60 * Scale, 90 * Scale, r1, r2,
                (p, tx) => (here != Geometry.ScreenIndex(tx, p.Top, Screens)) == otherScreen
                           && Geometry.ArcStaysOnScreens(new Arc(from, Y, tx, p.Top, Clearance, Gravity), Screens));
            if (j is not { } jump) return null;
            var steps = new List<Step>();
            if (from != X) steps.Add(Step.Walk(from));
            steps.AddRange(new[] { Step.Hold("antic", 0.5), Step.Jump(jump.Tx, jump.Platform.Top), Step.Hold("landing", 0.5), Step.Hold("stand", 0.5) });
            return steps;
        }
        if (rand() < CrossChance)
        {
            var starts = new List<double> { X };
            if (support is { } s && s.Width > 2 * HalfWidth) { starts.Add(s.Hi - HalfWidth); starts.Add(s.Lo + HalfWidth); }
            var r = Route(Pick(starts), true);
            if (r is not null) return r;
        }
        return Route(X, false);
    }

    void Plan(IReadOnlyList<Platform> ps)
    {
        var wasSleeping = anim == "sleep";
        if (rand() < SleepChance)
        {
            var dur = 20 + rand() * 40;
            queue = wasSleeping
                ? new List<Step> { Step.Hold("sleep", dur) }
                : new List<Step> { Step.Hold("stand", 0.5), Step.Hold("sit", 0.5), Step.Hold("sleep", dur) };
            return;
        }
        var q = wasSleeping ? new List<Step> { Step.Hold("sit", 0.5), Step.Hold("stand", 0.3) } : new List<Step>();
        var r = rand();
        var sup = Geometry.SupportAt(ps, X, Y);
        List<Step>? jump;
        if (r < 0.25)
        {
            q.AddRange(Lick());
        }
        else if (r < 0.55 && sup is { } s && s.Width > 4 * HalfWidth)
        {
            double lo = s.Lo + HalfWidth, hi = s.Hi - HalfWidth;
            var far = X - lo > hi - X ? lo : hi;
            var eye1 = Pick(new string?[] { null, null, "half" });
            q.Add(Step.Eye(eye1)); q.Add(Step.Walk(far)); q.Add(Step.Hold("stand", 0.5));
            q.Add(Step.Walk(lo + (hi - lo) * (0.3 + rand() * 0.4)));
            q.Add(Step.Eye(null)); q.Add(Step.Hold("stand", 0.5));
        }
        else if (r < 0.85 && (jump = JumpSteps(ps, sup)) is not null)
        {
            q.AddRange(jump);
        }
        else
        {
            q.Add(Step.Hold("stand", 0.5));
            q.Add(Step.Eye(Pick(new string?[] { null, null, "half", "down" })));
            q.Add(Step.Hold("knead", 5)); q.Add(Step.Eye(null)); q.Add(Step.Hold("stand", 0.5));
        }
        queue = q;
    }
}

namespace PerchCat.Core;

/// 窗口平台和跨屏的可调常量.
public static class PlatformTuning
{
    /// 平台顶边离屏幕顶至少这么多点. macOS 每块屏顶上都有菜单栏所以是 40; Windows 没有:
    /// 屏幕顶部要么被任务栏占掉 (工作区已排除), 要么就是窗口自己. 猫头出屏由 catHeight 那条规则管, 所以这里是 0.
    public const double MenuBarSafe = 0;
    /// 判全屏时的容差 (点): 像素换算成点会有舍入.
    public const double FullscreenSlack = 1;
    public const double MinHeight = 40;          // 比这矮的窗口不能站 (仍会挡住后面的窗口)
    public const double CrossChance = 0.5;       // 想跳的时候, 这个概率允许选别的屏上的目标

    /// 桌面和任务栏: 不算窗口, 既不能站也不挡人 (地板由工作区决定).
    public static readonly IReadOnlySet<string> ShellClasses = new HashSet<string>
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };
}

/// 拎猫的阈值. 时间单位秒, 速度单位"皮肤格/秒".
public static class LiftTuning
{
    public const double LiftUpPoints = 16.0;       // 按住往上提够这么多点才拎起
    public const double LiftUpRatio = 1.5;         // 上提距离至少是横向的这么多倍, 否则算蹭
    public const double DroopAfter = 1.5;          // 刚拎起在第一阶段停多久
    public const double DroopStep = 0.45;          // 之后每隔这么久往下坠一节
    public const double SwayBeat = 0.125;          // 摆动每帧多久
    public const double DragCellsPerSec = 20.0;    // 比这快才用大摆
    public const double FastCellsPerSec = 100.0;   // 比这快摆得更开
    public const double LagHold = 0.25;            // 手停了之后大摆再撑多久
    public static readonly (int Col, int Row) FallbackGrip = (17, 9);   // 没有拎起帧时拎 fall 姿势的头
}

/// 甩猫的阈值. 速度单位: 鼠标是点/秒, 上限是皮肤格/秒.
public static class ThrowTuning
{
    public const double Window = 0.1;              // 只看松手前这么久的鼠标
    public const double Idle = 0.06;               // 松手前停了这么久就算没甩
    public const double MinMouseSpeed = 150.0;     // 比这慢就是直接放下
    public const double Gain = 0.7;                // 鼠标速度折算成猫的速度
    public const double MaxSideCells = 400.0;
    public const double MaxUpCells = 225.0;
    public const double Bounce = 0.4;              // 撞屏幕边后留下的速度
}

/// 摸猫的阈值. 时间单位秒, 距离单位"皮肤格" (1 格 = scale 点).
public static class PetTuning
{
    // 判定"在蹭": 头宽 14 格, 一次直线穿过去攒不够
    public const double RubOnCells = 40.0;         // 攒够这么多距离才算开始摸
    public const double RubCapCells = 60.0;        // 攒的上限
    public const double RubLeakCellsPerSec = 10.0; // 每秒漏掉这么多, 比这慢的挪动永远攒不起来
    public const double RubMaxStepCells = 14.0;    // 单次采样最多记这么多, 防一下跳很远
    public const double RubMinStepPoints = 1.0;    // 比这小的抖动不算动

    // 摸的进程 (从开始摸算起)
    public const double HappyAfter = 3.0;          // squint -> happy
    public const double KneadAfter = 8.0;          // 开始踩奶
    public const double FedUpAfter = 30.0;         // 连续摸这么久就烦了
    public const double Release = 1.5;             // 停手这么久算摸完, 烦躁计时也清零
    public const double StirHalf = 1.0;            // 睡着被摸: 半睁眼多久后眯回去

    // 摸烦了
    public const double PuffSeconds = 1.6;
    public const double WalkAwayCells = 40.0;      // 走开多远
    public const double Cooldown = 60.0;           // 从炸毛算起, 这期间摸它不理
}

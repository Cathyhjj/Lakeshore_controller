using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LakeShoreControl
{
    public static class Numbers
    {
        public static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        public static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        public static string F(double x) { return x.ToString("0.000", Invariant); }
        public static double Parse(string x)
        {
            double n;
            if (!double.TryParse(x, NumberStyles.Float, Invariant, out n) || !Finite(n))
                throw new ArgumentException("请输入有效数字（小数点使用 .）：" + x);
            return n;
        }
        public static double ParseMinutes(string text)
        {
            string[] parts = text.Trim().Split(':');
            if (parts.Length == 1) return Parse(text);
            int minutes, seconds;
            if (parts.Length != 2 || !int.TryParse(parts[0], out minutes) || !int.TryParse(parts[1], out seconds) || minutes < 0 || seconds < 0 || seconds >= 60)
                throw new ArgumentException("保持时间请输入分钟数或分:秒，例如 24:28 表示 24 分 28 秒。");
            return minutes + seconds / 60.0;
        }
        public static string FormatMinutes(double minutes)
        {
            double seconds = minutes * 60, rounded = Math.Round(seconds);
            if (Finite(seconds) && seconds >= 0 && seconds <= 604800 && Math.Abs(seconds - rounded) < 0.000001)
            {
                int total = (int)rounded;
                return total % 60 == 0 ? (total / 60).ToString(Invariant) : (total / 60).ToString(Invariant) + ":" + (total % 60).ToString("00", Invariant);
            }
            return minutes.ToString("G17", Invariant);
        }
        public static string DurationLabel(double minutes)
        {
            string value = FormatMinutes(minutes);
            return value.Contains(":") ? value.Replace(":", "分") + "秒" : value + " min";
        }
        public static double ToKelvin(double value, int units)
        {
            if (units == 1) return value;
            if (units == 2) return value + 273.15;
            throw new InvalidOperationException("控制输入使用传感器原始单位，不能直接按温度控温。请在仪器面板确认输入单位为 K 或 °C。");
        }
        public static double FromKelvin(double value, int units)
        {
            if (units == 1) return value;
            if (units == 2) return value - 273.15;
            throw new InvalidOperationException("不支持传感器原始单位控温。");
        }
    }

    public sealed class Stage
    {
        public double TargetK { get; set; }
        public string Mode { get; set; } // Rate: K/min; Duration: minutes; Step: immediate target
        public double Value { get; set; }
        public double HoldMinutes { get; set; }
        public double TimeoutMinutes { get; set; }
        public Stage() { Mode = "Rate"; Value = 1; HoldMinutes = 5; TimeoutMinutes = 180; }
        public Stage Copy() { return (Stage)MemberwiseClone(); }
        public double RampSeconds(double startK)
        {
            return Mode == "Step" || Mode == "TimedStep" ? 0 : Mode == "Duration" ? Value * 60 : Math.Abs(TargetK - startK) / Value * 60;
        }
    }

    public sealed class Recipe
    {
        public int Version { get; set; }
        public string TemperatureUnit { get; set; }
        public List<Stage> Stages { get; set; }
        public double ToleranceK { get; set; }
        public double StableSeconds { get; set; }
        public bool HoldAtEnd { get; set; }
        public Recipe() { Version = 1; TemperatureUnit = "K"; Stages = new List<Stage>(); ToleranceK = 0.5; StableSeconds = 10; HoldAtEnd = true; }
        public static Recipe Parse(string json)
        {
            Recipe recipe = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Recipe>(json);
            if (recipe == null || (recipe.Version != 1 && recipe.Version != 2 && recipe.Version != 3) || recipe.TemperatureUnit != "K" || recipe.Stages == null || recipe.Stages.Count == 0 || recipe.Stages.Count > 1000) throw new ArgumentException("程序文件格式或版本不支持。");
            foreach (Stage stage in recipe.Stages)
                if (stage == null || (stage.Mode != "Rate" && stage.Mode != "Duration" && stage.Mode != "Step" && stage.Mode != "TimedStep")) throw new ArgumentException("程序含无效温度段。");
            return recipe;
        }
    }

    public sealed class Limits
    {
        public double MinK, MaxK, ToleranceK, StableSeconds;
        // Numeric validity bounds only; not a user-specified experiment temperature range.
        public static Limits ForInstrument(double tolerance, double stableSeconds)
        {
            return new Limits { MinK = 0, MaxK = 1505, ToleranceK = tolerance, StableSeconds = stableSeconds };
        }
        public void Validate()
        {
            if (!Numbers.Finite(MinK) || !Numbers.Finite(MaxK) || MinK < 0 || MaxK > 1505 || MinK >= MaxK)
                throw new ArgumentException("请填写装置允许的工作温区：0 ≤ 最低温度 < 最高温度 ≤ 1505 K。仪器量程不代表装置安全温区。");
            if (!Numbers.Finite(ToleranceK) || ToleranceK < 0.001 || ToleranceK > 20)
                throw new ArgumentException("到温容差必须在 0.001–20 K 之间。");
            if (!Numbers.Finite(StableSeconds) || StableSeconds < 0 || StableSeconds > 3600)
                throw new ArgumentException("到温确认时间必须在 0–3600 秒之间。");
        }
        public void CheckTemperature(double k)
        {
            if (!Numbers.Finite(k) || k < MinK || k > MaxK)
                throw new InvalidOperationException("温度 " + Numbers.F(k) + " K 超出工作温区 " + Numbers.F(MinK) + "–" + Numbers.F(MaxK) + " K。");
        }
    }

    public enum RunState { Idle, Running, Paused, Completed, Faulted, Stopped }

    // Pure, deterministic schedule engine. Time is monotonic elapsed seconds supplied by the caller.
    public sealed class ScheduleEngine
    {
        public RunState State { get; private set; }
        public int Index { get; private set; }
        public double SetpointK { get; private set; }
        public double HoldSeconds { get; private set; }
        public double StableElapsed { get; private set; }
        public double StageElapsed { get; private set; }
        public string Phase { get; private set; }
        public string Fault { get; private set; }
        public List<Stage> Stages { get; private set; }
        private Limits limits;
        private double startK, rampElapsed;
        private bool manual, manualArrived;

        public ScheduleEngine() { State = RunState.Idle; Phase = "待机"; Stages = new List<Stage>(); }

        public static void ValidateStages(IList<Stage> stages, Limits limits, double initialK)
        {
            limits.Validate(); limits.CheckTemperature(initialK);
            if (stages == null || stages.Count == 0 || stages.Count > 1000)
                throw new ArgumentException("温度程序需要 1–1000 个温度段。");
            double previous = initialK;
            for (int i = 0; i < stages.Count; i++)
            {
                Stage s = stages[i];
                limits.CheckTemperature(s.TargetK);
                if (s.Mode != "Rate" && s.Mode != "Duration" && s.Mode != "Step" && s.Mode != "TimedStep") throw new ArgumentException("第 " + (i + 1) + " 段的变化方式无效。");
                if (s.Mode != "Step" && s.Mode != "TimedStep" && (!Numbers.Finite(s.Value) || s.Value <= 0 || (s.Mode == "Rate" && (s.Value < 0.001 || s.Value > 100)) || (s.Mode == "Duration" && s.Value > 10080)))
                    throw new ArgumentException("第 " + (i + 1) + " 段：速率需为 0.001–100 K/min，或变化时间需大于 0 且不超过 10080 min。");
                if (!Numbers.Finite(s.HoldMinutes) || s.HoldMinutes < 0 || s.HoldMinutes > 10080)
                    throw new ArgumentException("保温时间需为 0–10080 min。");
                if (s.Mode == "Duration" && Math.Abs(s.TargetK - previous) / s.Value > 100)
                    throw new ArgumentException("第 " + (i + 1) + " 段指定时间过短，等效速率超过 100 K/min。");
                if (!Numbers.Finite(s.TimeoutMinutes) || s.TimeoutMinutes <= 0 || s.TimeoutMinutes > 20160)
                    throw new ArgumentException("每段总超时需大于 0 且不超过 20160 min。");
                double expected = s.RampSeconds(previous) + (s.Mode == "TimedStep" ? 0 : limits.StableSeconds) + s.HoldMinutes * 60;
                if (s.TimeoutMinutes * 60 <= expected)
                    throw new ArgumentException("第 " + (i + 1) + " 段超时太短：必须大于预计变温 + 到温确认 + 保温的 " + (expected / 60).ToString("0.##", Numbers.Invariant) + " min，并留出实测温度跟随时间。");
                previous = s.TargetK;
            }
        }

        public void Begin(IList<Stage> steps, Limits bounds, double measuredK, bool manualMode)
        {
            ValidateStages(steps, bounds, measuredK);
            Stages = steps.Select(s => s.Copy()).ToList(); limits = bounds;
            manual = manualMode; manualArrived = false; Index = 0; SetpointK = measuredK;
            State = RunState.Running; Fault = ""; BeginStage();
        }
        private void BeginStage()
        {
            startK = SetpointK; rampElapsed = 0; StageElapsed = 0; StableElapsed = 0; HoldSeconds = 0; Phase = "变温";
            if (Stages[Index].Mode == "Step") { SetpointK = Stages[Index].TargetK; Phase = "等待到温"; }
            if (Stages[Index].Mode == "TimedStep") { SetpointK = Stages[Index].TargetK; Phase = "目标保持计时"; }
        }
        public void RestorePausedTimed(IList<Stage> steps, Limits bounds, double measuredK, int index, double elapsed)
        {
            if (steps == null || steps.Any(s => s == null || s.Mode != "TimedStep") || index < 0 || index >= steps.Count || !Numbers.Finite(elapsed) || elapsed < 0 || elapsed >= steps[index].TimeoutMinutes * 60)
                throw new ArgumentException("暂停状态记录无效。");
            Begin(steps, bounds, measuredK, false); Index = index; BeginStage();
            StageElapsed = HoldSeconds = elapsed; State = RunState.Paused;
        }
        public void Pause() { if (State == RunState.Running) State = RunState.Paused; }
        public void Resume() { if (State == RunState.Paused) State = RunState.Running; }
        public void Stop() { State = RunState.Stopped; Phase = "已停止"; }
        public void Fail(string reason) { State = RunState.Faulted; Fault = reason; Phase = "故障"; }

        public void Tick(double dt, double measuredK)
        {
            if (State != RunState.Running && State != RunState.Paused) return;
            if (!Numbers.Finite(dt) || dt < 0 || dt > 15) { Fail("采样中断超过 15 秒；程序已中止。"); return; }
            try { limits.CheckTemperature(measuredK); } catch (Exception e) { Fail(e.Message); return; }
            if (State == RunState.Paused) return;
            Stage s = Stages[Index];
            StageElapsed += dt;
            if (!(manual && manualArrived) && StageElapsed > s.TimeoutMinutes * 60)
            { Fail("第 " + (Index + 1) + " 段超时，实测温度未按计划完成。"); return; }

            if (s.Mode == "TimedStep" && !manual)
            {
                HoldSeconds += dt;
                if (HoldSeconds + 1e-9 >= s.HoldMinutes * 60)
                {
                    if (Index + 1 < Stages.Count) { Index++; BeginStage(); }
                    else { State = RunState.Completed; Phase = "程序完成"; }
                }
                return;
            }

            if (Phase == "变温")
            {
                rampElapsed += dt;
                double duration = s.RampSeconds(startK);
                double fraction = duration <= 0 ? 1 : Math.Min(1, rampElapsed / duration);
                SetpointK = startK + (s.TargetK - startK) * fraction;
                if (fraction >= 1) { SetpointK = s.TargetK; Phase = "等待到温"; }
                // Do not credit the preceding ramp interval toward stabilization or hold time.
                return;
            }
            if (Math.Abs(measuredK - s.TargetK) > limits.ToleranceK)
            { StableElapsed = 0; HoldSeconds = 0; Phase = "等待到温"; return; }
            if (Phase == "等待到温")
            {
                StableElapsed += dt;
                if (StableElapsed >= limits.StableSeconds) { Phase = manual ? "恒温" : "保温"; manualArrived = true; }
                return;
            }
            if (manual) return;
            HoldSeconds += dt;
            if (HoldSeconds + 1e-9 >= s.HoldMinutes * 60)
            {
                if (Index + 1 < Stages.Count) { Index++; BeginStage(); }
                else { State = RunState.Completed; Phase = "程序完成"; }
            }
        }
    }
}

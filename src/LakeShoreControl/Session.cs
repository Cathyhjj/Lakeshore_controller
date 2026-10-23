using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LakeShoreControl
{
    public sealed class PausedRun
    {
        public string Identity;
        public string CreatedUtc;
        public int Output, Input, Range, Index;
        public double HoldSeconds;
        public Recipe Recipe;
    }
    // All public methods are serialized by the GUI semaphore; no simultaneous serial traffic.
    public sealed class ControlSession : IDisposable
    {
        public IInstrument Device;
        public Snapshot Last;
        public ScheduleEngine Engine = new ScheduleEngine();
        public int Output = 1, Input = 1, Range = 1;
        public bool Owned { get; private set; }
        public bool HoldAtEnd { get; private set; }
        public bool ShutdownUnconfirmed { get; private set; }
        public volatile bool StopRequested;
        public string Message = "已连接 · 只读监测";
        public Limits Bounds;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double lastTick, lastSetpoint;
        private int controlUnits;
        public ControlSession(IInstrument device) { Device = device; }

        public Snapshot Poll()
        {
            Last = Device.Read(Output, Input);
            if (Owned)
            {
                CheckMeasurement(Last, Bounds);
                if (Last.Mode != 1 || Last.ControlInput != Input || Last.Range != Range || Last.NativeRamp || Last.Units[Input - 1] != controlUnits)
                    throw new InvalidOperationException("仪器的模式、输入、单位、档位或 ramp 已被更改；程序中止。");
                if (!Numbers.Finite(Last.SetpointK) || Math.Abs(Last.SetpointK - lastSetpoint) > 0.01)
                    throw new InvalidOperationException("检测到仪器设定值被外部修改；程序中止。");
                if (Last.HardwareLimit > 0 && Engine.SetpointK >= Last.HardwareLimit)
                    throw new InvalidOperationException("当前设定值达到仪器输入温度上限。");
                double now = clock.Elapsed.TotalSeconds;
                double dt = now - lastTick; lastTick = now;
                if (dt > 15) throw new InvalidOperationException("采样中断超过 15 秒，可能发生电脑休眠；程序已中止。");
                int previousIndex = Engine.Index;
                Engine.Tick(dt, Last.Temperatures[Input - 1]);
                if (Engine.State == RunState.Faulted) throw new InvalidOperationException(Engine.Fault);
                if (Last.HardwareLimit > 0 && Engine.SetpointK >= Last.HardwareLimit)
                    throw new InvalidOperationException("下一步设定值达到仪器输入温度上限，未写入。");
                if (Engine.State == RunState.Completed && !HoldAtEnd)
                {
                    Device.OutputOff(Output); Owned = false; Message = "程序完成 · 已确认关闭输出 " + Output;
                }
                else if (!StopRequested && Math.Abs(Engine.SetpointK - lastSetpoint) >= 0.0005)
                {
                    Device.SetTemperature(Output, Input, Engine.SetpointK); lastSetpoint = Math.Round(Engine.SetpointK, 3);
                }
                // A timed hold begins after the new setpoint write has been verified.
                if (Engine.Index != previousIndex && Engine.Stages[Engine.Index].Mode == "TimedStep") lastTick = clock.Elapsed.TotalSeconds;
                if (Owned) Message = Engine.State == RunState.Paused ? "已暂停 · 保持当前设定值" :
                    Engine.State == RunState.Completed ? "程序完成 · 继续保持末温" : "第 " + (Engine.Index + 1) + "/" + Engine.Stages.Count + " 段 · " + Engine.Phase;
            }
            return Last;
        }

        private void CheckMeasurement(Snapshot s, Limits limits)
        {
            if (Input < 1 || Input > 4 || Output < 1 || Output > 2) throw new InvalidOperationException("只支持输入 A–D、加热输出 1–2。");
            if (s.Status[Input - 1] != 0) throw new InvalidOperationException("输入 " + (char)('A' + Input - 1) + " 读数异常，状态码 " + s.Status[Input - 1] + "。");
            limits.CheckTemperature(s.Temperatures[Input - 1]);
            Numbers.FromKelvin(s.Temperatures[Input - 1], s.Units[Input - 1]);
        }

        public void Start(List<Stage> stages, Limits limits, bool manual, bool holdAtEnd)
        {
            if (Owned) throw new InvalidOperationException("请先停止当前控温程序。");
            if (ShutdownUnconfirmed) throw new InvalidOperationException("上次关闭输出未确认，请先确认关闭输出。");
            limits.Validate();
            Last = Device.Read(Output, Input);
            CheckMeasurement(Last, limits);
            ScheduleEngine.ValidateStages(stages, limits, Last.Temperatures[Input - 1]);
            if (Range < 1 || Range > 3) throw new InvalidOperationException("请选择低、中或高加热档位。");
            foreach (Stage s in stages)
                if (Last.HardwareLimit > 0 && s.TargetK >= Last.HardwareLimit)
                    throw new InvalidOperationException("目标温度必须低于仪器输入上限 " + Numbers.F(Last.HardwareLimit) + " K。");
            Bounds = limits; HoldAtEnd = holdAtEnd; controlUnits = Last.Units[Input - 1];
            Engine.Begin(stages, limits, Last.Temperatures[Input - 1], manual);
            // From here on, any partial write failure requires a best-effort output shutdown.
            Owned = true;
            if (StopRequested) throw new OperationCanceledException("用户请求停止。");
            Device.Prepare(Output, Input, Range, Engine.SetpointK, () => StopRequested);
            lastSetpoint = Math.Round(Engine.SetpointK, 3); lastTick = clock.Elapsed.TotalSeconds;
            Message = manual ? "手动控温 · 变温" : "温度程序已启动 · 第 1 段";
        }

        public void RestorePaused(PausedRun saved)
        {
            DateTimeOffset created;
            if (Owned || saved == null || saved.Recipe == null || saved.Identity != Device.Identity || !DateTimeOffset.TryParse(saved.CreatedUtc, out created) || (DateTimeOffset.UtcNow - created).TotalSeconds > 300 || created > DateTimeOffset.UtcNow.AddSeconds(5))
                throw new InvalidOperationException("暂停状态过期或设备不匹配，未恢复程序。");
            if (saved.Output < 1 || saved.Output > 2 || saved.Input < 1 || saved.Input > 4 || saved.Range < 1 || saved.Range > 3 || saved.Index < 0 || saved.Index >= saved.Recipe.Stages.Count)
                throw new ArgumentException("暂停回路配置无效。");
            Output = saved.Output; Input = saved.Input; Range = saved.Range;
            Limits limits = Limits.ForInstrument(saved.Recipe.ToleranceK, saved.Recipe.StableSeconds);
            Last = Device.Read(Output, Input); CheckMeasurement(Last, limits);
            if (Last.Mode != 1 || Last.ControlInput != Input || Last.Range != Range || Last.NativeRamp || !Numbers.Finite(Last.SetpointK) || Math.Abs(Last.SetpointK - saved.Recipe.Stages[saved.Index].TargetK) > 0.002)
                throw new InvalidOperationException("仪器状态与暂停记录不一致，未恢复程序。");
            foreach (Stage stage in saved.Recipe.Stages)
                if (Last.HardwareLimit > 0 && stage.TargetK >= Last.HardwareLimit) throw new InvalidOperationException("目标超过仪器输入温度上限。");
            Engine.RestorePausedTimed(saved.Recipe.Stages, limits, Last.Temperatures[Input - 1], saved.Index, saved.HoldSeconds);
            Bounds = limits; HoldAtEnd = saved.Recipe.HoldAtEnd; controlUnits = Last.Units[Input - 1];
            lastSetpoint = Last.SetpointK; lastTick = clock.Elapsed.TotalSeconds;
            Owned = true; Message = "已恢复暂停状态 · 计时保留 · 保持当前目标";
        }

        public void Restart(List<Stage> stages, Limits limits, bool holdAtEnd)
        {
            if (!Owned) { Start(stages, limits, false, holdAtEnd); return; }
            if (StopRequested) throw new OperationCanceledException("用户请求停止。");
            limits.Validate(); Last = Device.Read(Output, Input); CheckMeasurement(Last, limits);
            ScheduleEngine.ValidateStages(stages, limits, Last.Temperatures[Input - 1]);
            if (Last.Mode != 1 || Last.ControlInput != Input || Last.Range != Range || Last.NativeRamp || Last.Units[Input - 1] != controlUnits)
                throw new InvalidOperationException("仪器配置已改变，不能重新开始当前程序。");
            foreach (Stage stage in stages)
                if (Last.HardwareLimit > 0 && stage.TargetK >= Last.HardwareLimit)
                    throw new InvalidOperationException("目标温度达到仪器输入温度上限。");
            if (StopRequested) throw new OperationCanceledException("用户请求停止。");
            // Keep the enabled PID loop and its range; reset only the program and verified target.
            Engine.Begin(stages, limits, Last.Temperatures[Input - 1], false);
            Device.SetTemperature(Output, Input, Engine.SetpointK);
            Bounds = limits; HoldAtEnd = holdAtEnd; lastSetpoint = Math.Round(Engine.SetpointK, 3);
            lastTick = clock.Elapsed.TotalSeconds;
            Message = "已重新开始 · 第 1 段 · 计时已清零";
        }

        public void PauseOrResume()
        {
            if (!Owned || Engine.State == RunState.Completed) return;
            if (Engine.State == RunState.Paused) { Engine.Resume(); Message = "继续程序"; }
            else { Engine.Pause(); Message = "已暂停 · 保持当前设定值"; }
            // No instrument write: software setpoint simply stops advancing.
        }
        public void Stop(bool all)
        {
            Engine.Stop();
            try
            {
                if (all) Device.AllOff(); else Device.OutputOff(Output);
                ShutdownUnconfirmed = false; Owned = false;
                Message = all ? "已确认所有加热输出关闭" : "已确认输出 " + Output + " 关闭";
            }
            catch { Owned = false; ShutdownUnconfirmed = true; Message = "关闭输出未确认！请在仪器面板按 All Off。"; throw; }
        }
        public string FailSafe(Exception failure)
        {
            bool needStop = Owned;
            Engine.Fail(failure.Message); Owned = false;
            if (needStop)
            {
                try { Device.OutputOff(Output); ShutdownUnconfirmed = false; Message = "程序中止；已确认输出 " + Output + " 关闭。"; }
                catch { ShutdownUnconfirmed = true; Message = "通信失败，无法确认加热已关闭！请在仪器面板按 All Off。"; }
            }
            else Message = ShutdownUnconfirmed ? "无法确认加热已关闭！请在仪器面板按 All Off。" : "监测失败 · " + failure.Message;
            return Message + "\n原因：" + failure.Message;
        }
        public void Dispose() { Device.Dispose(); }
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace LakeShoreControl
{
    public static class SelfTests
    {
        private static readonly List<string> results = new List<string>();
        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Near(double actual, double expected) { Assert(Math.Abs(actual - expected) < 0.00001, "Expected " + expected + ", got " + actual); }
        private static void Reject(Action action) { bool failed = false; try { action(); } catch { failed = true; } Assert(failed, "Expected rejection"); }
        private static Limits Bounds() { return new Limits { MinK = 80, MaxK = 300, ToleranceK = 0.1, StableSeconds = 2 }; }
        private static Stage Step(double target) { return new Stage { TargetK = target, Value = 60, HoldMinutes = 0.05, TimeoutMinutes = 180 }; }
        private static ScheduleEngine Engine(Stage stage) { ScheduleEngine e = new ScheduleEngine(); e.Begin(new List<Stage> { stage }, Bounds(), 100, false); return e; }
        private static void Test(string name, Action action) { action(); results.Add("PASS " + name); }
        public static int Run(string path)
        {
            try
            {
                Test("Heating rate uses elapsed seconds", () => { ScheduleEngine e = Engine(Step(110)); e.Tick(5, 100); Near(e.SetpointK, 105); e.Tick(5, 100); Near(e.SetpointK, 110); Assert(e.Phase == "等待到温", "Must wait for measurement"); });
                Test("Cooling rate has correct direction", () => { ScheduleEngine e = Engine(Step(90)); e.Tick(3, 100); Near(e.SetpointK, 97); });
                Test("Duration mode reaches target on time", () => { Stage s = Step(110); s.Mode = "Duration"; s.Value = 1; ScheduleEngine e = Engine(s); for (int i = 0; i < 3; i++) e.Tick(10, 100); Near(e.SetpointK, 105); for (int i = 0; i < 3; i++) e.Tick(10, 100); Near(e.SetpointK, 110); });
                Test("Pause freezes both ramp and timeout clocks", () => { ScheduleEngine e = Engine(Step(110)); e.Tick(2, 100); e.Pause(); e.Tick(10, 100); Near(e.SetpointK, 102); Near(e.StageElapsed, 2); e.Resume(); e.Tick(1, 100); Near(e.SetpointK, 103); });
                Test("No hold credit before actual temperature arrives", () => { ScheduleEngine e = Engine(Step(101)); e.Tick(1, 100); for (int i = 0; i < 10; i++) e.Tick(1, 100); Near(e.HoldSeconds, 0); Assert(e.State == RunState.Running, "Premature completion"); });
                Test("Stable window precedes hold; excursion restarts hold", () => { ScheduleEngine e = Engine(Step(101)); e.Tick(1, 100); e.Tick(1, 101); Assert(e.Phase == "等待到温", "Stable too soon"); e.Tick(1, 101); Assert(e.Phase == "保温", "Hold not entered"); e.Tick(2, 101); Near(e.HoldSeconds, 2); e.Tick(1, 100); Near(e.HoldSeconds, 0); e.Tick(2, 101); e.Tick(3, 101); Assert(e.State == RunState.Completed, "Hold did not finish"); });
                Test("Next stage starts from previous target", () => { ScheduleEngine e = new ScheduleEngine(); Stage a = Step(101); a.HoldMinutes = 0; e.Begin(new List<Stage> { a, Step(105) }, Bounds(), 100, false); e.Tick(1, 101); e.Tick(2, 101); e.Tick(1, 101); Assert(e.Index == 1, "Stage did not advance"); e.Tick(2, 101); Near(e.SetpointK, 103); });
                Test("Stage timeout catches stalled cooling", () => { Stage s = Step(101); s.TimeoutMinutes = 0.2; ScheduleEngine e = Engine(s); e.Tick(10, 100); e.Tick(3, 100); Assert(e.State == RunState.Faulted, "Missing timeout"); });
                Test("Stale sampling aborts without ramp jump", () => { ScheduleEngine e = Engine(Step(110)); e.Tick(16, 100); Assert(e.State == RunState.Faulted, "Missing stale fault"); Near(e.SetpointK, 100); });
                Test("Out of bounds temperature faults even while paused", () => { ScheduleEngine e = Engine(Step(110)); e.Pause(); e.Tick(1, 301); Assert(e.State == RunState.Faulted, "Missing paused trip"); });
                Test("Input validation rejects NaN and invalid durations", () => { Reject(() => Numbers.Parse("NaN")); Reject(() => Numbers.Parse("Infinity")); Stage s = Step(110); s.Value = 0; Reject(() => Engine(s)); s.Value = 60; s.TimeoutMinutes = 0.01; Reject(() => Engine(s)); s.TimeoutMinutes = 100; s.TargetK = 301; Reject(() => Engine(s)); });
                Test("Celsius conversions preserve physical target", () => { Near(Numbers.FromKelvin(273.15, 2), 0); Near(Numbers.ToKelvin(-138.15, 2), 135); Reject(() => Numbers.FromKelvin(100, 3)); });
                Test("Connection and monitor never write heater commands", () => { FakeInstrument d = new FakeInstrument(); ControlSession c = new ControlSession(d); c.Poll(); Assert(d.Writes == 0, "Unexpected control write"); });
                Test("Invalid sensor prevents any control write", () => { FakeInstrument d = new FakeInstrument(); d.Data.Status[0] = 32; ControlSession c = new ControlSession(d); Reject(() => c.Start(new List<Stage> { Step(110) }, Bounds(), false, false)); Assert(d.Writes == 0, "Wrote with invalid sensor"); });
                Test("Hardware input limit rejects target before control", () => { FakeInstrument d = new FakeInstrument(); d.Data.HardwareLimit = 105; ControlSession c = new ControlSession(d); Reject(() => c.Start(new List<Stage> { Step(110) }, Bounds(), false, false)); Assert(d.Writes == 0, "Wrote above hardware limit"); });
                Test("Prepare failure is followed by confirmed shutdown", () => { FakeInstrument d = new FakeInstrument(); d.FailPrepare = true; ControlSession c = new ControlSession(d); try { c.Start(new List<Stage> { Step(110) }, Bounds(), false, false); } catch (Exception ex) { c.FailSafe(ex); } Assert(d.OffCalls == 1 && !c.Owned && !c.ShutdownUnconfirmed, "Shutdown not verified"); });
                Test("Communication failure cannot claim successful shutdown", () => { FakeInstrument d = new FakeInstrument(); ControlSession c = new ControlSession(d); c.Start(new List<Stage> { Step(110) }, Bounds(), false, false); d.FailOff = true; c.FailSafe(new IOException("Cable removed")); Assert(c.ShutdownUnconfirmed && !c.Owned, "Lost shutdown uncertainty"); });
                Test("External setpoint changes abort owned control", () => { FakeInstrument d = new FakeInstrument(); ControlSession c = new ControlSession(d); c.Start(new List<Stage> { Step(110) }, Bounds(), false, false); d.Data.RawSetpoint = 140; Reject(() => c.Poll()); });
                Test("Stop request prevents prepare from energizing output", () => { FakeInstrument d = new FakeInstrument(); ControlSession c = new ControlSession(d); c.StopRequested = true; Reject(() => c.Start(new List<Stage> { Step(110) }, Bounds(), false, false)); Assert(d.Writes == 0, "Ignored stop request"); });
                Test("Raw sensor unit mode is never interpreted as Kelvin", () => { FakeInstrument d = new FakeInstrument(); d.Data.Units[0] = 3; ControlSession c = new ControlSession(d); Reject(() => c.Start(new List<Stage> { Step(110) }, Bounds(), false, false)); Assert(d.Writes == 0, "Wrote using raw units"); });
                Test("Duration mode rejects excessive equivalent ramp rate", () => { Stage s = Step(110); s.Mode = "Duration"; s.Value = 0.001; Reject(() => Engine(s)); });
                Test("Explicit shutdown-at-end turns selected output off", () => { FakeInstrument d = new FakeInstrument(); ControlSession c = new ControlSession(d); Stage s = Step(100); s.HoldMinutes = 0; c.Start(new List<Stage> { s }, Bounds(), false, false); c.Engine.Tick(1, 100); c.Engine.Tick(2, 100); c.Engine.Tick(1, 100); c.Poll(); Assert(!c.Owned && d.OffCalls == 1 && d.Data.Range == 0, "Completion did not shut down"); });
                Test("Default recipe retains heating after completion and subsequent polls", () => { FakeInstrument d = new FakeInstrument(); ControlSession c = new ControlSession(d); Recipe r = new Recipe(); Assert(r.HoldAtEnd, "Default must retain heating"); Stage s = Step(100); s.HoldMinutes = 0; c.Start(new List<Stage> { s }, Bounds(), false, r.HoldAtEnd); c.Engine.Tick(1, 100); c.Engine.Tick(2, 100); c.Engine.Tick(1, 100); for (int i = 0; i < 20; i++) c.Poll(); Assert(c.Owned && c.Engine.State == RunState.Completed && d.OffCalls == 0 && d.Data.Range > 0, "Heating stopped after completion"); Near(d.Data.RawSetpoint, 100); c.Stop(false); Assert(d.OffCalls == 1 && d.Data.Range == 0, "Explicit shutdown must still work"); });
                Test("Explicit hold-at-end keeps monitoring and control ownership", () => { FakeInstrument d = new FakeInstrument(); ControlSession c = new ControlSession(d); Stage s = Step(100); s.HoldMinutes = 0; c.Start(new List<Stage> { s }, Bounds(), false, true); c.Engine.Tick(1, 100); c.Engine.Tick(2, 100); c.Engine.Tick(1, 100); c.Poll(); Assert(c.Owned && d.OffCalls == 0 && c.Engine.State == RunState.Completed, "Hold-at-end lost control"); });
                Test("Manual mode stays active after reaching setpoint", () => { ScheduleEngine e = new ScheduleEngine(); e.Begin(new List<Stage> { Step(100) }, Bounds(), 100, true); e.Tick(1, 100); e.Tick(2, 100); for (int i = 0; i < 10; i++) e.Tick(10, 100); Assert(e.State == RunState.Running && e.Phase == "恒温", "Manual mode completed prematurely"); });
                Test("Step mode applies initial 80 K immediately and waits for measurement", () => { Stage s = Step(80); s.Mode = "Step"; s.Value = 0; s.HoldMinutes = 60; ScheduleEngine e = Engine(s); Near(e.SetpointK, 80); Assert(e.Phase == "等待到温", "Must wait for actual cooling"); for (int i = 0; i < 360; i++) e.Tick(10, 100); Near(e.HoldSeconds, 0); Assert(e.Index == 0 && e.State == RunState.Running, "One hour of cooling must not count as holding"); });
                Test("Step mode changes next target only after a full hour of stable hold", () => { Stage a = Step(100); a.Mode = "Step"; a.Value = 0; a.HoldMinutes = 60; Stage b = Step(110); b.Mode = "Step"; b.Value = 0; ScheduleEngine e = new ScheduleEngine(); e.Begin(new List<Stage> { a, b }, Bounds(), 100, false); e.Tick(2, 100); for (int i = 0; i < 359; i++) e.Tick(10, 100); Assert(e.Index == 0, "Advanced before one hour"); e.Tick(10, 100); Assert(e.Index == 1, "Did not advance after one hour"); Near(e.SetpointK, 110); Near(e.HoldSeconds, 0); });
                Test("Step mode prepare writes final target, not warm measured temperature", () => { FakeInstrument d = new FakeInstrument(); ControlSession c = new ControlSession(d); Stage s = Step(80); s.Mode = "Step"; s.Value = 0; c.Start(new List<Stage> { s }, Bounds(), false, false); Near(d.Data.RawSetpoint, 80); });
                Test("Full requested 27-point staircase completes in order with 27 hours hold", () => {
                    double[] targets = {80,90,100,110,120,130,135,140,145,150,155,160,165,170,175,180,185,190,195,200,210,220,230,240,260,280,298.15};
                    List<Stage> steps = new List<Stage>(); foreach (double t in targets) steps.Add(new Stage { TargetK = t, Mode = "Step", Value = 0, HoldMinutes = 60, TimeoutMinutes = 240 });
                    ScheduleEngine e = new ScheduleEngine(); e.Begin(steps, new Limits { MinK = 70, MaxK = 310, ToleranceK = 0.5, StableSeconds = 10 }, 285, false);
                    double totalHold = 0;
                    for (int i = 0; i < targets.Length; i++) {
                        Assert(e.Index == i, "Wrong point index"); Near(e.SetpointK, targets[i]); e.Tick(10, targets[i]);
                        for (int j = 0; j < 360; j++) { e.Tick(10, targets[i]); totalHold += 10; }
                    }
                    Near(totalHold, 27 * 3600); Assert(e.State == RunState.Completed, "Staircase incomplete"); Near(e.SetpointK, 298.15);
                });
                Test("Delayed command readback waits for matching value", () => { int reads = 0, waits = 0; CommandReadback.Verify(() => ++reads >= 3, () => waits++, "failed"); Assert(reads == 3 && waits == 2, "Did not handle delayed readback"); });
                Test("Persistent command mismatch remains a bounded failure", () => { int reads = 0; Reject(() => CommandReadback.Verify(() => { reads++; return false; }, () => {}, "failed")); Assert(reads == 6, "Readback retries must be bounded"); });
                Test("Timed staircase advances every hour with measured temperature far from target", () => {
                    double[] targets = {80,90,100,110,120,130,135,140,145,150,155,160,165,170,175,180,185,190,195,200,210,220,230,240,260,280,298.15};
                    List<Stage> steps = new List<Stage>(); foreach (double t in targets) steps.Add(new Stage { TargetK = t, Mode = "TimedStep", Value = 0, HoldMinutes = 60, TimeoutMinutes = 240 });
                    ScheduleEngine e = new ScheduleEngine(); e.Begin(steps, Bounds(), 217, false);
                    for (int i = 0; i < targets.Length; i++) {
                        Assert(e.Index == i, "Wrong timed point"); Near(e.SetpointK, targets[i]);
                        for (int j = 0; j < 359; j++) e.Tick(10, j % 2 == 0 ? 217 : 219);
                        Assert(e.Index == i && e.State == RunState.Running, "Advanced before full hour"); Near(e.HoldSeconds, 3590);
                        e.Tick(10, 218);
                    }
                    Assert(e.State == RunState.Completed, "Timed sequence did not complete in 27 hours"); Near(e.SetpointK, 298.15);
                });
                Test("Timed hold pause freezes clock while excursions do not reset it", () => { Stage s = Step(80); s.Mode = "TimedStep"; s.Value = 0; s.HoldMinutes = 60; ScheduleEngine e = Engine(s); e.Tick(10, 150); e.Pause(); e.Tick(10, 180); Near(e.HoldSeconds, 10); e.Resume(); e.Tick(10, 200); Near(e.HoldSeconds, 20); e.Tick(1, 301); Assert(e.State == RunState.Faulted, "Timed hold bypassed temperature protection"); });
                Test("Timed hold completion retains target until explicit shutdown", () => { FakeInstrument d = new FakeInstrument(); ControlSession c = new ControlSession(d); Stage s = Step(80); s.Mode = "TimedStep"; s.Value = 0; s.HoldMinutes = 1; c.Start(new List<Stage> { s }, Bounds(), false, true); Near(d.Data.RawSetpoint, 80); for (int i = 0; i < 6; i++) c.Engine.Tick(10, 100); c.Poll(); Assert(c.Engine.State == RunState.Completed && c.Owned && d.OffCalls == 0, "Completion lost heater control"); });
                Test("GUI recipe parser accepts all 27 timed steps and rejects unknown modes", () => { Recipe r = new Recipe { Version = 3 }; for (int i = 0; i < 27; i++) r.Stages.Add(new Stage { TargetK = 80 + i, Mode = "TimedStep", HoldMinutes = 60 }); string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(r); Recipe loaded = Recipe.Parse(json); Assert(loaded.Stages.Count == 27 && loaded.Stages[26].Mode == "TimedStep" && loaded.HoldAtEnd, "Timed recipe cannot load"); Reject(() => Recipe.Parse(json.Replace("TimedStep", "Unknown"))); });
                Test("Ten second files append across restart and preserve actual local timestamps", () => {
                    string dir = Path.Combine(Path.GetTempPath(), "LakeShoreLogTest_" + Guid.NewGuid().ToString("N"));
                    DateTimeOffset t = new DateTimeOffset(2026, 9, 12, 13, 0, 0, TimeSpan.FromHours(-5)); string file;
                    using (TemperatureLog log = new TemperatureLog(dir, "TEST")) { Assert(log.Record(t, 200, true, 1), "Missing first sample"); Assert(!log.Record(t.AddSeconds(2), 199, true, 1), "Saved duplicate time bucket"); Assert(log.Record(t.AddSeconds(10), 198, true, 1), "Missing next sample"); file = log.FilePath; }
                    using (TemperatureLog log = new TemperatureLog(dir, "TEST")) { Assert(!log.Record(t.AddSeconds(12), 197, true, 1), "Restart duplicated bucket"); Assert(log.Record(t.AddSeconds(40), 196, true, 1), "Missing delayed sample"); }
                    string[] lines = File.ReadAllLines(file); Assert(lines.Length == 4 && lines[0] == "temperature_K,local_time" && lines[1].Contains("200.000,2026-09-12 13:00:00.000 -05:00") && lines[3].Contains("13:00:40.000"), "History overwritten or missing samples invented"); File.Delete(file); Directory.Delete(dir);
                });
                Test("Sequence starts at first target without user min/max or measured arrival", () => { FakeInstrument d = new FakeInstrument(); d.Data.Temperatures[0] = 300; ControlSession c = new ControlSession(d); c.Start(new List<Stage> { new Stage { TargetK = 80, Mode = "TimedStep", Value = 0, HoldMinutes = 40, TimeoutMinutes = 1440 } }, Limits.ForInstrument(0.5, 10), false, true); Near(d.Data.RawSetpoint, 80); c.Engine.Tick(10, 300); Near(c.Engine.HoldSeconds, 10); Assert(c.Owned, "Program did not start"); });
                Test("No user min/max still rejects invalid sensors and hardware limit targets", () => { FakeInstrument d = new FakeInstrument(); d.Data.Status[0] = 32; ControlSession c = new ControlSession(d); Reject(() => c.Start(new List<Stage> { Step(110) }, Limits.ForInstrument(0.5, 10), false, true)); Assert(d.Writes == 0, "Invalid sensor wrote control"); d.Data.Status[0] = 0; d.Data.HardwareLimit = 105; Reject(() => c.Start(new List<Stage> { Step(110) }, Limits.ForInstrument(0.5, 10), false, true)); Assert(d.Writes == 0, "Hardware limit bypassed"); });
                Test("Restart from paused later point resets sequence without switching heater off", () => {
                    FakeInstrument d = new FakeInstrument(); ControlSession c = new ControlSession(d);
                    List<Stage> steps = new List<Stage> { new Stage { TargetK = 80, Mode = "TimedStep", HoldMinutes = 1, TimeoutMinutes = 240 }, new Stage { TargetK = 90, Mode = "TimedStep", HoldMinutes = 1, TimeoutMinutes = 240 } };
                    c.Start(steps, Bounds(), false, true); for (int i = 0; i < 6; i++) c.Engine.Tick(10, 100); c.Poll(); c.Engine.Tick(10, 100); c.PauseOrResume(); Assert(c.Engine.Index == 1 && c.Engine.State == RunState.Paused, "Test did not reach paused second point");
                    c.Restart(steps, Bounds(), true); Assert(c.Owned && c.Engine.Index == 0 && c.Engine.State == RunState.Running && d.OffCalls == 0 && d.Data.Range == c.Range, "Restart lost enabled control"); Near(c.Engine.HoldSeconds, 0); Near(c.Engine.StageElapsed, 0); Near(d.Data.RawSetpoint, 80);
                });
                Test("Restart from completed hold returns to first point", () => { FakeInstrument d = new FakeInstrument(); ControlSession c = new ControlSession(d); List<Stage> steps = new List<Stage> { new Stage { TargetK = 80, Mode = "TimedStep", HoldMinutes = 0, TimeoutMinutes = 240 } }; c.Start(steps, Bounds(), false, true); c.Engine.Tick(1, 100); c.Poll(); Assert(c.Engine.State == RunState.Completed, "Test must complete"); c.Restart(steps, Bounds(), true); Assert(c.Engine.State == RunState.Running && d.OffCalls == 0, "Completed restart failed"); });
                Test("Paused update handover preserves elapsed time and never writes hardware", () => {
                    FakeInstrument d = new FakeInstrument(); d.Data.RawSetpoint = 80; d.Data.Range = 3;
                    Recipe r = new Recipe { Version = 3 }; r.Stages.Add(new Stage { TargetK = 80, Mode = "TimedStep", HoldMinutes = 40, TimeoutMinutes = 1440 }); r.Stages.Add(new Stage { TargetK = 90, Mode = "TimedStep", HoldMinutes = 40, TimeoutMinutes = 240 });
                    ControlSession c = new ControlSession(d); c.RestorePaused(new PausedRun { Identity = "TEST", CreatedUtc = DateTimeOffset.UtcNow.ToString("o"), Output = 1, Input = 1, Range = 3, Index = 0, HoldSeconds = 248.716, Recipe = r });
                    c.Poll(); Assert(c.Owned && c.Engine.State == RunState.Paused && d.Writes == 0, "Restore changed hardware or resumed"); Near(c.Engine.HoldSeconds, 248.716); Near(d.Data.RawSetpoint, 80);
                    r.Stages[0].TargetK = 85; r.Stages[0].HoldMinutes = 35; Near(c.Engine.SetpointK, 80); Near(c.Engine.Stages[0].HoldMinutes, 40);
                    c.Restart(r.Stages, Limits.ForInstrument(.5, 10), true); Near(d.Data.RawSetpoint, 85); Near(c.Engine.HoldSeconds, 0); Near(c.Engine.Stages[0].HoldMinutes, 35); Assert(c.Engine.State == RunState.Running && d.OffCalls == 0, "Edited program did not restart correctly");
                });
                Test("Paused restoration rejects changed device state without writing", () => {
                    FakeInstrument d = new FakeInstrument(); d.Data.RawSetpoint = 90; d.Data.Range = 3; Recipe r = new Recipe(); r.Stages.Add(new Stage { TargetK = 80, Mode = "TimedStep", HoldMinutes = 40, TimeoutMinutes = 1440 }); ControlSession c = new ControlSession(d);
                    Reject(() => c.RestorePaused(new PausedRun { Identity = "TEST", CreatedUtc = DateTimeOffset.UtcNow.ToString("o"), Output = 1, Input = 1, Range = 3, Index = 0, HoldSeconds = 248.716, Recipe = r })); Assert(!c.Owned && d.Writes == 0, "Mismatch changed device or took ownership");
                });
                Test("Minutes and seconds survive GUI format and recipe roundtrip exactly", () => {
                    Near(Numbers.ParseMinutes("24:28") * 60, 1468);
                    Assert(Numbers.FormatMinutes(1468.0 / 60) == "24:28", "Duration rounded in GUI");
                    Near(Numbers.ParseMinutes(Numbers.FormatMinutes(1468.0 / 60)) * 60, 1468);
                    Near(Numbers.ParseMinutes("45"), 45); Near(Numbers.ParseMinutes("24.5"), 24.5);
                    Reject(() => Numbers.ParseMinutes("24:60")); Reject(() => Numbers.ParseMinutes("-1:28"));
                    Recipe r = new Recipe(); r.Stages.Add(new Stage { TargetK = 80, Mode = "TimedStep", HoldMinutes = 1468.0 / 60 });
                    Recipe loaded = Recipe.Parse(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(r));
                    Near(Numbers.ParseMinutes(Numbers.FormatMinutes(loaded.Stages[0].HoldMinutes)) * 60, 1468);
                });
                Test("23 points advance after exactly 1468 seconds each without waiting for measured arrival", () => {
                    List<Stage> stages = new List<Stage>();
                    for (int t = 80; t <= 290; t += 10) stages.Add(new Stage { TargetK = t, Mode = "TimedStep", Value = 0, HoldMinutes = 1468.0 / 60, TimeoutMinutes = 240 });
                    stages.Add(new Stage { TargetK = 298.15, Mode = "TimedStep", Value = 0, HoldMinutes = 1468.0 / 60, TimeoutMinutes = 240 });
                    ScheduleEngine e = new ScheduleEngine(); e.Begin(stages, Bounds(), 217, false);
                    for (int i = 0; i < stages.Count; i++) {
                        Assert(e.Index == i, "Wrong point"); Near(e.SetpointK, stages[i].TargetK);
                        for (int j = 0; j < 146; j++) e.Tick(10, 217);
                        e.Tick(7.9, 217); Assert(e.Index == i && e.State == RunState.Running, "Advanced early");
                        e.Tick(.1, 217);
                    }
                    Assert(stages.Count == 23 && e.State == RunState.Completed, "Sequence incomplete");
                    Near(e.SetpointK, 298.15); Near(stages.Count * 1468, 33764);
                });
                Test("English defaults and translated faults preserve original engine messages", () => {
                    Assert(UiText.English, "Fresh process should default to English");
                    string warning = "通信失败，无法确认加热已关闭！请在仪器面板按 All Off。";
                    Assert(UiText.T(warning).Contains("cannot be confirmed"), "Fault warning lost shutdown uncertainty");
                    Assert(UiText.T("第 2/23 段 · 目标保持计时") == "Step 2/23 / Timed hold", "Dynamic status not translated");
                    Assert(UiText.T("默认温度程序.json") == "默认温度程序.json", "Filename changed");
                    UiText.SetLanguage(false, false); Assert(UiText.T(warning) == warning, "Chinese original changed"); UiText.SetLanguage(true, false);
                });
                Test("Changing GUI language preserves all mode values, holds, paused clock and heater commands", () => {
                    using (MainForm form = new MainForm(null, false, null)) {
                        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        var add = typeof(MainForm).GetMethod("AddStage", flags);
                        var read = typeof(MainForm).GetMethod("ReadStages", flags);
                        var grid = (System.Windows.Forms.DataGridView)typeof(MainForm).GetField("grid", flags).GetValue(form);
                        grid.Rows.Clear();
                        foreach (string mode in new[] { "TimedStep", "Step", "Duration", "Rate" }) add.Invoke(form, new object[] { new Stage { TargetK = 100, Mode = mode, Value = 1, HoldMinutes = 1342.0 / 60, TimeoutMinutes = 240 } });
                        string before = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(read.Invoke(form, null));
                        FakeInstrument d = new FakeInstrument(); ControlSession c = new ControlSession(d);
                        c.Start(new List<Stage> { new Stage { TargetK = 100, Mode = "TimedStep", HoldMinutes = 1342.0 / 60, TimeoutMinutes = 240 } }, Bounds(), false, true);
                        c.Engine.Tick(7, 100); c.PauseOrResume();
                        typeof(MainForm).GetField("session", flags).SetValue(form, c);
                        int writes = d.Writes;
                        UiText.SetLanguage(false, false); Assert(form.Text.Contains("温度控制"), "Chinese window title missing");
                        UiText.SetLanguage(true, false); Assert(form.Text.Contains("Temperature Control"), "English window title missing");
                        string after = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(read.Invoke(form, null));
                        Assert(before == after, "Switch changed recipe values");
                        Assert(!(bool)typeof(MainForm).GetField("editedWhilePaused", flags).GetValue(form), "Switch marked paused recipe as edited");
                        Assert(c.Engine.State == RunState.Paused && d.Writes == writes && d.OffCalls == 0, "Switch changed instrument control"); Near(c.Engine.HoldSeconds, 7);
                        using (System.Windows.Forms.Label label = new System.Windows.Forms.Label { Text = "程序完成 · 继续保持末温" }) {
                            UiText.Bind(label); Assert(label.Text == "Completed; maintaining final target", "Initial label untranslated");
                            label.Text = "已暂停 · 保持当前设定值"; Assert(label.Text.StartsWith("Paused"), "Dynamic label untranslated");
                            UiText.SetLanguage(false, false); Assert(label.Text == "已暂停 · 保持当前设定值", "Label source lost"); UiText.SetLanguage(true, false);
                        }
                    }
                });
                results.Add("ALL " + results.Count + " TESTS PASSED. No physical instrument was used.");
                File.WriteAllLines(path, results); return 0;
            }
            catch (Exception e) { results.Add("FAIL " + e); File.WriteAllLines(path, results); return 1; }
        }
        private sealed class FakeInstrument : IInstrument
        {
            public Snapshot Data = new Snapshot { Temperatures = new double[] { 100, 0, 0, 0 }, Units = new int[] { 1, 1, 1, 1 }, Mode = 1, ControlInput = 1, RawSetpoint = 100 };
            public int Writes, OffCalls; public bool FailOff, FailPrepare;
            public string Identity { get { return "TEST"; } } public bool IsSimulation { get { return true; } }
            public Snapshot Read(int output, int input) { Data.Time = DateTime.Now; return Data; }
            public void Prepare(int output, int input, int range, double startK, Func<bool> cancelled) { if (cancelled()) throw new OperationCanceledException(); Writes++; if (FailPrepare) throw new IOException("Prepare failure"); Data.RawSetpoint = startK; Data.Range = range; Data.ControlInput = input; }
            public void SetTemperature(int output, int input, double targetK) { Writes++; Data.RawSetpoint = targetK; }
            public void OutputOff(int output) { OffCalls++; if (FailOff) throw new IOException("Disconnected"); Writes++; Data.Range = 0; }
            public void AllOff() { OutputOff(1); }
            public void Dispose() { }
        }
    }
}

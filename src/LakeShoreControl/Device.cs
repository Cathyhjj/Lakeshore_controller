using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Threading;

namespace LakeShoreControl
{
    public static class CommandReadback
    {
        // The 336 may acknowledge a query before a preceding setting has propagated.
        // Retry only the readback, never the state-changing command.
        public static void Verify(Func<bool> matches, Action delay, string failure)
        {
            for (int i = 0; i < 6; i++)
            {
                if (matches()) return;
                if (i < 5) delay();
            }
            throw new InvalidOperationException(failure);
        }
    }
    public sealed class Snapshot
    {
        public DateTime Time;
        public double[] Temperatures = new double[4];
        public int[] Status = new int[4];
        public int[] Units = new int[4];
        public int Mode, ControlInput, Range;
        public double RawSetpoint, Heater, HardwareLimit;
        public bool NativeRamp;
        public double SetpointK
        {
            get { return ControlInput >= 1 && ControlInput <= 4 && Units[ControlInput - 1] != 3 ? Numbers.ToKelvin(RawSetpoint, Units[ControlInput - 1]) : double.NaN; }
        }
    }

    public interface IInstrument : IDisposable
    {
        string Identity { get; }
        bool IsSimulation { get; }
        Snapshot Read(int output, int input);
        void Prepare(int output, int input, int range, double startK, Func<bool> cancelled);
        void SetTemperature(int output, int input, double targetK);
        void OutputOff(int output);
        void AllOff();
    }

    public sealed class SerialInstrument : IInstrument
    {
        private readonly SerialPort port;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long lastMessage = -1000;
        public string Identity { get; private set; }
        public bool IsSimulation { get { return false; } }
        public SerialInstrument(string comPort)
        {
            port = new SerialPort(comPort, 57600, Parity.Odd, 7, StopBits.One);
            port.Handshake = Handshake.None; port.DtrEnable = true; port.RtsEnable = true;
            port.ReadTimeout = 2000; port.WriteTimeout = 2000; port.NewLine = "\n";
            try
            {
                port.Open(); port.Write("\n"); Thread.Sleep(150); port.DiscardInBuffer();
                Identity = Query("*IDN?");
                if (!Identity.Split(',').Any(x => x.Trim() == "MODEL336"))
                    throw new InvalidOperationException("这个程序只支持 Lake Shore 336。实际设备：" + Identity);
            }
            catch { port.Dispose(); throw; }
        }
        private void Send(string command)
        {
            int wait = (int)(70 - (clock.ElapsedMilliseconds - lastMessage));
            if (wait > 0) Thread.Sleep(wait);
            port.WriteLine(command); lastMessage = clock.ElapsedMilliseconds;
        }
        private string Query(string command) { Send(command); return port.ReadLine().Trim(); }
        private double QDouble(string command) { return Numbers.Parse(Query(command)); }
        private int QInt(string command) { return int.Parse(Query(command), Numbers.Invariant); }
        private void Verify(Func<bool> matches, string failure) { CommandReadback.Verify(matches, () => Thread.Sleep(100), failure); }
        private int InputUnits(int input)
        {
            string[] values = Query("INTYPE? " + (char)('A' + input - 1)).Split(',');
            if (values.Length != 5) throw new InvalidOperationException("输入配置回复格式异常。");
            return int.Parse(values[4], Numbers.Invariant);
        }
        public Snapshot Read(int output, int input)
        {
            Snapshot s = new Snapshot();
            s.Temperatures = Query("KRDG? 0").Split(',').Select(Numbers.Parse).ToArray();
            if (s.Temperatures.Length != 4) throw new InvalidOperationException("温度回复数量异常；此版本按 A–D 四输入工作。");
            for (int i = 0; i < 4; i++) { s.Status[i] = QInt("RDGST? " + (char)('A' + i)); s.Units[i] = InputUnits(i + 1); }
            int[] mode = Query("OUTMODE? " + output).Split(',').Select(x => int.Parse(x, Numbers.Invariant)).ToArray();
            if (mode.Length != 3) throw new InvalidOperationException("输出模式回复格式异常。");
            s.Mode = mode[0]; s.ControlInput = mode[1];
            s.RawSetpoint = QDouble("SETP? " + output); s.Heater = QDouble("HTR? " + output);
            s.Range = QInt("RANGE? " + output);
            s.NativeRamp = Query("RAMP? " + output).Split(',')[0].Trim() != "0";
            s.HardwareLimit = QDouble("TLIMIT? " + (char)('A' + input - 1));
            s.Time = DateTime.Now;
            return s;
        }
        public void Prepare(int output, int input, int range, double startK, Func<bool> cancelled)
        {
            int units = InputUnits(input);
            double value = Numbers.FromKelvin(startK, units);
            // Disable output first. Only re-enable after readback of the new starting setpoint and mode.
            OutputOff(output);
            Send("RAMP " + output + ",0,1.000");
            Send("OUTMODE " + output + ",1," + input + ",0");
            Send("SETP " + output + "," + Numbers.F(value));
            int[] mode = Query("OUTMODE? " + output).Split(',').Select(x => int.Parse(x, Numbers.Invariant)).ToArray();
            if (mode.Length != 3 || mode[0] != 1 || mode[1] != input || mode[2] != 0)
                throw new InvalidOperationException("输出模式写入验证失败。");
            if (Query("RAMP? " + output).Split(',')[0].Trim() != "0") throw new InvalidOperationException("关闭仪器内置 ramp 失败。");
            Verify(() => Math.Abs(QDouble("SETP? " + output) - value) <= 0.002, "起始设定值写入验证失败。");
            if (cancelled()) throw new OperationCanceledException("用户请求停止，未启用输出。");
            Send("RANGE " + output + "," + range);
            Verify(() => QInt("RANGE? " + output) == range, "加热档位写入验证失败。");
        }
        public void SetTemperature(int output, int input, double targetK)
        {
            int units = InputUnits(input);
            double value = Numbers.FromKelvin(targetK, units);
            Send("SETP " + output + "," + Numbers.F(value));
            Verify(() => Math.Abs(QDouble("SETP? " + output) - value) <= 0.002, "设定温度写入后回读不一致。");
        }
        public void OutputOff(int output)
        {
            Send("RANGE " + output + ",0");
            Verify(() => QInt("RANGE? " + output) == 0, "未能确认输出 " + output + " 已关闭。");
        }
        public void AllOff()
        {
            Send("RANGE 1,0;RANGE 2,0;RANGE 3,0;RANGE 4,0");
            for (int o = 1; o <= 4; o++) { int selected = o; Verify(() => QInt("RANGE? " + selected) == 0, "未能确认所有输出关闭。"); }
        }
        public void Dispose() { if (port != null) port.Dispose(); }
    }

    public sealed class SimInstrument : IInstrument
    {
        private double temp = 243.72, setpoint = 243.72;
        private int range, channel = 1;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double last;
        public string Identity { get { return "SIMULATION · 演示设备（不连接任何硬件）"; } }
        public bool IsSimulation { get { return true; } }
        public Snapshot Read(int output, int input)
        {
            double now = clock.Elapsed.TotalSeconds, dt = now - last; last = now;
            double target = range > 0 ? setpoint : 243.72;
            temp += Math.Sign(target - temp) * Math.Min(Math.Abs(target - temp), dt * 0.5);
            Snapshot s = new Snapshot { Time = DateTime.Now, Mode = 1, ControlInput = channel, Range = range, RawSetpoint = setpoint,
                Heater = range > 0 ? Math.Min(100, Math.Max(0, (setpoint - temp) * 20)) : 0 };
            s.Temperatures = new double[] { temp, 0, 0, 0 }; s.Status = new int[] { 0, 96, 96, 32 };
            s.Units = new int[] { 1, 1, 1, 1 };
            return s;
        }
        public void Prepare(int output, int input, int heaterRange, double startK, Func<bool> cancelled) { if (cancelled()) throw new OperationCanceledException(); channel = input; setpoint = startK; range = heaterRange; }
        public void SetTemperature(int output, int input, double targetK) { setpoint = targetK; }
        public void OutputOff(int output) { range = 0; }
        public void AllOff() { range = 0; }
        public void Dispose() { }
    }
}

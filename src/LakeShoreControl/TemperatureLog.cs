using System;
using System.IO;
using System.Linq;
using System.Text;

namespace LakeShoreControl
{
    // Append-only, one real sample per ten-second local-time bucket. Never invent missed samples.
    public sealed class TemperatureLog : IDisposable
    {
        private readonly string directory, source;
        private StreamWriter writer;
        private string file;
        private long lastBucket = long.MinValue;
        public string FilePath { get { return file; } }
        public TemperatureLog(string directory, string source) { this.directory = directory; this.source = source; Directory.CreateDirectory(directory); }
        public bool Record(DateTimeOffset time, double temperature, bool valid, int input)
        {
            long bucket = time.UtcTicks / TimeSpan.TicksPerSecond / 10;
            string path = Path.Combine(directory, time.ToString("yyyyMMdd") + "_" + source + "_" + (char)('A' + input - 1) + "_10s.csv");
            if (path != file)
            {
                if (writer != null) writer.Dispose();
                file = path; lastBucket = long.MinValue;
                bool exists = File.Exists(file) && new FileInfo(file).Length > 0;
                if (exists)
                {
                    string last = File.ReadLines(file).LastOrDefault(); DateTimeOffset previous;
                    if (last != null && last.Contains(",") && DateTimeOffset.TryParse(last.Substring(last.IndexOf(',') + 1), Numbers.Invariant, System.Globalization.DateTimeStyles.None, out previous))
                        lastBucket = previous.UtcTicks / TimeSpan.TicksPerSecond / 10;
                }
                writer = new StreamWriter(file, true, new UTF8Encoding(true)); writer.AutoFlush = true;
                if (!exists) writer.WriteLine("temperature_K,local_time");
            }
            if (bucket <= lastBucket) return false;
            writer.WriteLine((valid && Numbers.Finite(temperature) ? Numbers.F(temperature) : "") + "," + time.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", Numbers.Invariant));
            lastBucket = bucket; return true;
        }
        public void Dispose() { if (writer != null) { writer.Dispose(); writer = null; } }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LakeShoreControl
{
    // Translation is a presentation boundary. Instrument commands, recipe values,
    // engine phases and the original on-disk measurement records remain unchanged.
    public static class UiText
    {
        private static bool english = true;
        public static bool English { get { return english; } }
        public static event Action LanguageChanged;
        private static readonly Dictionary<string, string> Messages = new Dictionary<string, string> {
            { "默认温度程序.json", "默认温度程序.json" },
            { "输入 ", "Input " },
            { "未连接设备", "Not connected" },
            { "连接后开始只读监测。", "Connect to monitor the instrument." },
            { "尚未运行温度程序。", "No temperature program is running." },
            { "等待读取仪器状态", "Waiting for instrument status" },
            { "填写分钟数，点击应用后更新全部温度点。", "Enter minutes or mm:ss, then apply to all points." },
            { "Lake Shore 336 · 温度控制", "Lake Shore 336 · Temperature Control" },
            { "Lake Shore 336 · 演示模式（无硬件）", "Lake Shore 336 · Demo (no hardware)" },
            { "温度控制  /  TEMPERATURE STUDIO", "TEMPERATURE CONTROL" },
            { "全部输出关闭 · All Off", "All Outputs Off" },
            { "真实设备", "Real device" },
            { "演示设备", "Demo device" },
            { "刷新", "Refresh" },
            { "连接", "Connect" },
            { "断开", "Disconnect" },
            { "控制输入", "Input" },
            { "加热输出", "Output" },
            { "实测温度", "Measured temperature" },
            { "仪器当前设定值", "Instrument setpoint" },
            { "当前加热输出", "Heater output" },
            { "温度程序", "Temperature program" },
            { "＋ 添加", "+ Add" },
            { "删除", "Delete" },
            { "等步长生成", "Generate" },
            { "导入", "Import" },
            { "保存", "Save" },
            { "全部温度点", "All points" },
            { "编辑程序表（可滚动）", "Edit steps (scrollable)" },
            { "定时阶跃：设定目标后立即计时，不等待到温。其余方式：实测到温并稳定后计时。保持可填分钟或分:秒，例如 24:28。", "Timed step: starts the hold timer at setpoint change. Other modes wait for stability. Hold accepts minutes or mm:ss (24:28)." },
            { "实时曲线", "Live chart" },
            { "操作记录", "Event log" },
            { "应用到全部点", "Apply to all" },
            { "每点保持 min / 分:秒", "Hold (min / mm:ss)" },
            { "统一调整并保存", "Update and save" },
            { "控温设置", "Control settings" },
            { "低 · Low", "Low" },
            { "中 · Medium", "Medium" },
            { "高 · High", "High" },
            { "加热档位", "Heater range" },
            { "到温容差 K", "Tolerance (K)" },
            { "完成后关闭输出", "Turn output off" },
            { "完成后保持末温", "Hold final target" },
            { "到温确认 秒", "Stable time (s)" },
            { "程序结束动作", "On completion" },
            { "手动控温", "Manual control" },
            { "目标温度", "Target temperature" },
            { "变化速率 K/min", "Rate (K/min)" },
            { "应用手动目标", "Apply manual target" },
            { "程序运行", "Run program" },
            { "开始程序", "Start" },
            { "暂停", "Pause" },
            { "继续", "Resume" },
            { "关闭加热", "Heater off" },
            { "↻ 重新开始 / Refresh", "Restart from point 1" },
            { "回到第 1 点，计时清零并运行；保留全部数据。", "Restart at point 1; reset timers; keep all data." },
            { "打开数据记录文件夹", "Open temperature data folder" },
            { "软件梯度需保持程序运行和电脑唤醒。断线或强制退出后，仪器可能保持最后设定值；必要时按面板 All Off。制冷能力决定实际降温速度。", "Keep this app and computer running. If disconnected, the instrument may hold its last target. Use panel All Off if needed. Cooling depends on the cryostat." },
            { "USB 57600 · 7O1    |    约每 2 秒采样    |    曲线与记录统一使用 K", "USB 57600 / 7O1    |    Sampling ~2 s    |    Chart and data in K" },
            { "温度程序加载失败", "Could not load program" },
            { "目标 K", "Target (K)" },
            { "变化方式", "Mode" },
            { "速率", "Rate" },
            { "时间", "Duration" },
            { "阶跃", "Step" },
            { "定时阶跃", "Timed step" },
            { "梯度值", "Rate / duration" },
            { "保持 min / 分:秒", "Hold min / mm:ss" },
            { "总超时 min", "Timeout (min)" },
            { "温度 / K", "Temperature / K" },
            { "仪器设定值", "Setpoint" },
            { "程序已修改；点击重新开始执行新方案。", "Program edited. Click Restart to run the new settings." },
            { "每点保持时间需大于 0 且不超过 10080 分钟，支持小数或分:秒（如 24:28）。", "Hold must be greater than 0 and at most 10080 minutes. Enter decimal minutes or mm:ss (24:28)." },
            { "请先添加或导入温度点。", "Add or import temperature points first." },
            { " 分钟。请先在编辑程序表中增大总超时，再设置保持时间。", " minutes. Increase that step's timeout in the table before setting the hold time." },
            { "已应用：每点 ", "Applied: each point " },
            { "（min 或分:秒）；总计 ", " (min or mm:ss); total " },
            { " 小时。", " hours." },
            { "已修改并保存；点击重新开始执行新方案。", "Saved. Click Restart to run the new settings." },
            { "当前仍暂停，点击重新开始应用新方案。", "Still paused. Click Restart to apply the new settings." },
            { "尚未启动序列。", "Sequence not started." },
            { "检查每点保持时间", "Check hold time" },
            { "已暂停，可修改；修改后点击重新开始。", "Paused and editable. After editing, click Restart." },
            { "未恢复暂停程序：", "Paused program was not restored: " },
            { "暂停状态恢复失败", "Could not restore paused state" },
            { "上次关闭输出未确认。请先在仪器面板按 All Off，再使用“全部输出关闭”重新确认。", "Previous shutdown was not confirmed. Press All Off on the instrument, then use All Outputs Off here to verify." },
            { "关闭状态未确认", "Shutdown not confirmed" },
            { "已断开 · 仪器保留原有设置", "Disconnected; instrument settings retained" },
            { "没有可用串口，请确认 USB 已接入并在面板启用。", "No serial port found. Connect USB and enable it on the instrument panel." },
            { "演示 · 无硬件连接", "Demo; no hardware" },
            { "连接成功：", "Connected: " },
            { "；未发送控温设置。", "; no control settings were sent." },
            { "连接失败", "Connection failed" },
            { "检查控温参数", "Check control parameters" },
            { "请求手动控温", "Manual control requested" },
            { "请求运行程序", "Program requested" },
            { "；按程序表执行，保留传感器与仪器温度保护。", "; execute the step table with sensor and instrument protections enabled." },
            { "温度段 ", "Step " },
            { "; 保温=", "; hold=" },
            { " min; 超时=", " min; timeout=" },
            { "检查程序参数", "Check program parameters" },
            { "请求重新开始：原第 ", "Restart requested: previous point " },
            { " 点，已保持 ", ", hold elapsed " },
            { " 秒；返回第 1 点重新计时，保留现有记录。", " s; reset to point 1 and keep existing records." },
            { "新程序已从第 1 点开始运行。", "New program running from point 1." },
            { "程序已修改，请点击重新开始执行新方案。", "Program edited. Click Restart to run the new settings." },
            { "程序已修改", "Program edited" },
            { "正在关闭输出…", "Turning output off..." },
            { "需要现场检查", "Check the instrument" },
            { "温控已中止", "Temperature control aborted" },
            { "输入异常", "Input error" },
            { " °C  ·  输入 ", " °C  /  Input " },
            { "状态码 ", "Status code " },
            { "原始单位", "Sensor units" },
            { "加热已关闭 · 设定值仅保留", "Heater off; setpoint retained only" },
            { "PID 控温已启用 · 持续保持目标", "PID enabled; maintaining target" },
            { "输出已启用 · 非 PID 控温模式", "Output enabled; non-PID mode" },
            { "档位 ", "Range " },
            { "关闭", "Off" },
            { "低", "Low" },
            { "中", "Medium" },
            { "高", "High" },
            { "  ·  输入 ", "  /  Input " },
            { "无", "None" },
            { "段目标  ", "Step target  " },
            { " K\n已用  ", " K\nElapsed  " },
            { " min    保持  ", " min    Hold  " },
            { "设定后计时 · 不等待实测到温", "Timer starts at setpoint change; no arrival wait" },
            { "到温确认  ", "Stability confirmation  " },
            { "仪器正在自主 PID 控温，持续保持目标。", "Instrument PID is maintaining the target." },
            { "加热已关闭，当前没有加热来维持目标。", "Heater is off; target is not actively maintained." },
            { "仪器处于非 PID 输出模式。", "Instrument output is in non-PID mode." },
            { "\nGUI 正在监测并记录温度；未运行多段程序。", "\nMonitoring and recording; no sequence is running." },
            { "温度 + 当地时间 · 每 10 秒保存\n", "Temperature + local time; saved every 10 s\n" },
            { "最新采样 ", "Latest sample " },
            { "（0=有效） | ", " (0=valid) | " },
            { "演示数据", "Demo data" },
            { " | 10 秒温度文件持续追加保存；完整原始采样另存 logs", " | Appending 10 s data; raw samples in logs" },
            { "自动保存 CSV + 操作日志\n", "Saving CSV + event log\n" },
            { "日志写入失败。", "Could not write event log." },
            { "温度程序 JSON|*.json", "Temperature program JSON|*.json" },
            { "已保存程序：", "Program saved: " },
            { "保存失败", "Save failed" },
            { "导入失败", "Import failed" },
            { "当前每点 ", "Each point: " },
            { "（min 或分:秒），修改后点击应用。", " (min or mm:ss). Edit, then click Apply." },
            { "当前各点时长不同；填写后可统一调整。", "Hold times differ. Enter a value to set them all." },
            { "已导入程序（尚未执行）：", "Program loaded (not started): " },
            { " 个温度点，总保持 ", " points; total hold " },
            { "生成等步长温度程序", "Generate temperature steps" },
            { "起始温度 K", "Start (K)" },
            { "终止温度 K", "End (K)" },
            { "步长 K（正数）", "Step size (K, positive)" },
            { "变温速率 K/min", "Ramp rate (K/min)" },
            { "每点保温 min", "Hold per point (min)" },
            { "每段总超时 min", "Timeout per step (min)" },
            { "自动判断升温或降温；包含首尾温度，末步可小于步长。将替换当前表格，生成后可逐行编辑。", "Includes both endpoints; the last step may be smaller. Replaces the table. Edit individual rows afterwards." },
            { "生成到表格", "Generate steps" },
            { "请检查温度、步长、速率和时间。", "Check temperatures, step size, rate and time." },
            { "最多生成 1000 段，请增大步长。", "At most 1000 steps are allowed. Increase the step size." },
            { "检查参数", "Check parameters" },
            { "控温正在运行，或关闭状态尚未确认。关闭前停止并确认当前输出已关闭？", "Control is active or shutdown is unconfirmed. Stop and verify the current output is off before exiting?" },
            { "退出温控", "Exit temperature control" },
            { "温控程序已经运行。请使用已打开的窗口，避免同时占用串口。", "Temperature control is already open. Use the existing window to avoid sharing the serial port." },
            { "请输入有效数字（小数点使用 .）：", "Enter a valid number (use . for decimals): " },
            { "保持时间请输入分钟数或分:秒，例如 24:28 表示 24 分 28 秒。", "Enter minutes or mm:ss. For example, 24:28 means 24 minutes 28 seconds." },
            { "控制输入使用传感器原始单位，不能直接按温度控温。请在仪器面板确认输入单位为 K 或 °C。", "Input uses raw sensor units. Set the instrument input units to K or degrees C before temperature control." },
            { "不支持传感器原始单位控温。", "Temperature control in raw sensor units is not supported." },
            { "请填写装置允许的工作温区：0 ≤ 最低温度 < 最高温度 ≤ 1505 K。仪器量程不代表装置安全温区。", "Enter the permitted apparatus range: 0 <= minimum < maximum <= 1505 K. Instrument range is not an apparatus safety limit." },
            { "到温容差必须在 0.001–20 K 之间。", "Tolerance must be between 0.001 and 20 K." },
            { "到温确认时间必须在 0–3600 秒之间。", "Stability confirmation must be between 0 and 3600 seconds." },
            { " K 超出工作温区 ", " K is outside the working range " },
            { "温度 ", "Temperature " },
            { "程序文件格式或版本不支持。", "Unsupported program file format or version." },
            { "程序含无效温度段。", "Program contains an invalid step." },
            { "温度程序需要 1–1000 个温度段。", "Program must have 1 to 1000 steps." },
            { "保温时间需为 0–10080 min。", "Hold must be between 0 and 10080 minutes." },
            { "每段总超时需大于 0 且不超过 20160 min。", "Step timeout must be greater than 0 and at most 20160 minutes." },
            { " 段：速率需为 0.001–100 K/min，或变化时间需大于 0 且不超过 10080 min。", ": rate must be 0.001-100 K/min, or duration must be greater than 0 and at most 10080 minutes." },
            { " 段指定时间过短，等效速率超过 100 K/min。", ": duration is too short; equivalent rate exceeds 100 K/min." },
            { " 段的变化方式无效。", ": invalid mode." },
            { " 段超时，实测温度未按计划完成。", ": timed out; measured temperature did not follow the program." },
            { " 段超时太短：必须大于预计变温 + 到温确认 + 保温的 ", ": timeout must exceed the estimated ramp + stability + hold time of " },
            { " min，并留出实测温度跟随时间。", " min, with extra time for measured temperature to follow." },
            { "待机", "Idle" },
            { "故障", "Fault" },
            { "等待到温", "Waiting for temperature" },
            { "变温", "Ramping" },
            { "目标保持计时", "Timed hold" },
            { "保温", "Holding" },
            { "恒温", "Maintaining target" },
            { "程序完成", "Completed" },
            { "已停止", "Stopped" },
            { "采样中断超过 15 秒；程序已中止。", "Sampling gap exceeded 15 seconds; program aborted." },
            { "采样中断超过 15 秒，可能发生电脑休眠；程序已中止。", "Sampling gap exceeded 15 seconds, possibly due to sleep; program aborted." },
            { "暂停状态记录无效。", "Invalid paused state record." },
            { "暂停回路配置无效。", "Invalid paused loop configuration." },
            { "暂停状态过期或设备不匹配，未恢复程序。", "Paused state expired or device does not match; not restored." },
            { "仪器状态与暂停记录不一致，未恢复程序。", "Instrument state differs from paused record; not restored." },
            { "已恢复暂停状态 · 计时保留 · 保持当前目标", "Restored paused state; elapsed time retained; holding target" },
            { "未启动控温", "Control not started" },
            { "已连接 · 只读监测", "Connected; monitoring only" },
            { "手动控温 · 变温", "Manual control; ramping" },
            { "温度程序已启动 · 第 1 段", "Program started; step 1" },
            { "请先停止当前控温程序。", "Stop the current control program first." },
            { "上次关闭输出未确认，请先确认关闭输出。", "Previous shutdown is unconfirmed. Verify output is off first." },
            { "用户请求停止。", "Stop requested by user." },
            { "仪器配置已改变，不能重新开始当前程序。", "Instrument configuration changed; cannot restart this program." },
            { "目标超过仪器输入温度上限。", "Target exceeds the instrument input temperature limit." },
            { "已重新开始 · 第 1 段 · 计时已清零", "Restarted at step 1; timers reset" },
            { "未启动：", "Not started: " },
            { "仪器的模式、输入、单位、档位或 ramp 已被更改；程序中止。", "Instrument mode, input, units, range or ramp changed; program aborted." },
            { "检测到仪器设定值被外部修改；程序中止。", "Instrument setpoint changed externally; program aborted." },
            { "当前设定值达到仪器输入温度上限。", "Current setpoint reaches the input temperature limit." },
            { "下一步设定值达到仪器输入温度上限，未写入。", "Next setpoint reaches the input temperature limit; not written." },
            { "程序完成 · 继续保持末温", "Completed; maintaining final target" },
            { "程序完成 · 已确认关闭输出 ", "Completed; confirmed output off: " },
            { "已暂停 · 保持当前设定值", "Paused; maintaining current setpoint" },
            { "继续程序", "Program resumed" },
            { "已确认所有加热输出关闭", "All heater outputs confirmed off" },
            { "已确认输出 ", "Confirmed output " },
            { "关闭输出未确认！请在仪器面板按 All Off。", "Output shutdown not confirmed! Press All Off on the instrument." },
            { "监测失败 · ", "Monitoring failed: " },
            { "程序中止；已确认输出 ", "Program aborted; confirmed output " },
            { "通信失败，无法确认加热已关闭！请在仪器面板按 All Off。", "Communication failed; heater shutdown cannot be confirmed! Press All Off on the instrument." },
            { "无法确认加热已关闭！请在仪器面板按 All Off。", "Heater shutdown cannot be confirmed! Press All Off on the instrument." },
            { "只支持输入 A–D、加热输出 1–2。", "Only inputs A-D and heater outputs 1-2 are supported." },
            { "请选择低、中或高加热档位。", "Select Low, Medium or High heater range." },
            { " 读数异常，状态码 ", " read error; status code " },
            { "目标温度达到仪器输入温度上限。", "Target reaches the input temperature limit." },
            { "目标温度必须低于仪器输入上限 ", "Target must be below the instrument input limit " },
            { "这个程序只支持 Lake Shore 336。实际设备：", "Only Lake Shore 336 is supported. Detected: " },
            { "温度回复数量异常；此版本按 A–D 四输入工作。", "Unexpected temperature response count; four inputs A-D are expected." },
            { "输出模式回复格式异常。", "Invalid output-mode response." },
            { "输入配置回复格式异常。", "Invalid input-configuration response." },
            { "关闭仪器内置 ramp 失败。", "Failed to disable the instrument ramp." },
            { "输出模式写入验证失败。", "Output-mode verification failed." },
            { "起始设定值写入验证失败。", "Initial setpoint verification failed." },
            { "用户请求停止，未启用输出。", "Stop requested; output was not enabled." },
            { "加热档位写入验证失败。", "Heater-range verification failed." },
            { "设定温度写入后回读不一致。", "Setpoint readback does not match the written value." },
            { "未能确认所有输出关闭。", "Could not confirm all outputs are off." },
            { "未能确认输出 ", "Could not confirm output " },
            { "SIMULATION · 演示设备（不连接任何硬件）", "SIMULATION (no hardware connection)" },
            { " 点的总超时为 ", " has timeout " },
            { " 点保持时间更新为 ", " point hold times updated to " },
            { " 分钟，已保存默认程序；", " minutes; saved as default; " },
            { "；恢复已计时 ", "; restored elapsed time " },
            { " 秒，未发送仪器控制命令。", " s; no instrument control commands sent." },
            { " / 档位 ", " / Range " },
            { " / 输出 ", " / Output " },
            { "，输入 ", ", input " },
            { "全部 ", "All " },
            { " 段 · ", " / " },
            { "第 ", "Step " },
            { " 行：", " row: " },
            { " 已关闭。", " is off." },
            { " 关闭。", " off." },
            { " 关闭", " off" },
            { "\n原因：", "\nReason: " },
            { "分", "m " },
            { "秒", "s" },
        };
        private static readonly Regex Tokens = new Regex(string.Join("|", Messages.Keys.OrderByDescending(s => s.Length).Select(Regex.Escape)), RegexOptions.Compiled);
        public static string T(string text)
        {
            if (!English || string.IsNullOrEmpty(text)) return text;
            string translated;
            if (Messages.TryGetValue(text, out translated)) return translated;
            return Tokens.Replace(text, match => Messages[match.Value]);
        }
        private static string PreferencePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LakeShoreControl", "ui-language.txt"); } }
        public static void LoadPreference()
        {
            try { english = !File.Exists(PreferencePath) || File.ReadAllText(PreferencePath).Trim() != "zh"; }
            catch (IOException) { english = true; }
            catch (UnauthorizedAccessException) { english = true; }
        }
        public static void SetLanguage(bool english, bool persist)
        {
            UiText.english = english;
            if (LanguageChanged != null) LanguageChanged();
            if (persist) { Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)); File.WriteAllText(PreferencePath, english ? "en" : "zh"); }
        }
        public static void Bind(Control control)
        {
            // User-entered numbers, ports and grid values must never be translated.
            if (control is Label || control is Button || control is Form || control is TabPage)
            {
                string original = control.Text;
                bool updating = false;
                Action render = () => { updating = true; try { control.Text = T(original); } finally { updating = false; } };
                control.TextChanged += (s, e) => { if (!updating) { original = control.Text; render(); } };
                LanguageChanged += render;
                control.Disposed += (s, e) => LanguageChanged -= render;
                render();
            }
            if (!string.IsNullOrEmpty(control.AccessibleName))
            {
                string name = control.AccessibleName;
                Action renderName = () => control.AccessibleName = T(name);
                LanguageChanged += renderName;
                control.Disposed += (s, e) => LanguageChanged -= renderName;
                renderName();
            }
            foreach (Control child in control.Controls) Bind(child);
            control.ControlAdded += (s, e) => Bind(e.Control);
        }
    }
    public sealed class LanguageChoice
    {
        public string Value { get; set; }
        public string Label { get; set; }
        public LanguageChoice(string value) { Value = value; Label = UiText.T(value); }
    }
    public static class UiMessageBox
    {
        public static DialogResult Show(IWin32Window owner, string text, string caption) { return Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None); }
        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) { return MessageBox.Show(owner, UiText.T(text), UiText.T(caption), buttons, icon); }
        public static DialogResult Show(string text, string caption) { return MessageBox.Show(UiText.T(text), UiText.T(caption)); }
    }
}

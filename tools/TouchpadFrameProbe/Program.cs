using System.Text;
using VoiDPlugins.Filter;

// Diagnostic probe for the touchpad drag feature. It reuses the exact listener the
// plugin installs inside OpenTabletDriver, so a failure here reproduces the plugin.
// Usage:
//   dotnet run --project tools/TouchpadFrameProbe
//   dotnet run --project tools/TouchpadFrameProbe -- --seconds 15
Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("Precision Control · 触控板帧探针 (TouchpadFrameProbe)");
Console.WriteLine("复用插件的 Raw Input 监听（0x0D/0x05, RIDEV_INPUTSINK | RIDEV_DEVNOTIFY）。");
Console.WriteLine("请把笔移出感应区，然后在触控板上用 3 指拖动；抬指会输出 count=0。");
Console.WriteLine();

var seconds = 0;
for (var index = 0; index < args.Length; index++)
{
    if (args[index] is "--seconds" or "-s" && index + 1 < args.Length &&
        int.TryParse(args[index + 1], out var parsed))
        seconds = Math.Max(1, parsed);
}

using var listener = new TouchpadRawInputListener();
if (!listener.IsRegistered)
{
    Console.WriteLine($"Raw Input 注册失败，Win32 错误 {listener.RegistrationError}。");
    Console.WriteLine("常见原因：在非交互会话/服务中运行，或该系统没有精确触控板。");
    return 2;
}

Console.WriteLine("监听已注册。Ctrl+C 退出" + (seconds > 0 ? $"，{seconds} 秒后自动退出。" : "。"));
Console.WriteLine();

var frames = 0L;
var started = DateTime.UtcNow;
listener.FrameReceived += frame =>
{
    frames++;
    var contacts = frame.Contacts;
    var text = new StringBuilder();
    text.Append($"[{frame.TimestampMilliseconds,10} ms] count={contacts.Count}");
    text.Append($" range={frame.HorizontalRange}x{frame.VerticalRange}");
    if (contacts.Count > 0)
    {
        var sumX = 0d;
        var sumY = 0d;
        text.Append(" | ");
        for (var index = 0; index < contacts.Count; index++)
        {
            var contact = contacts[index];
            if (index > 0)
                text.Append(", ");
            text.Append($"#{contact.ContactId}=({contact.X},{contact.Y})");
            sumX += contact.X;
            sumY += contact.Y;
        }

        text.Append($" | centroid=({sumX / contacts.Count:0.0},{sumY / contacts.Count:0.0})");
    }

    Console.WriteLine(text.ToString());
};

var deadline = seconds > 0 ? started.AddSeconds(seconds) : DateTime.MaxValue;
if (seconds > 0)
    Thread.Sleep(TimeSpan.FromSeconds(seconds));
else
    Thread.Sleep(Timeout.Infinite);

Console.WriteLine();
Console.WriteLine($"共收到 {frames} 帧。");
if (frames == 0)
    Console.WriteLine("没有任何帧：确认触控板是精确触控板、当前是交互桌面会话，并在触控板上实际触摸。");
else
    Console.WriteLine("能看到 count=3 且坐标随手指变化，说明输入层正常；下一步看插件日志中的 Touchpad drag started/ended。");

return 0;

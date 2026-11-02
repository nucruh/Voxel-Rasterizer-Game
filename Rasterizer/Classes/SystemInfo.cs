using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using System;
using System.Collections.Generic;
using System.Text;

public class SystemInfo
{
    private static MonitorInfo monitor = Monitors.GetPrimaryMonitor();
    
    public static Vector2i resolution = monitor.ClientArea.Size;
    public static Vector2i screenCenter = new Vector2i(resolution.X / 2, resolution.Y / 2);
    public static string gpuVendor = null!;
    public static string gpuRenderer = null!;
    public static string glslVersion = null!;
    public static int processorCount = Environment.ProcessorCount;
}
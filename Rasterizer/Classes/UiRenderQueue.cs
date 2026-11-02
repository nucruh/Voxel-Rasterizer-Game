using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

public static class UiRenderQueue
{
    private struct Item
    {
        public int Z;
        public int Order;
        public Action Draw;
    }

    private static readonly List<Item> _items = new();
    private static int _counter;

    /// <summary>Queue a draw action. Higher zIndex = drawn later = on top.</summary>
    public static void Submit(int zIndex, Action draw)
    {
        _items.Add(new Item { Z = zIndex, Order = _counter++, Draw = draw });
    }

    /// <summary>Call once per frame, after the 3D scene.</summary>
    public static void Flush()
    {
        if (_items.Count == 0) return;

        // Sort by z, ties broken by submission order (stable)
        _items.Sort((a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Order.CompareTo(b.Order));

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        foreach (var item in _items)
            item.Draw();

        GL.Enable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);

        _items.Clear();
        _counter = 0;
    }
}
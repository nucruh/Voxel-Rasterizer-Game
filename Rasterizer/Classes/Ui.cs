using System;
using System.Collections.Generic;

public abstract class UiElement
{
    public float X, Y;          // top-left, filled in by Layout
    public float W, H;          // size; 0 = auto (computed from children)
    public float Padding = 0;
    public int ZOffset = 0;
    public List<UiElement> Children = new();

    public UDim2 Position;                         // where to put it inside a Frame
    public UDim2 Size;                             // zero = auto size
    public (float x, float y) AnchorPoint = (0, 0);  // (0,0) top-left, (0.5,0.5) centre, (1,0) top-right

    public void Add(UiElement e) => Children.Add(e);   // enables { } initializer

    public abstract (float w, float h) Measure();
    public virtual void Layout(float x, float y) { X = x; Y = y; }
    public abstract void Draw(int z);
}

public class Frame : UiElement
{
    public BitmapFontRenderer.TextColor? Background;
    public float BackgroundAlpha = 0.6f;

    // A frame's size is given to it by its parent, so just report it
    public override (float, float) Measure() => (W, H);

    public override void Layout(float x, float y)
    {
        base.Layout(x, y);

        foreach (var c in Children)
        {
            // 1. resolve the child's size relative to THIS frame
            if (!c.Size.IsZero)
            {
                c.W = c.Size.SX * W + c.Size.OX;
                c.H = c.Size.SY * H + c.Size.OY;
            }

            // 2. measure it (Stack/Label use W,H if set, otherwise auto)
            var (cw, ch) = c.Measure();

            // 3. position = scale*parent + offset - anchor*ownSize
            float px = X + c.Position.SX * W + c.Position.OX - c.AnchorPoint.x * cw;
            float py = Y + c.Position.SY * H + c.Position.OY - c.AnchorPoint.y * ch;

            c.Layout(MathF.Round(px), MathF.Round(py));   // round: keeps pixel font crisp
        }
    }

    public override void Draw(int z)
    {
        if (Background is { } bg)
            BitmapFontRenderer.RenderRect(X, Y, W, H, bg, z + ZOffset, BackgroundAlpha);

        foreach (var c in Children)
            c.Draw(z + ZOffset + 1);
    }
}

public class Label : UiElement
{
    public string Text;
    public BitmapFontRenderer.TextColor Color = BitmapFontRenderer.white;

    public Label(string text) => Text = text;

    public override (float, float) Measure()
    {
        var (w, h) = BitmapFontRenderer.Measure(Text);
        return (W > 0 ? W : w, H > 0 ? H : h);
    }

    public override void Draw(int z)
    {
        BitmapFontRenderer.RenderTextPx(Text, X, Y, Color, z + ZOffset);
    }
}

public class Stack : UiElement
{
    public bool Horizontal = false;
    public float Gap = 4;
    public BitmapFontRenderer.TextColor? Background;
    public float BackgroundAlpha = 0.6f;

    public override (float, float) Measure()
    {
        float w = 0, h = 0;
        foreach (var c in Children)
        {
            var (cw, ch) = c.Measure();
            if (Horizontal) { w += cw + Gap; h = Math.Max(h, ch); }
            else { h += ch + Gap; w = Math.Max(w, cw); }
        }
        if (Children.Count > 0)
        {
            if (Horizontal) w -= Gap; else h -= Gap;   // no trailing gap
        }
        return (W > 0 ? W : w + Padding * 2,
                H > 0 ? H : h + Padding * 2);
    }

    public override void Layout(float x, float y)
    {
        base.Layout(x, y);

        var (tw, th) = Measure();
        W = tw; H = th;                       // remember own size for Draw

        float cx = x + Padding, cy = y + Padding;
        foreach (var c in Children)
        {
            c.Layout(cx, cy);                 // tell child where it goes
            var (cw, ch) = c.Measure();
            if (Horizontal) cx += cw + Gap;   // move right for next child
            else cy += ch + Gap;   // or move down
        }
    }

    public override void Draw(int z)
    {
        if (Background is { } bg)
            BitmapFontRenderer.RenderRect(X, Y, W, H, bg, z + ZOffset, BackgroundAlpha);

        foreach (var c in Children)
            c.Draw(z + ZOffset + 1);          // children sit above the background
    }
}

public static class Ui
{
    public static float MouseX, MouseY;
    public static bool MouseDown;
    public static bool Pressed;    // true only on the frame the button went down
    public static bool Released;   // true only on the frame it went up
    private static bool _prevDown;

    /// <summary>Call once per frame BEFORE Render/RenderScreen.</summary>
    public static void SetMouse(float x, float y, bool down)
    {
        MouseX = x; MouseY = y; MouseDown = down;
        Pressed = down && !_prevDown;
        Released = !down && _prevDown;
        _prevDown = down;
    }

    public static void Render(UiElement root, float x = 0, float y = 0, int z = 0)
    {
        root.Layout(x, y);
        root.Draw(z);
    }

    public static void RenderScreen(Frame root, int z = 0)
    {
        root.W = BitmapFontRenderer.WindowWidth;
        root.H = BitmapFontRenderer.WindowHeight;
        root.Layout(0, 0);
        root.Draw(z);
    }
}

public class Button : Frame
{
    public string Text = "";
    public Action? OnClick;

    public BitmapFontRenderer.TextColor TextColor = BitmapFontRenderer.white;
    public BitmapFontRenderer.TextColor NormalColor = new(50, 50, 70);
    public BitmapFontRenderer.TextColor HoverColor = new(75, 75, 105);
    public BitmapFontRenderer.TextColor PressedColor = new(30, 30, 45);

    private bool _held;          // pressed down inside this button
    private float _w, _h;        // resolved size for hit testing / drawing

    public Button(string text = "", Action? onClick = null)
    {
        Text = text;
        OnClick = onClick;
        Padding = 6;
    }

    // auto-size to text + padding unless a size was given
    public override (float, float) Measure()
    {
        var (tw, th) = BitmapFontRenderer.Measure(Text);
        return (W > 0 ? W : tw + Padding * 2,
                H > 0 ? H : th + Padding * 2);
    }

    public override void Layout(float x, float y)
    {
        X = x; Y = y;
        (_w, _h) = Measure();
    }

    public override void Draw(int z)
    {
        bool hover = Ui.MouseX >= X && Ui.MouseX < X + _w &&
                     Ui.MouseY >= Y && Ui.MouseY < Y + _h;

        if (hover && Ui.Pressed) _held = true;

        if (_held && Ui.Released)
        {
            if (hover) OnClick?.Invoke();
            _held = false;
        }
        if (!Ui.MouseDown) _held = false;

        var bg = (_held && hover) ? PressedColor : hover ? HoverColor : NormalColor;
        BitmapFontRenderer.RenderRect(X, Y, _w, _h, bg, z + ZOffset, 1f);

        var (tw, th) = BitmapFontRenderer.Measure(Text);
        BitmapFontRenderer.RenderTextPx(Text,
            MathF.Round(X + (_w - tw) / 2),
            MathF.Round(Y + (_h - th) / 2),
            TextColor, z + ZOffset + 1);
    }
}

public class Image : UiElement
{
    public int Texture;
    public BitmapFontRenderer.TextColor Tint = new(255, 255, 255);
    public float Alpha = 1f;

    // optional sub-rectangle in UV space (for atlases / sprite sheets)
    public float U0 = 0, V0 = 0, U1 = 1, V1 = 1;

    public Image(int texture) => Texture = texture;

    public override (float, float) Measure() => (W, H);   // set Size, or use Image.FromPath

    public override void Draw(int z)
        => ImageRenderer.Render(Texture, X, Y, W, H, Tint, Alpha, z + ZOffset, U0, V0, U1, V1);
}

// MMM LOVE ME SOME ROBLOX STYLE SHIT
public struct UDim2
{
    public float SX, OX, SY, OY;   // scale X, offset X, scale Y, offset Y
    public UDim2(float sx, float ox, float sy, float oy) { SX = sx; OX = ox; SY = sy; OY = oy; }
    public bool IsZero => SX == 0 && OX == 0 && SY == 0 && OY == 0;
}
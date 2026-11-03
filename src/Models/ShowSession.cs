using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PptSlider;

/// <summary>What covers the slide in the show: B / W like PowerPoint.</summary>
public enum Blank { None, Black, White }

/// <summary>
/// The slide show's state, shared by the show window, the presenter view and the ink overlay: the blank screen, the slide
/// number being typed, the laser dot, the spotlight and when the show started. The windows only show it.
/// </summary>
public sealed class ShowSession
{
    readonly DispatcherTimer jumpTimer = new();
    Gesture? jumpKey; // a lone digit: runs its own command (a pen color) if no Enter / second digit follows

    public ShowSession() => jumpTimer.Tick += OnJumpTimeout;

    public Blank Blanked { get; private set; }
    /// <summary>The slide number being typed in the show ("" when none).</summary>
    public string Jump { get; private set; } = "";
    public DateTime Start { get; private set; }
    /// <summary>The laser dot in slide coordinates, null when the mouse is off the slide; set by the show or the presenter view.</summary>
    public Point? LaserAt { get; private set; }
    /// <summary>A circle around the mouse pointer on the show screen (S), so the audience can see where it is. Kept between shows.</summary>
    public bool Spotlight { get; private set; }

    /// <summary>The blank screen, the typed slide number or the spotlight changed.</summary>
    public event Action? Changed;
    public event Action? LaserMoved;
    /// <summary>The blank screen changed (raised before <see cref="Changed"/>); a new blank screen starts without ink.</summary>
    public event Action? BlankChanged;
    /// <summary>A lone digit that nothing followed for a second: it runs its own command instead.</summary>
    public event Action<Gesture>? LoneKey;

    public void Begin() => Start = DateTime.Now;

    /// <summary>The show closed: no typed number, blank screen or laser dot left over.</summary>
    public void End()
    {
        jumpKey = null;
        SetJump("");
        SetBlank(Blank.None);
        SetLaser(null);
    }

    public void SetLaser(Point? p)
    {
        if (p == LaserAt) return;
        LaserAt = p;
        LaserMoved?.Invoke();
    }

    public void SetSpotlight(bool on)
    {
        if (on == Spotlight) return;
        Spotlight = on;
        Changed?.Invoke();
    }

    public void SetBlank(Blank b)
    {
        if (b == Blanked) return;
        Blanked = b;
        BlankChanged?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>B / W: that screen, or back to the slide when it is already showing.</summary>
    public void ToggleBlank(Blank b) => SetBlank(Blanked == b ? Blank.None : b);

    static string? Digit(Gesture g) => g.Mods != ModifierKeys.None ? null : g.Key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(g.Key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)(g.Key - Key.NumPad0)).ToString(),
        _ => null,
    };

    /// <summary>
    /// Typing a slide number: digits, then Enter goes there (<paramref name="goTo"/>, 1-based), Backspace and Esc edit it.
    /// True when the key was part of it; any other key drops the number and is left to its own command (false).
    /// </summary>
    public bool TypeKey(Gesture g, out int? goTo)
    {
        goTo = null;
        if (Digit(g) is { } d)
        {
            jumpKey = Jump == "" ? g : null;
            SetJump(Jump.Length < 4 ? Jump + d : Jump);
            return true;
        }
        if (Jump == "") return false;
        var typed = Jump;
        switch (g)
        {
            case { Key: Key.Enter, Mods: ModifierKeys.None }:
                SetJump("");
                SetBlank(Blank.None);
                if (int.TryParse(typed, out var n) && n >= 1) goTo = n;
                return true;
            case { Key: Key.Back, Mods: ModifierKeys.None }:
                jumpKey = null;
                SetJump(typed[..^1]);
                return true;
            case { Key: Key.Escape, Mods: ModifierKeys.None }:
                SetJump("");
                return true;
        }
        SetJump(""); // any other key drops the number and does its own thing
        return false;
    }

    void SetJump(string s)
    {
        Jump = s;
        jumpTimer.Stop();
        jumpTimer.Interval = TimeSpan.FromSeconds(jumpKey != null ? 1 : 4);
        if (s != "") jumpTimer.Start();
        Changed?.Invoke();
    }

    void OnJumpTimeout(object? sender, EventArgs e)
    {
        var lone = jumpKey;
        jumpKey = null;
        SetJump("");
        if (lone is { } k) LoneKey?.Invoke(k);
    }
}

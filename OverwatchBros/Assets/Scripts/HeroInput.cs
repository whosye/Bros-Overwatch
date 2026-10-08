using UnityEngine;
using UnityEngine.InputSystem;

// Vstup pro schopnosti hrdinu: u hrace klavesnice a mys, u bota (BotBrain) jeho "virtualni klavesy".
// Schopnosti se ptaji pres tuhle tridu, takze je muze pouzivat hrac i bot stejne.
public static class HeroInput
{
    public enum Key { Q, E, Shift, Space, LeftMouse, RightMouse }

    static BotBrain Bot(Component c) => c != null && c.TryGetComponent<BotBrain>(out var bot) ? bot : null;

    // Kurzor zamceny (hrac ve hre). Bot je "ve hre" vzdy.
    public static bool Locked(Component c) => Bot(c) != null || GameSettings.CursorLocked;

    public static bool Pressed(Component c, Key key)
    {
        var bot = Bot(c);
        if (bot != null) return bot.ConsumePress(key);
        switch (key)
        {
            case Key.Q: return Keyboard.current.qKey.wasPressedThisFrame;
            case Key.E: return Keyboard.current.eKey.wasPressedThisFrame;
            case Key.Shift: return Keyboard.current.leftShiftKey.wasPressedThisFrame;
            case Key.Space: return Keyboard.current.spaceKey.wasPressedThisFrame;
            case Key.LeftMouse: return Mouse.current.leftButton.wasPressedThisFrame;
            default: return Mouse.current.rightButton.wasPressedThisFrame;
        }
    }

    public static bool Held(Component c, Key key)
    {
        var bot = Bot(c);
        if (bot != null) return bot.IsHeld(key);
        switch (key)
        {
            case Key.Q: return Keyboard.current.qKey.isPressed;
            case Key.E: return Keyboard.current.eKey.isPressed;
            case Key.Shift: return Keyboard.current.leftShiftKey.isPressed;
            case Key.Space: return Keyboard.current.spaceKey.isPressed;
            case Key.LeftMouse: return Mouse.current.leftButton.isPressed;
            default: return Mouse.current.rightButton.isPressed;
        }
    }

    // Smer pohybu (WASD): x = doprava, y = dopredu.
    public static Vector2 Move(Component c)
    {
        var bot = Bot(c);
        if (bot != null) return bot.Move;
        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed) input.y -= 1f;
        if (Keyboard.current.dKey.isPressed) input.x += 1f;
        if (Keyboard.current.aKey.isPressed) input.x -= 1f;
        return input;
    }

    public static bool MoveKey(Component c, char key)
    {
        Vector2 m = Move(c);
        switch (key)
        {
            case 'w': return m.y > 0.3f;
            case 's': return m.y < -0.3f;
            case 'd': return m.x > 0.3f;
            default: return m.x < -0.3f;
        }
    }
}

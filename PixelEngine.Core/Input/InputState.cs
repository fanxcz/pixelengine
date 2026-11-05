using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace PixelEngine.Input;

/// <summary>Immutable input snapshot for one rendered frame (keyboard, mouse and first gamepad).</summary>
public sealed class InputState
{
    private KeyboardState _keyboard, _previousKeyboard;
    private MouseState _mouse, _previousMouse;
    private GamePadState _gamePad, _previousGamePad;

    internal void Poll()
    {
        _previousKeyboard = _keyboard;
        _previousMouse = _mouse;
        _previousGamePad = _gamePad;
        _keyboard = Keyboard.GetState();
        _mouse = Mouse.GetState();
        _gamePad = GamePad.GetState(PlayerIndex.One);
    }

    public bool IsKeyDown(Keys key) => _keyboard.IsKeyDown(key);
    public bool WasKeyPressed(Keys key) => _keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);
    public bool WasKeyReleased(Keys key) => _keyboard.IsKeyUp(key) && _previousKeyboard.IsKeyDown(key);
    public bool IsButtonDown(Buttons button) => _gamePad.IsButtonDown(button);
    public bool WasButtonPressed(Buttons button) => _gamePad.IsButtonDown(button) && !_previousGamePad.IsButtonDown(button);
    public bool WasButtonReleased(Buttons button) => !_gamePad.IsButtonDown(button) && _previousGamePad.IsButtonDown(button);
    public Vector2 LeftStick => _gamePad.ThumbSticks.Left;
    public Vector2 RightStick => _gamePad.ThumbSticks.Right;
    public float LeftTrigger => _gamePad.Triggers.Left;
    public float RightTrigger => _gamePad.Triggers.Right;
    public bool LeftMouseDown => _mouse.LeftButton == ButtonState.Pressed;
    public bool RightMouseDown => _mouse.RightButton == ButtonState.Pressed;
    public bool MiddleMouseDown => _mouse.MiddleButton == ButtonState.Pressed;
    public bool LeftMousePressed => LeftMouseDown && _previousMouse.LeftButton == ButtonState.Released;
    public bool LeftMouseReleased => !LeftMouseDown && _previousMouse.LeftButton == ButtonState.Pressed;
    public int MouseX => _mouse.X;
    public int MouseY => _mouse.Y;
    public Point MousePosition => new(_mouse.X, _mouse.Y);
    public int ScrollWheelDelta => _mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
}

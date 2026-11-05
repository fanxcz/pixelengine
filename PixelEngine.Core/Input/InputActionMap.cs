using Microsoft.Xna.Framework.Input;

namespace PixelEngine.Input;

/// <summary>Small rebindable action map for keyboard and gamepad controls.</summary>
public sealed class InputActionMap
{
    public const int MaximumActions = 64;
    public const int MaximumBindingsPerAction = 16;
    public const int MaximumActionNameLength = 64;

    private readonly Dictionary<string, HashSet<Keys>> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<Buttons>> _buttons = new(StringComparer.OrdinalIgnoreCase);

    public void BindKey(string action, Keys key)
    {
        action = NormalizeAction(action);
        if (key == Keys.None || !Enum.IsDefined(typeof(Keys), key))
            throw new ArgumentOutOfRangeException(nameof(key), "A valid keyboard key is required.");
        var set = GetSet(_keys, action);
        if (!set.Contains(key) && set.Count >= MaximumBindingsPerAction)
            throw new InvalidOperationException($"Action '{action}' already has the maximum number of keyboard bindings.");
        set.Add(key);
    }

    public void BindButton(string action, Buttons button)
    {
        action = NormalizeAction(action);
        if (button == Buttons.None || !Enum.IsDefined(typeof(Buttons), button))
            throw new ArgumentOutOfRangeException(nameof(button), "A valid gamepad button is required.");
        var set = GetSet(_buttons, action);
        if (!set.Contains(button) && set.Count >= MaximumBindingsPerAction)
            throw new InvalidOperationException($"Action '{action}' already has the maximum number of gamepad bindings.");
        set.Add(button);
    }

    public bool UnbindKey(string action, Keys key)
    {
        action = NormalizeAction(action);
        if (key == Keys.None || !Enum.IsDefined(typeof(Keys), key)) return false;
        if (!_keys.TryGetValue(action, out var set) || !set.Remove(key)) return false;
        if (set.Count == 0) _keys.Remove(action);
        return true;
    }

    public bool UnbindButton(string action, Buttons button)
    {
        action = NormalizeAction(action);
        if (button == Buttons.None || !Enum.IsDefined(typeof(Buttons), button)) return false;
        if (!_buttons.TryGetValue(action, out var set) || !set.Remove(button)) return false;
        if (set.Count == 0) _buttons.Remove(action);
        return true;
    }

    public void Clear(string action)
    {
        action = NormalizeAction(action);
        _keys.Remove(action);
        _buttons.Remove(action);
    }

    public void ClearAll()
    {
        _keys.Clear();
        _buttons.Clear();
    }

    public bool IsDown(InputState input, string action)
    {
        ArgumentNullException.ThrowIfNull(input);
        action = NormalizeAction(action);
        return _keys.TryGetValue(action, out var keys) && keys.Any(input.IsKeyDown)
            || _buttons.TryGetValue(action, out var buttons) && buttons.Any(input.IsButtonDown);
    }

    public bool WasPressed(InputState input, string action)
    {
        ArgumentNullException.ThrowIfNull(input);
        action = NormalizeAction(action);
        return _keys.TryGetValue(action, out var keys) && keys.Any(input.WasKeyPressed)
            || _buttons.TryGetValue(action, out var buttons) && buttons.Any(input.WasButtonPressed);
    }

    public bool WasReleased(InputState input, string action)
    {
        ArgumentNullException.ThrowIfNull(input);
        action = NormalizeAction(action);
        return _keys.TryGetValue(action, out var keys) && keys.Any(input.WasKeyReleased)
            || _buttons.TryGetValue(action, out var buttons) && buttons.Any(input.WasButtonReleased);
    }

    public IReadOnlyCollection<Keys> GetKeys(string action)
    {
        action = NormalizeAction(action);
        return _keys.TryGetValue(action, out var values) ? values.ToArray() : Array.Empty<Keys>();
    }

    public IReadOnlyCollection<Buttons> GetButtons(string action)
    {
        action = NormalizeAction(action);
        return _buttons.TryGetValue(action, out var values) ? values.ToArray() : Array.Empty<Buttons>();
    }

    public IReadOnlyCollection<string> GetActions()
        => _keys.Keys.Concat(_buttons.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    private static string NormalizeAction(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        action = action.Trim();
        if (action.Length > MaximumActionNameLength)
            throw new ArgumentOutOfRangeException(nameof(action), $"Action names are limited to {MaximumActionNameLength} characters.");
        return action;
    }

    private HashSet<T> GetSet<T>(Dictionary<string, HashSet<T>> map, string action)
    {
        if (!map.TryGetValue(action, out var set))
        {
            var actionCount = _keys.Keys.Concat(_buttons.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (actionCount >= MaximumActions && !_keys.ContainsKey(action) && !_buttons.ContainsKey(action))
                throw new InvalidOperationException($"An input map may contain at most {MaximumActions} actions.");
            map[action] = set = new HashSet<T>();
        }
        return set;
    }
}

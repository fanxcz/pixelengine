using PixelEngine.Serialization;

namespace PixelEngine.Editor;

/// <summary>Snapshot-based undo/redo for LIC authoring operations. Bounded to avoid unbounded memory use.</summary>
internal sealed class EditorHistory
{
    private const int MaximumEntries = 60;
    // Snapshots include Base64 assets. Keep both history stacks bounded by count and text size.
    private const long MaximumStackCharacters = 24L * 1024L * 1024L;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private string? _current;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public void Reset(LicProjectData project)
    {
        _undo.Clear();
        _redo.Clear();
        _current = LicProjectWriter.Serialize(project);
    }

    public bool Commit(LicProjectData project)
    {
        var next = LicProjectWriter.Serialize(project);
        if (string.Equals(next, _current, StringComparison.Ordinal)) return false;
        if (_current is not null)
        {
            _undo.Push(_current);
            Trim(_undo);
        }
        _redo.Clear();
        _current = next;
        return true;
    }

    public bool Undo(out LicProjectData? project)
    {
        project = null;
        if (_undo.Count == 0 || _current is null) return false;
        _redo.Push(_current);
        Trim(_redo);
        _current = _undo.Pop();
        project = LicProjectReader.Parse(_current);
        return true;
    }

    public bool Redo(out LicProjectData? project)
    {
        project = null;
        if (_redo.Count == 0 || _current is null) return false;
        _undo.Push(_current);
        Trim(_undo);
        _current = _redo.Pop();
        project = LicProjectReader.Parse(_current);
        return true;
    }

    private static void Trim(Stack<string> stack)
    {
        if (stack.Count == 0) return;
        var keepNewestFirst = new List<string>(Math.Min(stack.Count, MaximumEntries));
        long characters = 0;
        foreach (var snapshot in stack) // Stack enumerates newest to oldest.
        {
            if (keepNewestFirst.Count >= MaximumEntries) break;
            if (keepNewestFirst.Count > 0 && characters + snapshot.Length > MaximumStackCharacters) break;
            keepNewestFirst.Add(snapshot);
            characters += snapshot.Length;
            if (characters > MaximumStackCharacters) break; // Keep one latest snapshot even for a very large project.
        }
        stack.Clear();
        for (var i = keepNewestFirst.Count - 1; i >= 0; i--) stack.Push(keepNewestFirst[i]);
    }
}

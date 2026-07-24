using System;
using UnityEngine;

/// <summary>Circular buffer command stack with undo/redo. Not thread-safe (Unity main thread only).</summary>
public class CommandStack
{
    private readonly ICommand[] _buffer;
    private readonly int _maxCapacity;
    private int _head;       // Next write slot
    private int _count;      // Number of executed commands

    /// <summary>Action fired when a command is executed or revoked — pipeline hooks into this to rebuild.</summary>
    public Action<Bounds> OnStateChanged { get; set; }

    public CommandStack(int maxCapacity = 64)
    {
        _maxCapacity = maxCapacity;
        _buffer = new ICommand[maxCapacity];
        _head = 0;
        _count = 0;
    }

    /// <summary>Execute a command and push it onto the stack. Discards any redo history.</summary>
    public void Push(ICommand cmd)
    {
        if (cmd == null) return;

        // If we're at capacity, overwrite oldest but shift head
        if (_count >= _maxCapacity)
        {
            _head = _head % _maxCapacity;
            Array.Copy(_buffer, 1, _buffer, 0, _maxCapacity - 1);
            _count--;
        }

        cmd.Execute();
        _buffer[_head] = cmd;
        _head = (_head + 1) % _maxCapacity;
        _count++;

        // Notify pipeline to mark dirty
        OnStateChanged?.Invoke(cmd.AffectedBounds);
    }

    /// <summary>Revoke the last executed command. Returns false if stack is empty.</summary>
    public bool Undo()
    {
        if (_count == 0) return false;

        _head = (_head - 1 + _maxCapacity) % _maxCapacity;
        ICommand cmd = _buffer[_head];
        _buffer[_head] = null;
        _count--;

        cmd.Revoke();
        OnStateChanged?.Invoke(cmd.AffectedBounds);
        return true;
    }

    /// <summary>Re-execute the last undone command. Returns false if no redo available.</summary>
    public bool Redo()
    {
        // We don't keep a separate redo stack — once revoked, it's gone.
        // For full redo support we'd need a two-stack approach. Keep it simple for now.
        return false;
    }

    /// <summary>Clear the stack without revoking (e.g., on domain reload).</summary>
    public void Clear()
    {
        Array.Clear(_buffer, 0, _maxCapacity);
        _head = 0;
        _count = 0;
    }

    public int Count => _count;
    public bool CanUndo => _count > 0;
}

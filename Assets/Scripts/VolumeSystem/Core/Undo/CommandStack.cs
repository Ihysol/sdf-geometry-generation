using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Circular buffer command stack with undo/redo. Not thread-safe (Unity main thread only).</summary>
public class CommandStack
{
    private readonly ICommand[] _undoBuffer;
    private readonly ICommand[] _redoBuffer;
    private readonly int _maxCapacity;
    private int _undoHead;     // Next write slot in undo buffer
    private int _undoCount;    // Number of executed commands
    private int _redoHead;     // Next write slot in redo buffer  
    private int _redoCount;    // Number of revoked commands available

    /// <summary>Action fired when a command is executed or revoked — pipeline hooks into this to rebuild.</summary>
    public Action<Bounds> OnStateChanged { get; set; }

    public CommandStack(int maxCapacity = 64)
    {
        _maxCapacity = maxCapacity;
        _undoBuffer = new ICommand[maxCapacity];
        _redoBuffer = new ICommand[maxCapacity];
        _undoHead = 0; _undoCount = 0;
        _redoHead = 0; _redoCount = 0;
    }

    /// <summary>Execute a command and push it onto the undo stack. Discards any redo history.</summary>
    public void Push(ICommand cmd)
    {
        if (cmd == null) return;

        // Clear redo history — new action invalidates forward steps
        Array.Clear(_redoBuffer, 0, _redoCount);
        _redoHead = 0; _redoCount = 0;

        // If at capacity, shift oldest out
        if (_undoCount >= _maxCapacity)
        {
            _undoHead = _undoHead % _maxCapacity;
            Array.Copy(_undoBuffer, 1, _undoBuffer, 0, _maxCapacity - 1);
            _undoCount--;
        }

        cmd.Execute();
        _undoBuffer[_undoHead] = cmd;
        _undoHead = (_undoHead + 1) % _maxCapacity;
        _undoCount++;

        OnStateChanged?.Invoke(cmd.AffectedBounds);
    }

    /// <summary>Revoke the last executed command. Returns false if stack is empty.</summary>
    public bool Undo()
    {
        if (_undoCount == 0) return false;

        _undoHead = (_undoHead - 1 + _maxCapacity) % _maxCapacity;
        ICommand cmd = _undoBuffer[_undoHead];
        _undoBuffer[_undoHead] = null;
        _undoCount--;

        cmd.Revoke();

        // Push to redo buffer
        if (_redoCount < _maxCapacity)
        {
            _redoBuffer[_redoHead] = cmd;
            _redoHead = (_redoHead + 1) % _maxCapacity;
            _redoCount++;
        }

        OnStateChanged?.Invoke(cmd.AffectedBounds);
        return true;
    }

    /// <summary>Re-execute the last undone command. Returns false if no redo available.</summary>
    public bool Redo()
    {
        if (_redoCount == 0) return false;

        _redoHead = (_redoHead - 1 + _maxCapacity) % _maxCapacity;
        ICommand cmd = _redoBuffer[_redoHead];
        _redoBuffer[_redoHead] = null;
        _redoCount--;

        // Re-execute and push back to undo
        cmd.Execute();
        
        if (_undoCount < _maxCapacity)
        {
            _undoBuffer[_undoHead] = cmd;
            _undoHead = (_undoHead + 1) % _maxCapacity;
            _undoCount++;
        }

        OnStateChanged?.Invoke(cmd.AffectedBounds);
        return true;
    }

    /// <summary>Clear the stack without revoking (e.g., on domain reload).</summary>
    public void Clear()
    {
        Array.Clear(_undoBuffer, 0, _maxCapacity);
        Array.Clear(_redoBuffer, 0, _maxCapacity);
        _undoHead = 0; _undoCount = 0;
        _redoHead = 0; _redoCount = 0;
    }

    public int UndoCount => _undoCount;
    public int RedoCount => _redoCount;
    public bool CanUndo => _undoCount > 0;
    public bool CanRedo => _redoCount > 0;
}

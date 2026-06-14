namespace DotBoxD.Kernels.Interpreter;

using DotBoxD.Kernels;

// Unboxed i64 slot access, mirroring the i32/f64 raw-slot accessors. Used by I64ExpressionPlan / I64ForLoopRunner.
internal sealed partial class InterpreterFrame
{
    public bool IsI64Slot(int slot) => _layout.IsI64Slot(slot);

    public long ReadRawInt64Slot(int slot) => _i64Slots[slot];

    public void WriteRawInt64Slot(int slot, long value)
    {
        _i64Slots[slot] = value;
        _assigned[slot] = true;
    }

    public bool TryReadInt64(string name, out long value)
    {
        var slot = _layout.GetSlot(name);
        if (_layout.IsI64Slot(slot))
        {
            value = _assigned[slot] ? _i64Slots[slot] : 0;
            return _assigned[slot];
        }

        if (_slots[slot] is I64Value i64)
        {
            value = i64.Value;
            return true;
        }

        value = 0;
        return false;
    }
}

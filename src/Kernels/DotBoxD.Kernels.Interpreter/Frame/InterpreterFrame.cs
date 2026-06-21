using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Interpreter.Frame;

internal sealed partial class InterpreterFrame
{
    private readonly FunctionFrameLayout _layout;
    private readonly SandboxValue?[] _slots;
    private readonly int[] _i32Slots;
    private readonly long[] _i64Slots;
    private readonly double[] _f64Slots;
    private readonly bool[] _assigned;

    private InterpreterFrame(
        FunctionFrameLayout layout,
        SandboxValue?[] slots,
        int[] i32Slots,
        long[] i64Slots,
        double[] f64Slots,
        bool[] assigned)
    {
        _layout = layout;
        _slots = slots;
        _i32Slots = i32Slots;
        _i64Slots = i64Slots;
        _f64Slots = f64Slots;
        _assigned = assigned;
    }

    public string FunctionId => _layout.FunctionId;

    public int GetSlot(string name) => _layout.GetSlot(name);

    public SandboxValue Read(string name)
    {
        var slot = _layout.GetSlot(name);
        if (_layout.IsI32Slot(slot))
        {
            return _assigned[slot]
                ? SandboxValue.FromInt32(_i32Slots[slot])
                : throw Unassigned(name);
        }

        if (_layout.IsF64Slot(slot))
        {
            return _assigned[slot]
                ? SandboxValue.FromDouble(_f64Slots[slot])
                : throw Unassigned(name);
        }

        if (_layout.IsI64Slot(slot))
        {
            return _assigned[slot]
                ? SandboxValue.FromInt64(_i64Slots[slot])
                : throw Unassigned(name);
        }

        return _slots[slot]
            ?? throw Unassigned(name);
    }

    public void Write(string name, SandboxValue value)
    {
        var slot = _layout.GetSlot(name);
        if (_layout.IsI32Slot(slot))
        {
            _i32Slots[slot] = ((I32Value)value).Value;
        }
        else if (_layout.IsF64Slot(slot))
        {
            _f64Slots[slot] = ((F64Value)value).Value;
        }
        else if (_layout.IsI64Slot(slot))
        {
            _i64Slots[slot] = ((I64Value)value).Value;
        }
        else
        {
            _slots[slot] = value;
        }

        if (_layout.HasRawSlots)
        {
            _assigned[slot] = true;
        }
    }

    public bool CanReadInt32(string name)
    {
        var slot = _layout.GetSlot(name);
        return _layout.IsI32Slot(slot)
            ? _assigned[slot]
            : _slots[slot] is I32Value;
    }

    public bool IsInt32Local(string name) => _layout.IsI32Slot(name);

    public bool IsInt32Slot(int slot) => _layout.IsI32Slot(slot);

    public bool IsF64Slot(int slot) => _layout.IsF64Slot(slot);

    public bool IsF64Slot(string name) => _layout.IsF64Slot(name);

    public bool IsSlotAssigned(int slot)
        => _layout.HasRawSlots ? _assigned[slot] : _slots[slot] is not null;

    public int ReadInt32(string name)
    {
        var slot = _layout.GetSlot(name);
        if (_layout.IsI32Slot(slot))
        {
            return _assigned[slot] ? _i32Slots[slot] : throw Unassigned(name);
        }

        return _slots[slot] is I32Value value ? value.Value : throw Unassigned(name);
    }

    public int ReadInt32Slot(int slot)
        => _layout.IsI32Slot(slot)
            ? _assigned[slot] ? _i32Slots[slot] : throw UnassignedSlot()
            : _slots[slot] is I32Value value ? value.Value : throw UnassignedSlot();

    public int ReadRawInt32Slot(int slot) => _i32Slots[slot];

    public bool TryGetStringSlot(string name, out int slot)
    {
        slot = _layout.GetSlot(name);
        return _slots[slot] is StringValue;
    }

    public int ReadStringLengthSlot(int slot)
        => _slots[slot] is StringValue value ? value.Value.Length : throw UnassignedSlot();

    public bool TryGetListSlot(string name, out int slot)
    {
        slot = _layout.GetSlot(name);
        return _slots[slot] is ListValue;
    }

    public int ReadListCountSlot(int slot)
        => _slots[slot] is ListValue value ? value.Values.Count : throw UnassignedSlot();

    public int ReadListInt32ItemSlot(int slot, int index)
    {
        if (_slots[slot] is not ListValue list)
        {
            throw UnassignedSlot();
        }

        var values = list.Values;
        if (index < 0 || index >= values.Count)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "list index is out of range"));
        }

        return values[index] is I32Value item
            ? item.Value
            : throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "expected I32 value"));
    }

    public bool TryReadListInt32ItemsSlot(int slot, out int[] items)
    {
        if (_slots[slot] is not ListValue list)
        {
            items = [];
            return false;
        }

        var values = list.Values;
        items = new int[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not I32Value item)
            {
                items = [];
                return false;
            }

            items[i] = item.Value;
        }

        return true;
    }

    public bool TryGetMapSlot(string name, out int slot)
    {
        slot = _layout.GetSlot(name);
        return _slots[slot] is MapValue;
    }

    public int ReadMapCountSlot(int slot)
        => _slots[slot] is MapValue value ? value.Values.Count : throw UnassignedSlot();

    public int ReadMapInt32ValueSlot(int slot, SandboxValue key)
    {
        if (_slots[slot] is not MapValue map)
        {
            throw UnassignedSlot();
        }

        SandboxValueValidator.RequireType(key, map.KeyType, "map key type mismatch");
        if (!map.Values.TryGetValue(key, out var value))
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.NotFound,
                "map key was not found"));
        }

        return value is I32Value item
            ? item.Value
            : throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "expected I32 value"));
    }

    public bool TryReadDouble(string name, out double value)
    {
        var slot = _layout.GetSlot(name);
        if (_layout.IsF64Slot(slot))
        {
            value = _assigned[slot] ? _f64Slots[slot] : 0;
            return _assigned[slot];
        }

        if (_slots[slot] is F64Value f64)
        {
            value = f64.Value;
            return true;
        }

        value = 0;
        return false;
    }

    public double ReadDoubleSlot(int slot)
        => _layout.IsF64Slot(slot)
            ? _assigned[slot] ? _f64Slots[slot] : throw UnassignedSlot()
            : _slots[slot] is F64Value value ? value.Value : throw UnassignedSlot();

    public double ReadRawDoubleSlot(int slot) => _f64Slots[slot];

    public void WriteInt32(string name, int value)
    {
        var slot = _layout.GetSlot(name);
        if (!_layout.IsI32Slot(slot))
        {
            Write(name, SandboxValue.FromInt32(value));
            return;
        }

        _i32Slots[slot] = value;
        _assigned[slot] = true;
    }

    public void WriteInt32Slot(int slot, int value)
    {
        if (!_layout.IsI32Slot(slot))
        {
            _slots[slot] = SandboxValue.FromInt32(value);
            return;
        }

        _i32Slots[slot] = value;
        _assigned[slot] = true;
    }

    public void WriteRawInt32Slot(int slot, int value)
    {
        _i32Slots[slot] = value;
        _assigned[slot] = true;
    }

    public void WriteDoubleSlot(int slot, double value)
    {
        if (!_layout.IsF64Slot(slot))
        {
            _slots[slot] = SandboxValue.FromDouble(value);
            return;
        }

        _f64Slots[slot] = value;
        _assigned[slot] = true;
    }

    public void WriteRawDoubleSlot(int slot, double value)
    {
        _f64Slots[slot] = value;
        _assigned[slot] = true;
    }

    public static InterpreterFrame Create(
        FunctionFrameLayout layout,
        SandboxFunction function,
        IReadOnlyList<SandboxValue> args)
    {
        var slots = layout.SlotCount == 0
            ? System.Array.Empty<SandboxValue?>()
            : new SandboxValue?[layout.SlotCount];
        var i32Slots = layout.HasI32Slots ? new int[layout.SlotCount] : System.Array.Empty<int>();
        var i64Slots = layout.HasI64Slots ? new long[layout.SlotCount] : System.Array.Empty<long>();
        var f64Slots = layout.HasF64Slots ? new double[layout.SlotCount] : System.Array.Empty<double>();
        var assigned = layout.HasRawSlots ? new bool[layout.SlotCount] : System.Array.Empty<bool>();

        // Parameters occupy the leading slots in declaration order (see
        // FunctionFrameLayout.Build), so positional arguments map directly.
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (layout.IsI32Slot(i))
            {
                i32Slots[i] = ((I32Value)args[i]).Value;
            }
            else if (layout.IsF64Slot(i))
            {
                f64Slots[i] = ((F64Value)args[i]).Value;
            }
            else if (layout.IsI64Slot(i))
            {
                i64Slots[i] = ((I64Value)args[i]).Value;
            }
            else
            {
                slots[i] = args[i];
            }

            if (layout.HasRawSlots)
            {
                assigned[i] = true;
            }
        }

        return new InterpreterFrame(layout, slots, i32Slots, i64Slots, f64Slots, assigned);
    }

    private static SandboxRuntimeException Unassigned(string name)
        => new(new SandboxError(SandboxErrorCode.ValidationError, $"local '{name}' read before assignment"));

    private static SandboxRuntimeException UnassignedSlot()
        => new(new SandboxError(SandboxErrorCode.ValidationError, "local read before assignment"));
}

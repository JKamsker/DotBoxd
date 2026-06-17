using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression;

/// <summary>
/// Regression coverage for ALG-0019: live-setting updates coerce and range-validate
/// values twice — once in <see cref="LiveSettingStore"/> and again inside the slot's
/// <c>SetObject</c>. The store calls its private <c>CoerceAndValidate</c> (which runs
/// <c>LiveSettingTypeConverter.CoerceClr</c> + <c>ValidateRangeValue</c>) and then hands
/// the already-coerced value to <see cref="ILiveSetting.SetObject(object?)"/>, whose
/// production implementation repeats the same conversion and range check.
///
/// These tests inject a recording <see cref="ILiveSetting"/> through the public
/// <see cref="LiveSettingStore"/> constructor and observe exactly what the store passes
/// to the slot. The fix splits validation from storage so each value is coerced and
/// range-validated once per update; until then the store double-handles the value, so
/// the slot never sees the raw caller input and is asked to coerce a second time.
/// </summary>
public sealed class Fix_ALG_0019_Tests
{
    private const string IntType = "int";

    [Fact]
    public void SetObject_passes_raw_caller_value_to_slot_without_pre_coercing_in_store()
    {
        // Arrange: a slot for an "int" setting registered through the public store API.
        var slot = new RecordingLiveSetting(
            new LiveSettingDefinition("Damage", IntType, 0, Min: 0, Max: 100));
        var store = new LiveSettingStore([slot]);

        // Act: pass a string the store will coerce to int. After the fix, coercion lives
        // in a single place; the store should not coerce-and-validate before delegating.
        store.SetObject("Damage", "42");

        // Assert: the slot must receive the original, un-coerced caller value so that
        // coercion/validation happens exactly once. The buggy store coerces "42" -> 42
        // in CoerceAndValidate first, so the slot is handed a boxed int instead.
        Assert.Single(slot.ReceivedValues);
        Assert.Equal(
            "42",
            slot.ReceivedValues[0]); // currently fails: store delivers int 42, already coerced
    }

    [Fact]
    public void SetObject_invokes_slot_once_with_an_uncoerced_value()
    {
        // Arrange.
        var slot = new RecordingLiveSetting(
            new LiveSettingDefinition("Damage", IntType, 0, Min: 0, Max: 100));
        var store = new LiveSettingStore([slot]);

        // Act: pass a string the store will convert to int. The slot is meant to be the
        // single coercion/validation site for the "int" type.
        store.SetObject("Damage", "7");

        // Assert: the slot is handed the raw caller object (a string) exactly once and
        // not a value the store already coerced+validated. The buggy store delegates a
        // boxed int because CoerceAndValidate ran first.
        Assert.Single(slot.ReceivedValues);
        Assert.True(
            slot.LastReceivedWasUncoerced,
            "store coerced/validated the value before delegating, duplicating slot work");
    }

    [Fact]
    public void SetMany_collects_raw_values_and_delegates_each_setting_once()
    {
        // Arrange: two range-checked settings updated as a batch.
        var damage = new RecordingLiveSetting(
            new LiveSettingDefinition("Damage", IntType, 0, Min: 0, Max: 100));
        var armor = new RecordingLiveSetting(
            new LiveSettingDefinition("Armor", IntType, 0, Min: 0, Max: 100));
        var store = new LiveSettingStore([damage, armor]);

        // Act: SetMany pre-validates every value in CoerceAndValidate(values), then calls
        // each slot's SetObject again — double work while holding the store lock.
        store.SetMany(new Dictionary<string, object?>
        {
            ["Damage"] = "10",
            ["Armor"] = "20",
        });

        // Assert: each slot is the single coercion site and receives its raw caller value
        // exactly once. The buggy batch path coerces in the store first.
        Assert.Single(damage.ReceivedValues);
        Assert.Single(armor.ReceivedValues);
        Assert.Equal("10", damage.ReceivedValues[0]); // currently fails: store delivers int 10
        Assert.Equal("20", armor.ReceivedValues[0]);
    }

    [Fact]
    public void SetMany_does_not_partially_commit_when_later_custom_slot_fails()
    {
        var first = new LiveValue<int>("First", 1);
        var failing = new ThrowingLiveSetting("Second");
        var store = new LiveSettingStore([first, failing]);

        Assert.Throws<InvalidOperationException>(() => store.SetMany(
            new Dictionary<string, object?>
            {
                ["First"] = 2,
                ["Second"] = 3
            }));

        Assert.Equal(1, store.Get<int>("First"));
    }

    /// <summary>
    /// A test-only <see cref="ILiveSetting"/> that records every value the store delegates
    /// to it via <see cref="SetObject"/>, without performing any coercion or validation of
    /// its own. This isolates whether the store performed coercion before delegating.
    /// </summary>
    private sealed class RecordingLiveSetting : ILiveSetting
    {
        private readonly List<object?> _received = [];

        public RecordingLiveSetting(LiveSettingDefinition definition)
        {
            Definition = definition;
            CurrentValue = definition.DefaultValue;
        }

        public string Name => Definition.Name;

        public LiveSettingDefinition Definition { get; }

        public object? CurrentValue { get; private set; }

        public IReadOnlyList<object?> ReceivedValues => _received;

        public bool LastReceivedWasUncoerced { get; private set; }

        public SandboxValue ToSandboxValue() => SandboxValue.Unit;

        public void SetObject(object? value)
        {
            _received.Add(value);
            // The caller passed a string for an "int" setting. If the store already coerced
            // it, the slot observes a boxed int instead of the original string the caller
            // supplied, signalling that coercion happened twice (store + slot).
            LastReceivedWasUncoerced = value is string;
            CurrentValue = value;
        }
    }

    private sealed class ThrowingLiveSetting(string name) : ILiveSetting
    {
        public string Name => name;

        public LiveSettingDefinition Definition { get; } = new(name, IntType, 0);

        public object? CurrentValue => 0;

        public SandboxValue ToSandboxValue() => SandboxValue.FromInt32(0);

        public void SetObject(object? value) => throw new InvalidOperationException("boom");
    }
}

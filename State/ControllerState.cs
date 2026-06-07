using System;
using System.Runtime.InteropServices;
using System.Threading;
using ExileCore2;
using ExileCore2.Shared.Nodes;

namespace ReAgent.State;

[Api]
public class ControllerState
{
    private const uint ErrorSuccess = 0;
    private readonly Lazy<ControllerSkillBindings> _skills;
    private readonly XInputState _state;

    public ControllerState(GameController controller)
    {
        _skills = new Lazy<ControllerSkillBindings>(() => new ControllerSkillBindings(controller), LazyThreadSafetyMode.None);
        ResultCode = TryGetState(out _state);
    }

    [Api]
    public bool IsConnected => ResultCode == ErrorSuccess;

    public uint ResultCode { get; }

    [Api]
    public byte LeftTriggerPressure => IsConnected ? _state.Gamepad.LeftTrigger : (byte)0;

    [Api]
    public byte RightTriggerPressure => IsConnected ? _state.Gamepad.RightTrigger : (byte)0;

    [Api]
    public ControllerSkillBindings Skills => _skills.Value;

    [Api]
    public bool IsPressed(HotkeyNodeV2.ControllerKey key)
    {
        if (!IsConnected)
        {
            return false;
        }

        return key switch
        {
            HotkeyNodeV2.ControllerKey.A => IsButtonPressed(XInputButtons.A),
            HotkeyNodeV2.ControllerKey.B => IsButtonPressed(XInputButtons.B),
            HotkeyNodeV2.ControllerKey.X => IsButtonPressed(XInputButtons.X),
            HotkeyNodeV2.ControllerKey.Y => IsButtonPressed(XInputButtons.Y),
            HotkeyNodeV2.ControllerKey.Lb => IsButtonPressed(XInputButtons.LeftShoulder),
            HotkeyNodeV2.ControllerKey.Rb => IsButtonPressed(XInputButtons.RightShoulder),
            HotkeyNodeV2.ControllerKey.Ls => IsButtonPressed(XInputButtons.LeftThumb),
            HotkeyNodeV2.ControllerKey.Rs => IsButtonPressed(XInputButtons.RightThumb),
            HotkeyNodeV2.ControllerKey.Back => IsButtonPressed(XInputButtons.Back),
            HotkeyNodeV2.ControllerKey.Start => IsButtonPressed(XInputButtons.Start),
            HotkeyNodeV2.ControllerKey.Up => IsButtonPressed(XInputButtons.DPadUp),
            HotkeyNodeV2.ControllerKey.Right => IsButtonPressed(XInputButtons.DPadRight),
            HotkeyNodeV2.ControllerKey.Left => IsButtonPressed(XInputButtons.DPadLeft),
            HotkeyNodeV2.ControllerKey.Down => IsButtonPressed(XInputButtons.DPadDown),
            HotkeyNodeV2.ControllerKey.LTrigger => LeftTriggerPressure > 30,
            HotkeyNodeV2.ControllerKey.RTrigger => RightTriggerPressure > 30,
            _ => false
        };
    }

    private bool IsButtonPressed(XInputButtons button) => (_state.Gamepad.Buttons & (ushort)button) != 0;

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    private static uint TryGetState(out XInputState state)
    {
        try
        {
            return XInputGetState(0, out state);
        }
        catch
        {
            state = default;
            return uint.MaxValue;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [Flags]
    private enum XInputButtons : ushort
    {
        DPadUp = 0x0001,
        DPadDown = 0x0002,
        DPadLeft = 0x0004,
        DPadRight = 0x0008,
        Start = 0x0010,
        Back = 0x0020,
        LeftThumb = 0x0040,
        RightThumb = 0x0080,
        LeftShoulder = 0x0100,
        RightShoulder = 0x0200,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000
    }
}

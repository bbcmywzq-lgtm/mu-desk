using System.ComponentModel;
using System.Runtime.InteropServices;
using MouseRing.Interop;

namespace MouseRing.Services;

internal static class InputSender
{
    internal const ushort VkShift = 0x10;
    internal const ushort VkMenu = 0x12;
    internal const ushort VkTab = 0x09;
    internal const ushort VkLWin = 0x5B;
    internal const ushort VkD = 0x44;
    internal const ushort VkE = 0x45;
    internal const ushort VkS = 0x53;
    internal const ushort VkV = 0x56;
    internal const ushort VkVolumeMute = 0xAD;

    public static void SendChord(params ushort[] keys)
    {
        var inputs = new List<NativeMethods.INPUT>(keys.Length * 2);
        inputs.AddRange(keys.Select(CreateKeyDown));
        inputs.AddRange(keys.Reverse().Select(CreateKeyUp));
        Send(inputs.ToArray());
    }

    public static void SendRightButtonDown()
    {
        Send(
        [
            new NativeMethods.INPUT
            {
                Type = NativeMethods.InputMouse,
                Data = new NativeMethods.INPUTUNION
                {
                    Mouse = new NativeMethods.MOUSEINPUT { Flags = NativeMethods.MouseEventFRightDown },
                },
            },
        ]);
    }

    public static void SendRightClick()
    {
        Send(
        [
            new NativeMethods.INPUT
            {
                Type = NativeMethods.InputMouse,
                Data = new NativeMethods.INPUTUNION
                {
                    Mouse = new NativeMethods.MOUSEINPUT { Flags = NativeMethods.MouseEventFRightDown },
                },
            },
            new NativeMethods.INPUT
            {
                Type = NativeMethods.InputMouse,
                Data = new NativeMethods.INPUTUNION
                {
                    Mouse = new NativeMethods.MOUSEINPUT { Flags = NativeMethods.MouseEventFRightUp },
                },
            },
        ]);
    }

    private static NativeMethods.INPUT CreateKeyDown(ushort key) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Data = new NativeMethods.INPUTUNION
        {
            Keyboard = new NativeMethods.KEYBDINPUT { VirtualKey = key },
        },
    };

    private static NativeMethods.INPUT CreateKeyUp(ushort key) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Data = new NativeMethods.INPUTUNION
        {
            Keyboard = new NativeMethods.KEYBDINPUT
            {
                VirtualKey = key,
                Flags = NativeMethods.KeyEventFKeyUp,
            },
        },
    };

    private static void Send(NativeMethods.INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != (uint)inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}

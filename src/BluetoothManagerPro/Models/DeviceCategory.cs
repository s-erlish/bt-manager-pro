using System;

namespace BluetoothManagerPro.Models;

/// <summary>What the device *is*, used to pick an icon and to group the list.</summary>
public enum DeviceCategory
{
    Unknown,
    Headphones,
    Speaker,
    Microphone,
    Mouse,
    Keyboard,
    Gamepad,
    Phone,
    Computer,
    Wearable,
    Printer,
}

/// <summary>Which Bluetooth radio mode the endpoint was discovered on.</summary>
public enum DeviceTransport
{
    Classic,
    LowEnergy,
}

/// <summary>
/// Maps a Bluetooth Class of Device word onto <see cref="DeviceCategory"/>.
/// Layout per the Bluetooth assigned-numbers spec: bits 2..7 minor class,
/// bits 8..12 major class, bits 13..23 service classes.
/// </summary>
public static class ClassOfDeviceMap
{
    public static DeviceCategory ToCategory(uint classOfDevice)
    {
        uint major = (classOfDevice >> 8) & 0x1F;
        uint minor = (classOfDevice >> 2) & 0x3F;

        return major switch
        {
            1 => DeviceCategory.Computer,
            2 => DeviceCategory.Phone,
            4 => AudioVideo(minor),
            5 => Peripheral(minor),
            6 => DeviceCategory.Printer,
            7 => DeviceCategory.Wearable,
            _ => DeviceCategory.Unknown,
        };
    }

    /// <summary>
    /// LE endpoints usually report no Class of Device at all, so fall back to what the
    /// advertised name says. Crude, but it beats showing every earbud as a generic chip.
    /// </summary>
    public static DeviceCategory Refine(DeviceCategory category, string? name)
    {
        if (category != DeviceCategory.Unknown || string.IsNullOrWhiteSpace(name))
        {
            return category;
        }

        foreach ((string token, DeviceCategory guess) in NameHints)
        {
            if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return guess;
            }
        }

        return DeviceCategory.Unknown;
    }

    private static readonly (string Token, DeviceCategory Category)[] NameHints =
    {
        ("headphone", DeviceCategory.Headphones),
        ("headset", DeviceCategory.Headphones),
        ("earbud", DeviceCategory.Headphones),
        ("earphone", DeviceCategory.Headphones),
        ("airpod", DeviceCategory.Headphones),
        ("buds", DeviceCategory.Headphones),
        ("wh-", DeviceCategory.Headphones),
        ("wf-", DeviceCategory.Headphones),
        ("наушник", DeviceCategory.Headphones),
        ("speaker", DeviceCategory.Speaker),
        ("soundbar", DeviceCategory.Speaker),
        ("boombox", DeviceCategory.Speaker),
        ("колонк", DeviceCategory.Speaker),
        ("mouse", DeviceCategory.Mouse),
        ("мышь", DeviceCategory.Mouse),
        ("keyboard", DeviceCategory.Keyboard),
        ("клавиат", DeviceCategory.Keyboard),
        ("controller", DeviceCategory.Gamepad),
        ("gamepad", DeviceCategory.Gamepad),
        ("joystick", DeviceCategory.Gamepad),
        ("dualsense", DeviceCategory.Gamepad),
        ("xbox", DeviceCategory.Gamepad),
        ("watch", DeviceCategory.Wearable),
        ("band", DeviceCategory.Wearable),
        ("часы", DeviceCategory.Wearable),
        ("phone", DeviceCategory.Phone),
        ("iphone", DeviceCategory.Phone),
        ("galaxy s", DeviceCategory.Phone),
        ("pixel", DeviceCategory.Phone),
        ("printer", DeviceCategory.Printer),
    };

    private static DeviceCategory AudioVideo(uint minor) => minor switch
    {
        1 => DeviceCategory.Headphones,   // wearable headset
        2 => DeviceCategory.Headphones,   // hands-free
        4 => DeviceCategory.Microphone,
        5 => DeviceCategory.Speaker,      // loudspeaker
        6 => DeviceCategory.Headphones,
        7 => DeviceCategory.Speaker,      // portable audio
        8 => DeviceCategory.Speaker,      // car audio
        10 => DeviceCategory.Speaker,     // hi-fi audio
        18 => DeviceCategory.Gamepad,     // gaming / toy
        _ => DeviceCategory.Speaker,
    };

    private static DeviceCategory Peripheral(uint minor)
    {
        // Bits 6..7 of the class word are independent keyboard / pointer flags,
        // which land in bits 4..5 once the minor class has been shifted down.
        bool keyboard = (minor & 0x10) != 0;
        bool pointer = (minor & 0x20) != 0;

        if (keyboard && !pointer)
        {
            return DeviceCategory.Keyboard;
        }

        if (pointer && !keyboard)
        {
            return DeviceCategory.Mouse;
        }

        return (minor & 0x0F) switch
        {
            1 => DeviceCategory.Gamepad,  // joystick
            2 => DeviceCategory.Gamepad,
            _ => keyboard ? DeviceCategory.Keyboard : DeviceCategory.Unknown,
        };
    }
}

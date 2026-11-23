using System.Runtime.InteropServices;

namespace Vstavka.Platform.Linux;

internal static class X11Native
{
    private const string X11 = "libX11.so.6";
    private const string Xcb = "libxcb.so.1";
    private const string Xfixes = "libXfixes.so.3";
    private const string Xtst = "libXtst.so.6";

    public const int KeyPress = 2, PropertyNotify = 28, SelectionRequest = 30, SelectionNotify = 31;
    public const uint PropertyChangeMask = 1 << 22;
    public const uint EventMaskAttribute = 1 << 11;
    public const int PropertyNewValue = 0, PropertyDelete = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct Header
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public nint Display;
        public nuint Window;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OwnerEvent
    {
        public Header Header;
        public int Subtype;
        public nuint Owner;
        public nuint Selection;
        public nuint Time;
        public nuint SelectionTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RequestEvent
    {
        public Header Header;
        public nuint Requestor;
        public nuint Selection;
        public nuint Target;
        public nuint Property;
        public nuint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NotifyEvent
    {
        public Header Header;
        public nuint Selection;
        public nuint Target;
        public nuint Property;
        public nuint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyEvent
    {
        public Header Header;
        public nuint Atom;
        public nuint Time;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyEvent
    {
        public Header Header;
        public nuint Root;
        public nuint Child;
        public nuint Time;
        public int X, Y, RootX, RootY;
        public uint State, Keycode;
        public int SameScreen;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    public struct Event
    {
        [FieldOffset(0)] public Header Header;
        [FieldOffset(0)] public OwnerEvent Owner;
        [FieldOffset(0)] public RequestEvent Request;
        [FieldOffset(0)] public NotifyEvent Notify;
        [FieldOffset(0)] public PropertyEvent Property;
        [FieldOffset(0)] public KeyEvent Key;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyReply
    {
        public byte Type;
        public byte Format;
        public ushort Sequence;
        public uint Length;
        public uint Atom;
        public uint BytesAfter;
        public uint Count;
    }

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    public struct SelectionNotifyWire
    {
        public byte Type;
        public byte Padding;
        public ushort Sequence;
        public uint Time, Requestor, Selection, Target, Property;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ModifierMap
    {
        public int KeysPerModifier;
        public nint Keycodes;
    }

    [DllImport(X11)] public static extern nint XOpenDisplay(nint display);
    [DllImport(X11)] public static extern int XCloseDisplay(nint display);
    [DllImport(X11)] public static extern nuint XDefaultRootWindow(nint display);
    [DllImport(X11)] public static extern nuint XCreateSimpleWindow(nint display, nuint parent,
        int x, int y, uint width, uint height, uint borderWidth, nuint border, nuint background);
    [DllImport(X11)] public static extern int XDestroyWindow(nint display, nuint window);
    [DllImport(X11)] public static extern int XPending(nint display);
    [DllImport(X11)] public static extern int XNextEvent(nint display, out Event xevent);
    [DllImport(X11)] public static extern int XFlush(nint display);
    [DllImport(X11)] public static extern int XSelectInput(nint display, nuint window, nint mask);
    [DllImport(X11)] public static extern nuint XInternAtom(nint display,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int onlyIfExists);
    [DllImport(X11)] public static extern nuint XGetSelectionOwner(nint display, nuint selection);
    [DllImport(X11)] public static extern int XSetSelectionOwner(nint display, nuint selection, nuint owner, nuint time);
    [DllImport(X11)] public static extern int XConvertSelection(nint display, nuint selection, nuint target,
        nuint property, nuint requestor, nuint time);
    [DllImport(X11)] public static extern int XGetInputFocus(nint display, out nuint focus, out int revert);
    [DllImport(X11)] public static extern byte XKeysymToKeycode(nint display, nuint keysym);
    [DllImport(X11)] public static extern nint XGetModifierMapping(nint display);
    [DllImport(X11)] public static extern int XFreeModifiermap(nint modifierMap);
    [DllImport(X11)] public static extern int XUngrabKey(nint display, int keycode, uint modifiers, nuint window);
    [DllImport(Xfixes)] public static extern int XFixesQueryExtension(nint display, out int firstEvent, out int firstError);
    [DllImport(Xfixes)] public static extern int XFixesQueryVersion(nint display, out int major, out int minor);
    [DllImport(Xfixes)] public static extern void XFixesSelectSelectionInput(nint display, nuint window,
        nuint selection, nuint mask);
    [DllImport(Xtst)] public static extern int XTestQueryExtension(nint display, out int firstEvent, out int firstError,
        out int major, out int minor);
    [DllImport(Xtst)] public static extern int XTestFakeKeyEvent(nint display, uint keycode, int press, nuint delay);

    [DllImport("libX11-xcb.so.1")] public static extern nint XGetXCBConnection(nint display);
    [DllImport(Xcb)] public static extern uint xcb_grab_key_checked(nint connection, byte ownerEvents,
        uint window, ushort modifiers, byte keycode, byte pointerMode, byte keyboardMode);
    [DllImport(Xcb)] public static extern uint xcb_set_input_focus_checked(nint connection, byte revert,
        uint window, uint timestamp);
    [DllImport(Xcb)] public static extern uint xcb_get_property(nint connection, byte delete, uint window,
        uint property, uint type, uint offset, uint length);
    [DllImport(Xcb)] public static extern uint xcb_get_atom_name(nint connection, uint atom);
    [DllImport(Xcb)] public static extern nint xcb_get_atom_name_reply(nint connection, uint cookie, out nint error);
    [DllImport(Xcb)] public static extern nint xcb_get_atom_name_name(nint reply);
    [DllImport(Xcb)] public static extern int xcb_get_atom_name_name_length(nint reply);
    [DllImport(Xcb)] public static extern nint xcb_get_property_reply(nint connection, uint cookie, out nint error);
    [DllImport(Xcb)] public static extern nint xcb_get_property_value(nint reply);
    [DllImport(Xcb)] public static extern int xcb_get_property_value_length(nint reply);
    [DllImport(Xcb)] public static extern uint xcb_change_property_checked(nint connection, byte mode, uint window,
        uint property, uint type, byte format, uint units, byte[] data);
    [DllImport(Xcb)] public static extern uint xcb_change_window_attributes_checked(nint connection, uint window,
        uint mask, in uint value);
    [DllImport(Xcb)] public static extern uint xcb_send_event_checked(nint connection, byte propagate,
        uint window, uint eventMask, in SelectionNotifyWire xevent);
    [DllImport(Xcb)] public static extern nint xcb_request_check(nint connection, uint cookie);
    [DllImport("libc.so.6")] public static extern void free(nint pointer);
}

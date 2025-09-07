using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GHUBDriver.Classes;

/*
 * Made by fl0rixn
 * Only works with version 2021.10
*/

internal class GHub: IDisposable
{
    // NTSTATUS is an int. 0 == STATUS_SUCCESS.
    private const int STATUS_SUCCESS = 0;

    // NtCreateFile flags (subset)
    private const uint FILE_ATTRIBUTE_NORMAL = 0x0000_0080;
    private const uint FILE_NON_DIRECTORY_FILE = 0x0000_0040;
    private const uint FILE_SYNCHRONOUS_IO_NONALERT = 0x0000_0020;

    private const uint SYNCHRONIZE = 0x0010_0000;
    private const uint GENERIC_WRITE = 0x4000_0000;

    // CreateDisposition values
    private const uint FILE_OPEN = 0x00000001;

    // IOCTL used by this device
    private const uint IOCTL_UPDATE_MOUSE = 0x002A2010;

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_STATUS_BLOCK
    {
        public UIntPtr Status;          // NTSTATUS as pointer-sized
        public UIntPtr Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;           // in bytes, not chars
        public ushort MaximumLength;    // in bytes, not chars
        public IntPtr Buffer;           // PWSTR
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OBJECT_ATTRIBUTES
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;           // PUNICODE_STRING
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSE_IO
    {
        public byte Button;
        public byte X;
        public byte Y;
        public byte Wheel;
        public byte Reserved; // formerly Unk1
    }

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(
        out IntPtr FileHandle,
        uint DesiredAccess,
        ref OBJECT_ATTRIBUTES ObjectAttributes,
        ref IO_STATUS_BLOCK IoStatusBlock,
        IntPtr AllocationSize, // LARGE_INTEGER*
        uint FileAttributes,
        uint ShareAccess,
        uint CreateDisposition,
        uint CreateOptions,
        IntPtr EaBuffer,
        uint EaLength);

    [DllImport("ntdll.dll")]
    private static extern int NtDeviceIoControlFile(
        IntPtr FileHandle,
        IntPtr Event,
        IntPtr ApcRoutine,
        IntPtr ApcContext,
        ref IO_STATUS_BLOCK IoStatusBlock,
        uint IoControlCode,
        ref MOUSE_IO InputBuffer,
        int InputBufferLength,
        IntPtr OutputBuffer,
        int OutputBufferLength);

    [DllImport("ntdll.dll")]
    private static extern int ZwClose(IntPtr Handle);

    private IntPtr _deviceHandle = IntPtr.Zero;
    private bool _disposed;

    /// <summary>
    /// Opens the device handle if not already open
    /// </summary>
    /// <returns>true if the device was opened successfully; otherwise false.</returns>
    public bool Open()
    {
        ThrowIfDisposed();

        if (_deviceHandle != IntPtr.Zero)
            return true;

        // Try 10 instances, highest first
        for (int i = 9; i >= 0; i--)
        {
            var path = $@"\??\ROOT#SYSTEM#{"000" + i:D3}#{{1abc05c0-c378-41b9-9cef-df1aba82b015}}";
            int status = CreateFileByNt(path, out _deviceHandle);
            if (status == STATUS_SUCCESS && _deviceHandle != IntPtr.Zero)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Sends an update to the mouse device via IOCTL.
    /// Automatically attempts to reopen the device once if the call fails.
    /// </summary>
    public void UpdateMouse(int button, int x, int y, int wheel)
    {
        ThrowIfDisposed();

        EnsureOpenOrThrow();

        var payload = new MOUSE_IO
        {
            Button = ToByteClamp(button),
            X = ToByteClamp(x),
            Y = ToByteClamp(y),
            Wheel = ToByteClamp(wheel),
            Reserved = 0
        };

        if (!IoctlUpdateMouse(payload))
        {
            // Try one reopen
            Close();
            if (Open())
            {
                IoctlUpdateMouse(payload); // if this fails again, we just let it return
            }
        }
    }

    /// <summary>
    /// Closes the device handle if open.
    /// </summary>
    public void Close()
    {
        ThrowIfDisposed();

        if (_deviceHandle != IntPtr.Zero)
        {
            ZwClose(_deviceHandle);
            _deviceHandle = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~GHub()
    {
        if (_deviceHandle != IntPtr.Zero)
        {
            ZwClose(_deviceHandle);
            _deviceHandle = IntPtr.Zero;
        }
    }

    private static byte ToByteClamp(int value)
    {
        if (value < byte.MinValue) return byte.MinValue;
        if (value > byte.MaxValue) return byte.MaxValue;
        return (byte)value;
    }

    private void EnsureOpenOrThrow()
    {
        if (_deviceHandle == IntPtr.Zero && !Open())
        {
            throw new Win32Exception("Failed to open target device.");
        }
    }

    private bool IoctlUpdateMouse(in MOUSE_IO payload)
    {
        // NtDeviceIoControlFile requires a ref parameter; copy to local
        var local = payload;
        var iosb = new IO_STATUS_BLOCK();
        int status = NtDeviceIoControlFile(
            _deviceHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            ref iosb,
            IOCTL_UPDATE_MOUSE,
            ref local,
            Marshal.SizeOf<MOUSE_IO>(),
            IntPtr.Zero,
            0);

        return status == STATUS_SUCCESS;
    }

    private static int CreateFileByNt(string ntPath, out IntPtr handle)
    {
        IntPtr usPtr = IntPtr.Zero;
        IntPtr nameBuffer = IntPtr.Zero;
        try
        {
            // Allocate and populate UNICODE_STRING (unmanaged)
            var chars = ntPath.AsSpan();
            int byteLen = checked(chars.Length * 2);

            // Allocate unmanaged buffer
            nameBuffer = Marshal.AllocHGlobal(byteLen);

            byte[] ntPathBytes = System.Text.Encoding.Unicode.GetBytes(ntPath);
            Marshal.Copy(ntPathBytes, 0, nameBuffer, byteLen);

            var u = new UNICODE_STRING
            {
                Length = (ushort)byteLen,
                MaximumLength = (ushort)byteLen,
                Buffer = nameBuffer
            };

            usPtr = Marshal.AllocHGlobal(Marshal.SizeOf<UNICODE_STRING>());
            Marshal.StructureToPtr(u, usPtr, false);

            var oa = new OBJECT_ATTRIBUTES
            {
                Length = Marshal.SizeOf<OBJECT_ATTRIBUTES>(),
                RootDirectory = IntPtr.Zero,
                ObjectName = usPtr,
                Attributes = 0, // OBJ_CASE_INSENSITIVE would be 0x40; leave as-is unless required
                SecurityDescriptor = IntPtr.Zero,
                SecurityQualityOfService = IntPtr.Zero
            };

            var iosb = new IO_STATUS_BLOCK();

            // Open up
            int status = NtCreateFile(
                out handle,
                GENERIC_WRITE | SYNCHRONIZE,
                ref oa,
                ref iosb,
                IntPtr.Zero,
                FILE_ATTRIBUTE_NORMAL,
                0,                          // ShareAccess
                FILE_OPEN,                  // CreateDisposition (original code used 3; FILE_OPEN is 1)
                FILE_NON_DIRECTORY_FILE | FILE_SYNCHRONOUS_IO_NONALERT,
                IntPtr.Zero,
                0);

            return status;
        }
        finally
        {
            // Free unmanaged memory
            if (usPtr != IntPtr.Zero)
            {
                Marshal.DestroyStructure<UNICODE_STRING>(usPtr);
                Marshal.FreeHGlobal(usPtr);
            }
            if (nameBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(nameBuffer);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GHub));
    }
}
using System;
using System.Runtime.InteropServices;

namespace EvDevSharp
{
    unsafe class NativeUnsafeMethods
    {
        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        public static extern int open(string pathname, int flags, int mode);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        public static extern int close(int fd);

        [DllImport("libc", EntryPoint = "mmap", SetLastError = true)]
        public static extern IntPtr mmap(IntPtr addr, IntPtr length, int prot, int flags,
            int fd, IntPtr offset);
        [DllImport("libc", EntryPoint = "munmap", SetLastError = true)]
        public static extern int munmap(IntPtr addr, IntPtr length);

        [DllImport("libc", EntryPoint = "memcpy", SetLastError = true)]
        public static extern int memcpy(IntPtr dest, IntPtr src, IntPtr length);

        [DllImport("libc", EntryPoint = "select", SetLastError = true)]
        public static extern int select(int nfds, void* rfds, void* wfds, void* exfds, IntPtr* timevals);

        [DllImport("libc", SetLastError = true)]
        public static extern int ioctl(int fd, uint request, void* data);
        
        [DllImport("libc", SetLastError = true)]
        public static extern int ioctl(int fd, uint request, [Out]byte[] data);
    }
}
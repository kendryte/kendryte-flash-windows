using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using PInvoke;

namespace Canaan.Kendryte.Flash.Shell.Services
{
    public class TerminalService
    {
        private readonly Kernel32.SafeObjectHandle _job;
        private readonly string _plinkFile;
        private readonly Kernel32.SafeObjectHandle _plinkHandle;
        private Process _process;

        public TerminalService()
        {
            _job = Kernel32.CreateJobObject(IntPtr.Zero, null);

            var limit = new Kernel32.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limit.BasicLimitInformation.LimitFlags = Kernel32.JOB_OBJECT_LIMIT_FLAGS.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            unsafe
            {
                if (!Kernel32.SetInformationJobObject(_job, Kernel32.JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, &limit, (uint)sizeof(Kernel32.JOBOBJECT_EXTENDED_LIMIT_INFORMATION)))
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            _plinkFile = Path.GetTempFileName();
            File.SetAttributes(_plinkFile, File.GetAttributes(_plinkFile) | FileAttributes.Temporary);
            _plinkHandle = ExtractPlink();
        }

        public void Start(string port, int baudRate, uint chip)
        {
            Stop();
            var arg = $"-serial {port} -sercfg {baudRate},8,1,N,N";
            if (chip == 3)
                arg += " -mem";

            _process = Process.Start(new ProcessStartInfo(_plinkFile, arg)
            {
                UseShellExecute = false
            });
            
            System.Threading.Thread.Sleep(100);
            User32.SetWindowText(_process.MainWindowHandle, "K-Flash Terminal");
            Kernel32.AssignProcessToJobObject(_job, new Kernel32.SafeObjectHandle(_process.Handle, false));
        }

        public void Stop()
        {
            if (_process != null)
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit();
                }

                _process.Dispose();
                _process = null;
            }
        }

        private Kernel32.SafeObjectHandle ExtractPlink()
        {
            using (Stream dst = File.OpenWrite(_plinkFile),
                src = new GZipStream(typeof(TerminalService).Assembly.GetManifestResourceStream("Canaan.Kendryte.Flash.Shell.Assets.plink.exe.gz"), CompressionMode.Decompress))
            {
                src.CopyTo(dst);
            }

            var handle = Kernel32.CreateFile(_plinkFile, new Kernel32.ACCESS_MASK(0), Kernel32.FileShare.FILE_SHARE_READ | Kernel32.FileShare.FILE_SHARE_DELETE, IntPtr.Zero,
                Kernel32.CreationDisposition.OPEN_EXISTING, Kernel32.CreateFileFlags.FILE_ATTRIBUTE_TEMPORARY | Kernel32.CreateFileFlags.FILE_FLAG_DELETE_ON_CLOSE, Kernel32.SafeObjectHandle.Null);
            if (handle.IsInvalid)
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            return handle;
        }
    }
}

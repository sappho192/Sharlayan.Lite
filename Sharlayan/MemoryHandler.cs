// --------------------------------------------------------------------------------------------------------------------
// <copyright file="MemoryHandler.cs" company="SyndicatedLife">
//   Copyright© 2007 - 2022 Ryan Wilson <syndicated.life@gmail.com> (https://syndicated.life/)
//   Licensed under the MIT license. See LICENSE.md in the solution root for full license information.
// </copyright>
// <summary>
//   MemoryHandler.cs Implementation
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Sharlayan {
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks;

    using NLog;

    using Sharlayan.Models;
    using Sharlayan.Models.Structures;
    using Sharlayan.Utilities;

    public class MemoryHandler : IDisposable {
        public delegate void ExceptionEvent(object sender, Logger logger, Exception ex);

        public delegate void MemoryHandlerDisposedEvent(object sender);

        public delegate void MemoryLocationsFoundEvent(object sender, ConcurrentDictionary<string, MemoryLocation> memoryLocations, long processingTime);

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private bool _isNewInstance = true;

        private bool _ownsProcessHandle;

        internal ClearingArrayPool<byte> BufferPool = new ClearingArrayPool<byte>();

        public MemoryHandler(SharlayanConfiguration configuration) {
            this.Configuration = configuration;
            this.ProcessHandle = UnsafeNativeMethods.OpenProcess(UnsafeNativeMethods.ProcessAccessFlags.PROCESS_VM_READ_QUERY, false, (uint) this.Configuration.ProcessModel.ProcessID);
            this._ownsProcessHandle = this.ProcessHandle != IntPtr.Zero;
            if (this.ProcessHandle == IntPtr.Zero) {
                this.ProcessHandle = UnsafeNativeMethods.OpenProcess(UnsafeNativeMethods.ProcessAccessFlags.PROCESS_VM_ALL, false, (uint) this.Configuration.ProcessModel.ProcessID);
                this._ownsProcessHandle = this.ProcessHandle != IntPtr.Zero;
            }

            if (this.ProcessHandle == IntPtr.Zero) {
                this.ProcessHandle = this.Configuration.ProcessModel.Process.Handle;
                this._ownsProcessHandle = false;
            }

            this.IsAttached = this.ProcessHandle != IntPtr.Zero;

            this.Configuration.ProcessModel.Process.EnableRaisingEvents = true;
            this.Configuration.ProcessModel.Process.Exited += this.Process_OnExited;

            this.GetProcessModules();

            this.Scanner = new Scanner(this);
            this.Reader = new Reader(this);

            if (this._isNewInstance) {
                this._isNewInstance = false;

                Task.Run(
                    async () => {
                        await this.ResolveMemoryStructures();
                    })
                    .ContinueWith(
                        task => {
                            Logger.Error(task.Exception, "Background structure resolution faulted.");
                            this.RaiseException(Logger, task.Exception);
                        },
                        TaskContinuationOptions.OnlyOnFaulted);
            }

            Task.Run(
                async () => {
                    Signature[] signatures = await Signatures.Resolve(this.Configuration);
                    this.Scanner.LoadOffsets(signatures, this.Configuration.ScanAllRegions);
                })
                .ContinueWith(
                    task => {
                        Logger.Error(task.Exception, "Background signature resolution faulted.");
                        this.RaiseException(Logger, task.Exception);
                    },
                    TaskContinuationOptions.OnlyOnFaulted);
        }

        public SharlayanConfiguration Configuration { get; set; }

        private volatile bool _isAttached;

        internal bool IsAttached {
            get => this._isAttached;
            set => this._isAttached = value;
        }

        public Reader Reader { get; set; }

        public long ScanCount { get; set; }

        public Scanner Scanner { get; }

        internal IntPtr ProcessHandle { get; set; }

        internal StructuresContainer Structures { get; set; } = new StructuresContainer();

        private List<ProcessModule> _systemModules { get; } = new List<ProcessModule>();

        public void Dispose() {
            try {
                if (this.IsAttached && this._ownsProcessHandle && this.ProcessHandle != IntPtr.Zero) {
                    UnsafeNativeMethods.CloseHandle(this.ProcessHandle);
                }
            }
            catch (Exception) {
                // IGNORED
            }
            finally {
                this.IsAttached = false;
                this.ProcessHandle = IntPtr.Zero;
                this.RaiseMemoryHandlerDisposed();
                GC.SuppressFinalize(this);
            }
        }

        ~MemoryHandler() {
            this.Dispose();
        }

        public event ExceptionEvent OnException = delegate { };

        public event MemoryHandlerDisposedEvent OnMemoryHandlerDisposed = delegate { };

        public event MemoryLocationsFoundEvent OnMemoryLocationsFound = delegate { };

        [ThreadStatic]
        private static byte[] _singleByteBuffer;

        public byte GetByte(IntPtr address, long offset = 0) {
            if (_singleByteBuffer == null) {
                _singleByteBuffer = new byte[1];
            }

            return this.Peek(new IntPtr(address.ToInt64() + offset), _singleByteBuffer) ? _singleByteBuffer[0] : (byte) 0;
        }

        public byte[] GetByteArray(IntPtr address, int length) {
            byte[] data = new byte[length];
            this.Peek(address, data);
            return data;
        }

        public void GetByteArray(IntPtr address, byte[] destination) {
            this.Peek(address, destination);
        }

        public void GetByteArray(IntPtr address, byte[] destination, int count) {
            this.Peek(address, destination, count);
        }

        [ThreadStatic]
        private static byte[] _twoByteBuffer;

        [ThreadStatic]
        private static byte[] _fourByteBuffer;

        [ThreadStatic]
        private static byte[] _eightByteBuffer;

        public short GetInt16(IntPtr address, long offset = 0) {
            if (_twoByteBuffer == null) {
                _twoByteBuffer = new byte[2];
            }

            return this.Peek(new IntPtr(address.ToInt64() + offset), _twoByteBuffer) ? SharlayanBitConverter.TryToInt16(_twoByteBuffer, 0) : (short) 0;
        }

        public int GetInt32(IntPtr address, long offset = 0) {
            if (_fourByteBuffer == null) {
                _fourByteBuffer = new byte[4];
            }

            return this.Peek(new IntPtr(address.ToInt64() + offset), _fourByteBuffer) ? SharlayanBitConverter.TryToInt32(_fourByteBuffer, 0) : 0;
        }

        public long GetInt64(IntPtr address, long offset = 0) {
            if (_eightByteBuffer == null) {
                _eightByteBuffer = new byte[8];
            }

            return this.Peek(new IntPtr(address.ToInt64() + offset), _eightByteBuffer) ? SharlayanBitConverter.TryToInt64(_eightByteBuffer, 0) : 0;
        }

        public long GetInt64FromBytes(byte[] source, int index = 0) {
            return SharlayanBitConverter.TryToInt64(source, index);
        }

        public IntPtr GetStaticAddress(long offset) {
            ProcessModule processMainModule = this.Configuration.ProcessModel.Process.MainModule;
            if (processMainModule != null) {
                return new IntPtr(processMainModule.BaseAddress.ToInt64() + offset);
            }

            return IntPtr.Zero;
        }

        public string GetString(IntPtr address, long offset = 0, int size = 256) {
            byte[] bytes = this.BufferPool.Rent(size);
            try {
                if (!this.Peek(new IntPtr(address.ToInt64() + offset), bytes, size)) {
                    return string.Empty;
                }

                return DecodeString(bytes, 0, size);
            }
            finally {
                this.BufferPool.Return(bytes);
            }
        }

        public string GetStringFromBytes(byte[] source, int offset = 0, int size = 256) {
            return DecodeString(source, offset, size);
        }

        internal static string DecodeString(byte[] source, int offset, int size) {
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }

            if (offset < 0 || offset > source.Length) {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (size < 0) {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            int count = Math.Min(size, source.Length - offset);
            int stringLength = count;
            for (int i = 0; i < count; i++) {
                if (source[offset + i] == 0) {
                    stringLength = i;
                    break;
                }
            }

            return Encoding.UTF8.GetString(source, offset, stringLength);
        }

        public T GetStructure<T>(IntPtr address, int offset = 0) {
            IntPtr buffer = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(T)));
            try {
                if (!UnsafeNativeMethods.ReadProcessMemory(this.ProcessHandle, address + offset, buffer, new IntPtr(Marshal.SizeOf(typeof(T))), out IntPtr _)) {
                    return default(T);
                }

                return (T) Marshal.PtrToStructure(buffer, typeof(T));
            }
            finally {
                Marshal.FreeCoTaskMem(buffer);
            }
        }

        public ushort GetUInt16(IntPtr address, long offset = 0) {
            if (_twoByteBuffer == null) {
                _twoByteBuffer = new byte[2];
            }

            return this.Peek(new IntPtr(address.ToInt64() + offset), _twoByteBuffer) ? SharlayanBitConverter.TryToUInt16(_twoByteBuffer, 0) : (ushort) 0;
        }

        public uint GetUInt32(IntPtr address, long offset = 0) {
            if (_fourByteBuffer == null) {
                _fourByteBuffer = new byte[4];
            }

            return this.Peek(new IntPtr(address.ToInt64() + offset), _fourByteBuffer) ? SharlayanBitConverter.TryToUInt32(_fourByteBuffer, 0) : 0;
        }

        public ulong GetUInt64(IntPtr address, long offset = 0) {
            if (_eightByteBuffer == null) {
                _eightByteBuffer = new byte[8];
            }

            return this.Peek(new IntPtr(address.ToInt64() + offset), _eightByteBuffer) ? SharlayanBitConverter.TryToUInt64(_eightByteBuffer, 0) : 0;
        }

        public ulong GetUInt64FromBytes(byte[] source, int index = 0) {
            return SharlayanBitConverter.TryToUInt64(source, index);
        }

        public bool Peek(IntPtr address, byte[] buffer) {
            return UnsafeNativeMethods.ReadProcessMemory(this.ProcessHandle, address, buffer, new IntPtr(buffer.Length), out IntPtr bytesRead);
        }

        public bool Peek(IntPtr address, byte[] buffer, int count) {
            if (buffer == null) {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (count < 0 || count > buffer.Length) {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            return UnsafeNativeMethods.ReadProcessMemory(this.ProcessHandle, address, buffer, new IntPtr(count), out IntPtr bytesRead);
        }

        public IntPtr ReadPointer(IntPtr address, long offset = 0) {
            if (_eightByteBuffer == null) {
                _eightByteBuffer = new byte[8];
            }

            return this.Peek(new IntPtr(address.ToInt64() + offset), _eightByteBuffer)
                       ? new IntPtr(SharlayanBitConverter.TryToInt64(_eightByteBuffer, 0))
                       : IntPtr.Zero;
        }

        public IntPtr ResolvePointerPath(IEnumerable<long> path, IntPtr baseAddress, bool IsASMSignature = false) {
            IntPtr nextAddress = baseAddress;
            foreach (long offset in path) {
                try {
                    baseAddress = new IntPtr(nextAddress.ToInt64() + offset);
                    if (baseAddress == IntPtr.Zero) {
                        return IntPtr.Zero;
                    }

                    if (IsASMSignature) {
                        nextAddress = baseAddress + this.GetInt32(new IntPtr(baseAddress.ToInt64())) + 4;
                        IsASMSignature = false;
                    }
                    else {
                        nextAddress = this.ReadPointer(baseAddress);
                    }
                }
                catch {
                    return IntPtr.Zero;
                }
            }

            return baseAddress;
        }

        internal ProcessModule GetModuleByAddress(IntPtr address) {
            try {
                foreach (ProcessModule module in this._systemModules) {
                    long baseAddress = module.BaseAddress.ToInt64();
                    if (baseAddress <= (long)address && baseAddress + module.ModuleMemorySize >= (long)address) {
                        return module;
                    }
                }

                return null;
            }
            catch (Exception) {
                return null;
            }
        }

        internal bool IsSystemModule(IntPtr address) {
            ProcessModule moduleByAddress = this.GetModuleByAddress(address);
            if (moduleByAddress == null) {
                return false;
            }

            foreach (ProcessModule module in this._systemModules) {
                if (module.ModuleName == moduleByAddress.ModuleName) {
                    return true;
                }
            }

            return false;
        }

        internal async Task ResolveMemoryStructures() {
            this.Structures = await APIHelper.GetStructures(this.Configuration);
        }

        protected internal virtual void RaiseException(Logger logger, Exception ex) {
            this.OnException?.Invoke(this, logger, ex);
        }

        protected internal virtual void RaiseMemoryHandlerDisposed() {
            this.OnMemoryHandlerDisposed?.Invoke(this);
        }

        protected internal virtual void RaiseMemoryLocationsFound(ConcurrentDictionary<string, MemoryLocation> memoryLocations, long processingTime) {
            this.OnMemoryLocationsFound?.Invoke(this, memoryLocations, processingTime);
        }

        private void GetProcessModules() {
            ProcessModuleCollection modules = this.Configuration.ProcessModel.Process.Modules;

            foreach (ProcessModule module in modules) {
                this._systemModules.Add(module);
            }
        }

        private void Process_OnExited(object sender, EventArgs e) {
            if (!SharlayanMemoryManager.Instance.RemoveHandler(this.Configuration.ProcessModel.ProcessID)) {
                this.Dispose();
            }

            this.Configuration.ProcessModel.Process.Exited -= this.Process_OnExited;
        }
    }
}

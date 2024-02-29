// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using WinRT;

namespace ABI.System.ComponentModel
{
#if EMBED
    internal
#else
    public
#endif
    static class INotifyPropertyChangedMethods
    {
        public static global::System.Guid IID { get; } = new Guid(new global::System.ReadOnlySpan<byte>(new byte[] { 0x01, 0x76, 0xB1, 0x90, 0x65, 0xB0, 0x6E, 0x58, 0x83, 0xD9, 0x9A, 0xDC, 0x3A, 0x69, 0x52, 0x84 }));

        public static IntPtr AbiToProjectionVftablePtr => INotifyPropertyChanged.Vftbl.AbiToProjectionVftablePtr;

        private volatile static global::System.Runtime.CompilerServices.ConditionalWeakTable<object, PropertyChangedEventSource> _PropertyChanged;
        private static global::System.Runtime.CompilerServices.ConditionalWeakTable<object, PropertyChangedEventSource> MakePropertyChangedTable()
        {
            global::System.Threading.Interlocked.CompareExchange(ref _PropertyChanged, new(), null);
            return _PropertyChanged;
        }
        private static global::System.Runtime.CompilerServices.ConditionalWeakTable<object, PropertyChangedEventSource> PropertyChanged => _PropertyChanged ?? MakePropertyChangedTable();

        public static unsafe (global::System.Action<global::System.ComponentModel.PropertyChangedEventHandler>,
                              global::System.Action<global::System.ComponentModel.PropertyChangedEventHandler>)
            Get_PropertyChanged(IObjectReference obj, object thisObj)
        {
            var eventSource = PropertyChanged.GetValue(thisObj, (key) =>
            {
                var ThisPtr = obj.ThisPtr;

                return new PropertyChangedEventSource(obj,
                    (*(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, global::WinRT.EventRegistrationToken*, int>**)ThisPtr)[6],
                    (*(delegate* unmanaged[Stdcall]<IntPtr, global::WinRT.EventRegistrationToken, int>**)ThisPtr)[7]);
            });
            return eventSource.EventActions;
        }
    }

    [DynamicInterfaceCastableImplementation]
    [Guid("90B17601-B065-586E-83D9-9ADC3A695284")]
    internal unsafe interface INotifyPropertyChanged : global::System.ComponentModel.INotifyPropertyChanged
    {
        [Guid("90B17601-B065-586E-83D9-9ADC3A695284")]
        [StructLayout(LayoutKind.Sequential)]
#pragma warning disable CA2257 // This member is a type (so it cannot be invoked)
        public struct Vftbl
#pragma warning restore CA2257
        {
            internal IInspectable.Vftbl IInspectableVftbl;

            private delegate* unmanaged<IntPtr, IntPtr, global::WinRT.EventRegistrationToken*, int> _add_PropertyChanged_0;
            public delegate* unmanaged[Stdcall]<IntPtr, IntPtr, global::WinRT.EventRegistrationToken*, int> add_PropertyChanged_0 { get => (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, global::WinRT.EventRegistrationToken*, int>)_add_PropertyChanged_0; set => _add_PropertyChanged_0 = (delegate* unmanaged<IntPtr, IntPtr, global::WinRT.EventRegistrationToken*, int>)value; }
            private delegate* unmanaged<IntPtr, global::WinRT.EventRegistrationToken, int> _remove_PropertyChanged_1;
            public delegate* unmanaged[Stdcall]<IntPtr, global::WinRT.EventRegistrationToken, int> remove_PropertyChanged_1 { get => (delegate* unmanaged[Stdcall]<IntPtr, global::WinRT.EventRegistrationToken, int>)_remove_PropertyChanged_1; set => _remove_PropertyChanged_1 = (delegate* unmanaged<IntPtr, global::WinRT.EventRegistrationToken, int>)value; }

            private static readonly Vftbl AbiToProjectionVftable;
            public static readonly IntPtr AbiToProjectionVftablePtr;

            static unsafe Vftbl()
            {
                AbiToProjectionVftable = new Vftbl
                {
                    IInspectableVftbl = global::WinRT.IInspectable.Vftbl.AbiToProjectionVftable,

                    _add_PropertyChanged_0 = &Do_Abi_add_PropertyChanged_0,
                    _remove_PropertyChanged_1 = &Do_Abi_remove_PropertyChanged_1,

                };
                var nativeVftbl = (IntPtr*)ComWrappersSupport.AllocateVtableMemory(typeof(Vftbl), sizeof(global::WinRT.IInspectable.Vftbl) + sizeof(IntPtr) * 2);
                Marshal.StructureToPtr(AbiToProjectionVftable, (IntPtr)nativeVftbl, false);
                AbiToProjectionVftablePtr = (IntPtr)nativeVftbl;
            }
            private volatile static global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.ComponentModel.INotifyPropertyChanged, global::WinRT.EventRegistrationTokenTable<global::System.ComponentModel.PropertyChangedEventHandler>> _PropertyChanged_TokenTablesLazy = null;

            private static global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.ComponentModel.INotifyPropertyChanged, global::WinRT.EventRegistrationTokenTable<global::System.ComponentModel.PropertyChangedEventHandler>> MakeConditionalWeakTable()
            {
                global::System.Threading.Interlocked.CompareExchange(ref _PropertyChanged_TokenTablesLazy, new(), null);
                return _PropertyChanged_TokenTablesLazy;
            }

            private static global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.ComponentModel.INotifyPropertyChanged, global::WinRT.EventRegistrationTokenTable<global::System.ComponentModel.PropertyChangedEventHandler>> _PropertyChanged_TokenTables => _PropertyChanged_TokenTablesLazy ?? MakeConditionalWeakTable();

            [UnmanagedCallersOnly]
            private static unsafe int Do_Abi_add_PropertyChanged_0(IntPtr thisPtr, IntPtr handler, global::WinRT.EventRegistrationToken* token)
            {
                *token = default;
                try
                {
                    var __this = global::WinRT.ComWrappersSupport.FindObject<global::System.ComponentModel.INotifyPropertyChanged>(thisPtr);
                    var __handler = global::ABI.System.ComponentModel.PropertyChangedEventHandler.FromAbi(handler);
                    *token = _PropertyChanged_TokenTables.GetOrCreateValue(__this).AddEventHandler(__handler);
                    __this.PropertyChanged += __handler;
                    return 0;
                }
                catch (global::System.Exception __ex)
                {
                    return __ex.HResult;
                }
            }

            [UnmanagedCallersOnly]
            private static unsafe int Do_Abi_remove_PropertyChanged_1(IntPtr thisPtr, global::WinRT.EventRegistrationToken token)
            {
                try
                {
                    var __this = global::WinRT.ComWrappersSupport.FindObject<global::System.ComponentModel.INotifyPropertyChanged>(thisPtr);
                    if (__this != null && _PropertyChanged_TokenTables.TryGetValue(__this, out var __table) && __table.RemoveEventHandler(token, out var __handler))
                    {
                        __this.PropertyChanged -= __handler;
                    }
                    return 0;
                }
                catch (global::System.Exception __ex)
                {
                    return __ex.HResult;
                }
            }
        }
        internal static ObjectReference<Vftbl> FromAbi(IntPtr thisPtr) => ObjectReference<Vftbl>.FromAbi(thisPtr);

        private static (global::System.Action<global::System.ComponentModel.PropertyChangedEventHandler>,
                        global::System.Action<global::System.ComponentModel.PropertyChangedEventHandler>)
            _PropertyChanged(IWinRTObject _this)
        {
            var _obj = _this.GetObjectReferenceForType(typeof(global::System.ComponentModel.INotifyPropertyChanged).TypeHandle);
            return INotifyPropertyChangedMethods.Get_PropertyChanged(_obj, _this);
        }

        event global::System.ComponentModel.PropertyChangedEventHandler global::System.ComponentModel.INotifyPropertyChanged.PropertyChanged
        {
            add => _PropertyChanged((IWinRTObject)this).Item1(value);
            remove => _PropertyChanged((IWinRTObject)this).Item2(value);
        }
    }
}

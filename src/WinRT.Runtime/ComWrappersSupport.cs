// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using ABI.Microsoft.UI.Xaml.Data;
using ABI.Windows.Foundation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WinRT.Interop;

#if NET
using ComInterfaceEntry = System.Runtime.InteropServices.ComWrappers.ComInterfaceEntry;
#endif

#pragma warning disable 0169 // The field 'xxx' is never used
#pragma warning disable 0649 // Field 'xxx' is never assigned to, and will always have its default value

namespace WinRT
{
#if EMBED
    internal
#else
    public 
#endif
    static partial class ComWrappersSupport
    {
        private readonly static ConcurrentDictionary<string, Func<IInspectable, object>> TypedObjectFactoryCacheForRuntimeClassName = new ConcurrentDictionary<string, Func<IInspectable, object>>(StringComparer.Ordinal);
        private readonly static ConcurrentDictionary<Type, Func<IInspectable, object>> TypedObjectFactoryCacheForType = new ConcurrentDictionary<Type, Func<IInspectable, object>>();
        private readonly static ConditionalWeakTable<object, object> CCWTable = new ConditionalWeakTable<object, object>();
        private readonly static ConcurrentDictionary<Type, Func<IntPtr, object>> DelegateFactoryCache = new ConcurrentDictionary<Type, Func<IntPtr, object>>();

        public static TReturn MarshalDelegateInvoke<TDelegate, TReturn>(IntPtr thisPtr, Func<TDelegate, TReturn> invoke)
            where TDelegate : class, Delegate
        {
#if !NET
            using (new Mono.ThreadContext())
#endif
            {
                var target_invoke = FindObject<TDelegate>(thisPtr);
                if (target_invoke != null)
                {
                    return invoke(target_invoke);
                }
                return default;
            }
        }

        public static void MarshalDelegateInvoke<T>(IntPtr thisPtr, Action<T> invoke)
            where T : class, Delegate
        {
#if !NET
            using (new Mono.ThreadContext())
#endif
            {
                var target_invoke = FindObject<T>(thisPtr);
                if (target_invoke != null)
                {
                    invoke(target_invoke);
                }
            }
        }

        // If we are free threaded, we do not need to keep track of context.
        // This can either be if the object implements IAgileObject or the free threaded marshaler.
        internal unsafe static bool IsFreeThreaded(IObjectReference objRef)
        {
            if (objRef.TryAs(ABI.WinRT.Interop.IAgileObject.IID, out var agilePtr) >= 0)
            {
                Marshal.Release(agilePtr);
                return true;
            }
            else if (objRef.TryAs<ABI.WinRT.Interop.IMarshal.Vftbl>(ABI.WinRT.Interop.IMarshal.IID, out var marshalRef) >= 0)
            {
                using (marshalRef)
                {
                    Guid iid_IUnknown = IUnknownVftbl.IID;
                    Guid iid_unmarshalClass;
                    Marshal.ThrowExceptionForHR(marshalRef.Vftbl.GetUnmarshalClass_0(
                        marshalRef.ThisPtr, &iid_IUnknown, IntPtr.Zero, MSHCTX.InProc, IntPtr.Zero, MSHLFLAGS.Normal, &iid_unmarshalClass));
                    if (iid_unmarshalClass == ABI.WinRT.Interop.IMarshal.IID_InProcFreeThreadedMarshaler.Value)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static IObjectReference GetObjectReferenceForInterface(IntPtr externalComObject)
        {
            return GetObjectReferenceForInterface<IUnknownVftbl>(externalComObject);
        }

        public static ObjectReference<T> GetObjectReferenceForInterface<T>(IntPtr externalComObject)
        {
            if (externalComObject == IntPtr.Zero)
            {
                return null;
            }

            ObjectReference<T> objRef = ObjectReference<T>.FromAbi(externalComObject);
            if (IsFreeThreaded(objRef))
            {
                return objRef;
            }
            else
            {
                using (objRef)
                {
                    return new ObjectReferenceWithContext<T>(
                        objRef.GetRef(),
                        Context.GetContextCallback(),
                        Context.GetContextToken());
                }
            }
        }

        public static void RegisterProjectionAssembly(Assembly assembly) => TypeNameSupport.RegisterProjectionAssembly(assembly);

        internal static object GetRuntimeClassCCWTypeIfAny(object obj)
        {
            var type = obj.GetType();
            var ccwType = type.GetRuntimeClassCCWType();
            if (ccwType != null)
            {
                return CCWTable.GetValue(obj, obj => {
                    var ccwConstructor = ccwType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.CreateInstance | BindingFlags.Instance, null, new[] { type }, null);
                    return ccwConstructor.Invoke(new[] { obj });
                });
            }

            return obj;
        }

        internal static List<ComInterfaceEntry> GetInterfaceTableEntries(Type type)
        {
            var entries = new List<ComInterfaceEntry>();
            var objType = type.GetRuntimeClassCCWType() ?? type;
            var interfaces = objType.GetInterfaces();
            bool hasCustomIMarshalInterface = false;
            foreach (var iface in interfaces)
            {
                if (Projections.IsTypeWindowsRuntimeType(iface))
                {
                    var ifaceAbiType = iface.FindHelperType();
                    Guid iid = GuidGenerator.GetIID(ifaceAbiType);
                    entries.Add(new ComInterfaceEntry
                    {
                        IID = iid,
                        Vtable = (IntPtr)ifaceAbiType.GetAbiToProjectionVftblPtr()
                    });

                    if(!hasCustomIMarshalInterface && iid == typeof(ABI.WinRT.Interop.IMarshal.Vftbl).GUID)
                    {
                        hasCustomIMarshalInterface = true;
                    }
                }

                if (iface.IsConstructedGenericType
                    && Projections.TryGetCompatibleWindowsRuntimeTypesForVariantType(iface, out var compatibleIfaces))
                {
                    foreach (var compatibleIface in compatibleIfaces)
                    {
                        var compatibleIfaceAbiType = compatibleIface.FindHelperType();
                        entries.Add(new ComInterfaceEntry
                        {
                            IID = GuidGenerator.GetIID(compatibleIfaceAbiType),
                            Vtable = (IntPtr)compatibleIfaceAbiType.GetAbiToProjectionVftblPtr()
                        });
                    }
                }
            }

            if (type.IsDelegate())
            {
                var helperType = type.FindHelperType();
                if (helperType is object)
                {
                    entries.Add(new ComInterfaceEntry
                    {
                        IID = GuidGenerator.GetIID(type),
                        Vtable = (IntPtr)helperType.GetAbiToProjectionVftblPtr()
                    });
                }
            }

            if (objType.IsGenericType && objType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.KeyValuePair<,>))
            {
                var ifaceAbiType = objType.FindHelperType();
                entries.Add(new ComInterfaceEntry
                {
                    IID = GuidGenerator.GetIID(ifaceAbiType),
                    Vtable = (IntPtr)ifaceAbiType.GetAbiToProjectionVftblPtr()
                });
            }
            else if (ShouldProvideIReference(type))
            {
                entries.Add(IPropertyValueEntry);
                entries.Add(ProvideIReference(type));
            }
            else if (ShouldProvideIReferenceArray(type))
            {
                entries.Add(IPropertyValueEntry);
                entries.Add(ProvideIReferenceArray(type));
            }

            entries.Add(new ComInterfaceEntry
            {
                IID = typeof(ManagedIStringableVftbl).GUID,
                Vtable = ManagedIStringableVftbl.AbiToProjectionVftablePtr
            });

            entries.Add(new ComInterfaceEntry
            {
                IID = typeof(ManagedCustomPropertyProviderVftbl).GUID,
                Vtable = ManagedCustomPropertyProviderVftbl.AbiToProjectionVftablePtr
            });

            entries.Add(new ComInterfaceEntry
            {
                IID = ABI.WinRT.Interop.IWeakReferenceSource.IID,
                Vtable = ABI.WinRT.Interop.IWeakReferenceSource.AbiToProjectionVftablePtr
            });

            // Add IMarhal implemented using the free threaded marshaler
            // to all CCWs if it doesn't already have its own.
            if (!hasCustomIMarshalInterface)
            {
                entries.Add(new ComInterfaceEntry
                {
                    IID = typeof(ABI.WinRT.Interop.IMarshal.Vftbl).GUID,
                    Vtable = ABI.WinRT.Interop.IMarshal.Vftbl.AbiToProjectionVftablePtr
                });
            }

            // Add IAgileObject to all CCWs
            entries.Add(new ComInterfaceEntry
            {
                IID = typeof(ABI.WinRT.Interop.IAgileObject.Vftbl).GUID,
                Vtable = IUnknownVftbl.AbiToProjectionVftblPtr
            });
            return entries;
        }

        internal static (InspectableInfo inspectableInfo, List<ComInterfaceEntry> interfaceTableEntries) PregenerateNativeTypeInformation(Type type)
        {
            var interfaceTableEntries = GetInterfaceTableEntries(type);
            var iids = new Guid[interfaceTableEntries.Count];
            for (int i = 0; i < interfaceTableEntries.Count; i++)
            {
                iids[i] = interfaceTableEntries[i].IID;
            }

            if (type.FullName.StartsWith("ABI.", StringComparison.Ordinal))
            {
                type = Projections.FindCustomPublicTypeForAbiType(type) ?? type.Assembly.GetType(type.FullName.Substring("ABI.".Length)) ?? type;
            }

            return (
                new InspectableInfo(type, iids),
                interfaceTableEntries);
        }

        private static bool IsNullableT(Type implementationType)
        {
            return implementationType.IsGenericType && implementationType.GetGenericTypeDefinition() == typeof(System.Nullable<>);
        }

        private static bool IsAbiNullableDelegate(Type implementationType)
        {
            return implementationType.IsGenericType && implementationType.GetGenericTypeDefinition() == typeof(ABI.System.Nullable_Delegate<>);
        }

        private static bool IsIReferenceArray(Type implementationType)
        {
            return implementationType.FullName.StartsWith("Windows.Foundation.IReferenceArray`1", StringComparison.Ordinal);
        }

        private static Func<IInspectable, object> CreateKeyValuePairFactory(Type type)
        {
            var parms = new[] { Expression.Parameter(typeof(IInspectable), "obj") };
            return Expression.Lambda<Func<IInspectable, object>>(
                Expression.Call(type.GetHelperType().GetMethod("CreateRcw", BindingFlags.Public | BindingFlags.Static), 
                    parms), parms).Compile();
        }

        internal static Func<IntPtr, object> CreateDelegateFactory(Type type)
        {
            return DelegateFactoryCache.GetOrAdd(type, (type) =>
            {
                var parms = new[] { Expression.Parameter(typeof(IntPtr), "ptr") };
                return Expression.Lambda<Func<IntPtr, object>>(
                    Expression.Call(type.GetHelperType().GetMethod("CreateRcw", BindingFlags.Public | BindingFlags.Static),
                        parms), parms).Compile();
            });
        }

        private static Func<IInspectable, object> CreateNullableTFactory(Type implementationType)
        {
            Type helperType = implementationType.GetHelperType();

            ParameterExpression[] parms = new[] { Expression.Parameter(typeof(IInspectable), "inspectable") };
            return Expression.Lambda<Func<IInspectable, object>>(
                Expression.Convert(Expression.Call(helperType.GetMethod("GetValue", BindingFlags.Static | BindingFlags.NonPublic), 
                    parms), typeof(object)), parms).Compile();
        }

        private static Func<IInspectable, object> CreateAbiNullableTFactory(Type implementationType)
        {
            ParameterExpression[] parms = new[] { Expression.Parameter(typeof(IInspectable), "inspectable") };
            return Expression.Lambda<Func<IInspectable, object>>(
                Expression.Convert(Expression.Call(implementationType.GetMethod("GetValue", BindingFlags.Static | BindingFlags.NonPublic),
                    parms), typeof(object)), parms).Compile();
        }

        private static Func<IInspectable, object> CreateArrayFactory(Type implementationType)
        {
            Type helperType = implementationType.GetHelperType();
            Type vftblType = helperType.FindVftblType();

            ParameterExpression[] parms = new[] { Expression.Parameter(typeof(IInspectable), "inspectable") };
            var createInterfaceInstanceExpression = Expression.New(helperType.GetConstructor(new[] { typeof(ObjectReference<>).MakeGenericType(vftblType) }),
                    Expression.Call(parms[0],
                        typeof(IInspectable).GetMethod(nameof(IInspectable.As)).MakeGenericMethod(vftblType)));

            return Expression.Lambda<Func<IInspectable, object>>(
                Expression.Property(createInterfaceInstanceExpression, "Value"), parms).Compile();
        }

        // This is used to hold the reference to the native value type object (IReference) until the actual value in it (boxed as an object) gets cleaned up by GC
        // This is done to avoid pointer reuse until GC cleans up the boxed object
        private static ConditionalWeakTable<object, IInspectable> _boxedValueReferenceCache = new();

        private static Func<IInspectable, object> CreateReferenceCachingFactory(Func<IInspectable, object> internalFactory)
        {
            return inspectable =>
            {
                object resultingObject = internalFactory(inspectable);
                _boxedValueReferenceCache.Add(resultingObject, inspectable);
                return resultingObject;
            };
        }

        private static Func<IInspectable, object> CreateCustomTypeMappingFactory(Type customTypeHelperType)
        {
            var fromAbiMethod = customTypeHelperType.GetMethod("FromAbi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (fromAbiMethod is null)
            {
                throw new MissingMethodException();
            }

            var parms = new[] { Expression.Parameter(typeof(IInspectable), "obj") };
            return Expression.Lambda<Func<IInspectable, object>>(
                Expression.Call(fromAbiMethod, Expression.Property(parms[0], "ThisPtr")), parms).Compile();
        }

        internal static Func<IInspectable, object> CreateTypedRcwFactory(Type implementationType, string runtimeClassName = null)
        {
            if (implementationType == null)
            {
                // If we reach here, then we couldn't find a type that matches the runtime class name.
                // Fall back to using IInspectable directly.
                return (IInspectable obj) => obj;
            }

            var customHelperType = Projections.FindCustomHelperTypeMapping(implementationType, true);
            if (customHelperType != null)
            {
                return CreateReferenceCachingFactory(CreateCustomTypeMappingFactory(customHelperType));
            }

            if (implementationType.IsGenericType && implementationType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.KeyValuePair<,>))
            {
                return CreateReferenceCachingFactory(CreateKeyValuePairFactory(implementationType));
            }

            if (implementationType.IsValueType)
            {
                if (IsNullableT(implementationType))
                {
                    return CreateReferenceCachingFactory(CreateNullableTFactory(implementationType));
                }
                else
                {
                    return CreateReferenceCachingFactory(CreateNullableTFactory(typeof(System.Nullable<>).MakeGenericType(implementationType)));
                }
            }
            else if(IsAbiNullableDelegate(implementationType))
            {
                return CreateReferenceCachingFactory(CreateAbiNullableTFactory(implementationType));
            }
            else if (IsIReferenceArray(implementationType))
            {
                return CreateReferenceCachingFactory(CreateArrayFactory(implementationType));
            }

            return CreateFactoryForImplementationType(runtimeClassName, implementationType);
        }

        internal static Func<IInspectable, object> CreateTypedRcwFactory(string runtimeClassName)
        {
            // If runtime class name is empty or "Object", then just use IInspectable.
            if (string.IsNullOrEmpty(runtimeClassName) || 
                string.CompareOrdinal(runtimeClassName, "Object") == 0)
            {
                return (IInspectable obj) => obj;
            }
            // PropertySet and ValueSet can return IReference<String> but Nullable<String> is illegal
            if (string.CompareOrdinal(runtimeClassName, "Windows.Foundation.IReference`1<String>") == 0)
            {
                return CreateReferenceCachingFactory((IInspectable obj) => ABI.System.Nullable_string.GetValue(obj));
            }
            else if (string.CompareOrdinal(runtimeClassName, "Windows.Foundation.IReference`1<Windows.UI.Xaml.Interop.TypeName>") == 0)
            {
                return CreateReferenceCachingFactory((IInspectable obj) => ABI.System.Nullable_Type.GetValue(obj));
            }

            Type implementationType = TypeNameSupport.FindTypeByNameCached(runtimeClassName);
            return CreateTypedRcwFactory(implementationType, runtimeClassName);
        }

        internal static string GetRuntimeClassForTypeCreation(IInspectable inspectable, Type staticallyDeterminedType)
        {
            string runtimeClassName = inspectable.GetRuntimeClassName(noThrow: true);
            if (staticallyDeterminedType != null && staticallyDeterminedType != typeof(object))
            {
                // We have a static type which we can use to construct the object.  But, we can't just use it for all scenarios
                // and primarily use it for tear off scenarios and for scenarios where runtimeclass isn't accurate.
                // For instance if the static type is an interface, we return an IInspectable to represent the interface.
                // But it isn't convertable back to the class via the as operator which would be possible if we use runtimeclass.
                // Similarly for composable types, they can be statically retrieved using the parent class, but can then no longer
                // be cast to the sub class via as operator even if it is really an instance of it per rutimeclass.
                // To handle these scenarios, we use the runtimeclass if we find it is assignable to the statically determined type.
                // If it isn't, we use the statically determined type as it is a tear off.

                Type implementationType = null;
                if (!string.IsNullOrEmpty(runtimeClassName))
                {
                    implementationType = TypeNameSupport.FindTypeByNameCached(runtimeClassName);
                }

                if (!(implementationType != null &&
                    (staticallyDeterminedType == implementationType ||
                     staticallyDeterminedType.IsAssignableFrom(implementationType) ||
                     staticallyDeterminedType.IsGenericType && implementationType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == staticallyDeterminedType.GetGenericTypeDefinition()))))
                {
                    runtimeClassName = TypeNameSupport.GetNameForType(staticallyDeterminedType, TypeNameGenerationFlags.GenerateBoxedName);
                }
            }

            return runtimeClassName;
        }

        private readonly static ConcurrentDictionary<Type, bool> IsIReferenceTypeCache = new ConcurrentDictionary<Type, bool>();
        private static bool IsIReferenceType(Type type)
        {
            static bool IsIReferenceTypeHelper(Type type)
            {
                if ((type.GetCustomAttribute<WindowsRuntimeTypeAttribute>() is object) ||
                    WinRT.Projections.IsTypeWindowsRuntimeType(type))
                    return true;
                type = type.GetAuthoringMetadataType();
                if (type is object)
                {
                    if ((type.GetCustomAttribute<WindowsRuntimeTypeAttribute>() is object) ||
                        WinRT.Projections.IsTypeWindowsRuntimeType(type))
                        return true;
                }
                return false;
            }

            return IsIReferenceTypeCache.GetOrAdd(type, (type) =>
            {
                if (type == typeof(string) || type.IsTypeOfType())
                    return true;
                if (type.IsDelegate())
                    return IsIReferenceTypeHelper(type);
                if (!type.IsValueType)
                    return false;
                return type.IsPrimitive || IsIReferenceTypeHelper(type);
            });
        }

        private static bool ShouldProvideIReference(Type type) => IsIReferenceType(type);

        private static ComInterfaceEntry IPropertyValueEntry =>
            new ComInterfaceEntry
            {
                IID = global::WinRT.GuidGenerator.GetIID(typeof(global::Windows.Foundation.IPropertyValue)),
                Vtable = ManagedIPropertyValueImpl.AbiToProjectionVftablePtr
            };

        private static ComInterfaceEntry ProvideIReference(Type type)
        {
            if (type == typeof(int))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_int.IID,
                    Vtable = ABI.System.Nullable_int.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(string))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_string.IID,
                    Vtable = ABI.System.Nullable_string.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(byte))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_byte.IID,
                    Vtable = ABI.System.Nullable_byte.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(short))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_short.IID,
                    Vtable = ABI.System.Nullable_short.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(ushort))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_ushort.IID,
                    Vtable = ABI.System.Nullable_ushort.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(uint))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_uint.IID,
                    Vtable = ABI.System.Nullable_uint.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(long))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_long.IID,
                    Vtable = ABI.System.Nullable_long.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(ulong))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_ulong.IID,
                    Vtable = ABI.System.Nullable_ulong.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(float))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_float.IID,
                    Vtable = ABI.System.Nullable_float.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(double))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_double.IID,
                    Vtable = ABI.System.Nullable_double.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(char))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_char.IID,
                    Vtable = ABI.System.Nullable_char.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(bool))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_bool.IID,
                    Vtable = ABI.System.Nullable_bool.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(Guid))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_guid.IID,
                    Vtable = ABI.System.Nullable_guid.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(DateTimeOffset))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_DateTimeOffset.IID,
                    Vtable = ABI.System.Nullable_DateTimeOffset.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(TimeSpan))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_TimeSpan.IID,
                    Vtable = ABI.System.Nullable_TimeSpan.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(object))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_Object.IID,
                    Vtable = ABI.System.Nullable_Object.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type.IsTypeOfType())
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_Type.IID,
                    Vtable = ABI.System.Nullable_Type.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(sbyte))
            {
                return new ComInterfaceEntry
                {
                    IID = ABI.System.Nullable_sbyte.IID,
                    Vtable = ABI.System.Nullable_sbyte.Vftbl.AbiToProjectionVftablePtr
                };
            }
            if (type.IsDelegate())
            {
                var delegateHelperType = typeof(ABI.System.Nullable_Delegate<>).MakeGenericType(type);
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(delegateHelperType),
                    Vtable = delegateHelperType.GetAbiToProjectionVftblPtr()
                };
            }

            return new ComInterfaceEntry
            {
                IID = global::WinRT.GuidGenerator.GetIID(typeof(ABI.System.Nullable<>).MakeGenericType(type)),
                Vtable = typeof(BoxedValueIReferenceImpl<>).MakeGenericType(type).GetAbiToProjectionVftblPtr()
            };
        }

        private static bool ShouldProvideIReferenceArray(Type type)
        {
            // Check if one dimensional array with lower bound of 0
            return type.IsArray && type == type.GetElementType().MakeArrayType() && !type.GetElementType().IsArray;
        }

        private static ComInterfaceEntry ProvideIReferenceArray(Type arrayType)
        {
            Type type = arrayType.GetElementType();
            if (type == typeof(int))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<int>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<int>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(string))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<string>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<string>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(byte))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<byte>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<byte>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(short))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<short>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<short>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(ushort))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<ushort>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<ushort>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(uint))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<uint>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<uint>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(long))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<long>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<long>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(ulong))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<ulong>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<ulong>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(float))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<float>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<float>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(double))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<double>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<double>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(char))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<char>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<char>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(bool))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<bool>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<bool>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(Guid))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<Guid>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<Guid>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(DateTimeOffset))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<DateTimeOffset>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<DateTimeOffset>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(TimeSpan))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<TimeSpan>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<TimeSpan>.AbiToProjectionVftablePtr
                };
            }
            if (type == typeof(object))
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<object>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<object>.AbiToProjectionVftablePtr
                };
            }
            if (type.IsTypeOfType())
            {
                return new ComInterfaceEntry
                {
                    IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<Type>)),
                    Vtable = BoxedArrayIReferenceArrayImpl<Type>.AbiToProjectionVftablePtr
                };
            }
            return new ComInterfaceEntry
            {
                IID = global::WinRT.GuidGenerator.GetIID(typeof(IReferenceArray<>).MakeGenericType(type)),
                Vtable = (IntPtr)typeof(BoxedArrayIReferenceArrayImpl<>).MakeGenericType(type).GetAbiToProjectionVftblPtr()
            };
        }

        internal sealed class InspectableInfo
        {
            private readonly Lazy<string> runtimeClassName;

            public Guid[] IIDs { get; }
            public string RuntimeClassName => runtimeClassName.Value;

            internal InspectableInfo(Type type, Guid[] iids)
            {
                runtimeClassName = new Lazy<string>(() => TypeNameSupport.GetNameForType(type, TypeNameGenerationFlags.GenerateBoxedName | TypeNameGenerationFlags.NoCustomTypeName));
                IIDs = iids;
            }

        }
    }
}
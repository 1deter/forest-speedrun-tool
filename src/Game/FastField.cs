using System;
using System.Reflection;
using System.Reflection.Emit;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Field and property reads with no garbage, for the per-frame polls.
    //
    // FieldInfo.GetValue boxes every value-type answer (a bool, an int, an
    // enum: ~24-32 bytes on the heap each), and PropertyInfo.GetValue goes
    // through MethodBase.Invoke, which on this Mono also allocates. Read
    // once a frame - or once per inventory item four times a second - that
    // was a steady stream of garbage feeding the game's ~90 ms collections
    // (2026-10-04, the idle "overlay +KB/s" figure).
    //
    // These bind a delegate once: a DynamicMethod with a plain ldfld /
    // ldsfld for fields (as ClipWatch's bool getters already do), and
    // Delegate.CreateDelegate on the getter for properties. An enum field
    // read as int returns its value (the IL does not distinguish them).
    // When emitting fails, the getter falls back to the reflection read it
    // replaces, so behaviour never changes - only the garbage.
    // ------------------------------------------------------------------
    public static class FastField
    {
        /// `o => ((Declaring)o).field` as T. Null when `f` is null or
        /// static; T must be the field's type (or int for an int enum,
        /// object for any reference type).
        public static Func<object, T> Instance<T>(FieldInfo f)
        {
            if (f == null || f.IsStatic) return null;
            if (Compatible<T>(f.FieldType) && !f.DeclaringType.IsValueType)
            {
                try
                {
                    DynamicMethod dm = new DynamicMethod("Get_" + f.Name, typeof(T), new[] { typeof(object) }, typeof(FastField).Module, true);
                    ILGenerator il = dm.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, f.DeclaringType);
                    il.Emit(OpCodes.Ldfld, f);
                    il.Emit(OpCodes.Ret);
                    return (Func<object, T>)dm.CreateDelegate(typeof(Func<object, T>));
                }
                catch (Exception) { }
            }
            return delegate(object o) { return (T)Unbox(f.GetValue(o), typeof(T)); };
        }

        /// `() => Declaring.field` as T; null when `f` is null or not static.
        public static Func<T> Static<T>(FieldInfo f)
        {
            if (f == null || !f.IsStatic) return null;
            if (Compatible<T>(f.FieldType))
            {
                try
                {
                    DynamicMethod dm = new DynamicMethod("Get_" + f.Name, typeof(T), Type.EmptyTypes, typeof(FastField).Module, true);
                    ILGenerator il = dm.GetILGenerator();
                    il.Emit(OpCodes.Ldsfld, f);
                    il.Emit(OpCodes.Ret);
                    return (Func<T>)dm.CreateDelegate(typeof(Func<T>));
                }
                catch (Exception) { }
            }
            return delegate { return (T)Unbox(f.GetValue(null), typeof(T)); };
        }

        /// A static property's getter as a delegate; null when missing.
        public static Func<T> StaticProperty<T>(PropertyInfo p)
        {
            MethodInfo get = p != null ? p.GetGetMethod(true) : null;
            if (get == null || !get.IsStatic) return null;
            if (Compatible<T>(p.PropertyType))
            {
                try { return (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), get); }
                catch (Exception) { }
            }
            return delegate { return (T)Unbox(p.GetValue(null, null), typeof(T)); };
        }

        // What ldfld leaves on the stack is usable as T.
        private static bool Compatible<T>(Type fieldType)
        {
            Type t = typeof(T);
            if (fieldType == t) return true;
            if (fieldType.IsEnum) return t == typeof(int) && Enum.GetUnderlyingType(fieldType) == typeof(int);
            return !t.IsValueType && !fieldType.IsValueType && t.IsAssignableFrom(fieldType);
        }

        // The fallback: an enum's boxed value read as int, the rest as is.
        private static object Unbox(object v, Type t)
        {
            if (v != null && t == typeof(int) && v.GetType().IsEnum) return Convert.ToInt32(v);
            return v;
        }
    }
}

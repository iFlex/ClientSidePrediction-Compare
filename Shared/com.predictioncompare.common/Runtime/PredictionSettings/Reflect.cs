using System;
using System.Reflection;

namespace PredictionDebug
{
    // Reads settings a library keeps private or internal. Every use is recorded, so the settings panel can mark
    // the value with a dagger: those are the knobs the library gives you no supported way to read.
    // See LibrarySettingsAccess.md in the repository root for the list and what each library would need to expose.
    public static class Reflect
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // Set whenever a read below succeeds; SettingsSheet clears it before each row and checks it after.
        internal static bool Used;

        /// <summary>Instance field or property, searched up the class hierarchy.</summary>
        public static T Get<T>(object target, string name)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target), $"reading {name}");
            return Read<T>(target.GetType(), target, name);
        }

        /// <summary>Static field, property or constant.</summary>
        public static T GetStatic<T>(Type type, string name) => Read<T>(type, null, name);

        /// <summary>Calls a non-public static method, for values the library computes rather than stores.</summary>
        public static T CallStatic<T>(Type type, string name, params object[] args)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                MethodInfo method = t.GetMethod(name, Flags | BindingFlags.DeclaredOnly);
                if (method != null)
                {
                    Used = true;
                    return (T)Convert.ChangeType(method.Invoke(null, args), typeof(T));
                }
            }
            throw new MissingMemberException(type.Name, name);
        }

        static T Read<T>(Type type, object target, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, Flags | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    Used = true;
                    object value = field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(target);
                    return value is T typed ? typed : (T)Convert.ChangeType(value, typeof(T));
                }
                PropertyInfo property = t.GetProperty(name, Flags | BindingFlags.DeclaredOnly);
                if (property != null)
                {
                    Used = true;
                    object value = property.GetValue(target);
                    return value is T typed ? typed : (T)Convert.ChangeType(value, typeof(T));
                }
            }
            throw new MissingMemberException(type.Name, name);
        }
    }
}

// Value.cs
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DreamberdInterpreter
{
    public enum ValueKind
    {
        Number,
        String,
        Boolean,
        Null,
        Undefined,
        Array
    }

    public readonly struct Value
    {
        public ValueKind Kind
        {
            get;
        }
        public double Number
        {
            get;
        }
        public string String
        {
            get;
        }
        public bool Boolean
        {
            get;
        }
        public IReadOnlyDictionary<double, Value> Array
        {
            get;
        }

        private Value(ValueKind kind, double number, string str, bool boolean, IReadOnlyDictionary<double, Value> array)
        {
            Kind = kind;
            Number = number;
            String = str;
            Boolean = boolean;
            Array = array;
        }

        public static Value FromNumber(double n) => new Value(ValueKind.Number, n, null, false, null);
        public static Value FromString(string s) => new Value(ValueKind.String, 0, s ?? string.Empty, false, null);
        public static Value FromBoolean(bool b) => new Value(ValueKind.Boolean, 0, null, b, null);
        public static Value FromArray(IReadOnlyDictionary<double, Value> array) =>
            new Value(ValueKind.Array, 0, null, false, array);

        public static Value Null => new Value(ValueKind.Null, 0, null, false, null);
        public static Value Undefined => new Value(ValueKind.Undefined, 0, null, false, null);

        public override string ToString()
        {
            return Kind switch
            {
                ValueKind.Number => Number.ToString("G", CultureInfo.InvariantCulture),
                ValueKind.String => String ?? string.Empty,
                ValueKind.Boolean => Boolean ? "true" : "false",
                ValueKind.Null => "null",
                ValueKind.Undefined => "undefined",
                ValueKind.Array => "[" + (Array == null ? "" : string.Join(", ", ArrayValuesInIndexOrder())) + "]",
                _ => ""
            };
        }

        private IEnumerable<string> ArrayValuesInIndexOrder()
        {
            if (Array == null)
                yield break;

            var keys = new List<double>(Array.Keys);
            keys.Sort();
            foreach (var k in keys)
            {
                yield return Array[k].ToString();
            }
        }
    }
}

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
        Array,
        Object,
        Method
    }

    /// <summary>
    /// Trójstanowy boolean Dreamberda: true / false / maybe.
    /// </summary>
    public enum BooleanState
    {
        False = 0,
        True = 1,
        Maybe = 2
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
        public string? String
        {
            get;
        }
        public BooleanState Bool
        {
            get;
        }
        public IReadOnlyDictionary<double, Value>? Array
        {
            get;
        }
        public ClassInstance? Object
        {
            get;
        }
        public BoundMethod? Method
        {
            get;
        }

        private Value(
            ValueKind kind,
            double number,
            string? str,
            BooleanState boolVal,
            IReadOnlyDictionary<double, Value>? array,
            ClassInstance? obj,
            BoundMethod? method)
        {
            Kind = kind;
            Number = number;
            String = str;
            Bool = boolVal;
            Array = array;
            Object = obj;
            Method = method;
        }

        public static Value FromNumber(double number) =>
            new Value(ValueKind.Number, number, null, BooleanState.False, null, null, null);

        public static Value FromString(string str) =>
            new Value(ValueKind.String, 0, str ?? string.Empty, BooleanState.False, null, null, null);

        public static Value FromBoolean(bool b) =>
            new Value(ValueKind.Boolean, 0, null, b ? BooleanState.True : BooleanState.False, null, null, null);

        public static Value FromBooleanState(BooleanState state) =>
            new Value(ValueKind.Boolean, 0, null, state, null, null, null);

        /// <summary>
        /// Literal 'maybe'.
        /// </summary>
        public static Value Maybe =>
            new Value(ValueKind.Boolean, 0, null, BooleanState.Maybe, null, null, null);

        public static Value FromArray(IReadOnlyDictionary<double, Value> array) =>
            new Value(ValueKind.Array, 0, null, BooleanState.False, array, null, null);

        public static Value FromObject(ClassInstance instance) =>
            new Value(ValueKind.Object, 0, null, BooleanState.False, null, instance, null);

        public static Value FromMethod(BoundMethod method) =>
            new Value(ValueKind.Method, 0, null, BooleanState.False, null, null, method);

        public static Value Null =>
            new Value(ValueKind.Null, 0, null, BooleanState.False, null, null, null);

        public static Value Undefined =>
            new Value(ValueKind.Undefined, 0, null, BooleanState.False, null, null, null);

        public double ToNumber()
        {
            switch (Kind)
            {
                case ValueKind.Number:
                    return Number;

                case ValueKind.Boolean:
                    return Bool switch
                    {
                        BooleanState.True => 1.0,
                        BooleanState.False => 0.0,
                        BooleanState.Maybe => throw new InterpreterException("Cannot convert 'maybe' to number."),
                        _ => 0.0
                    };

                case ValueKind.String:
                    if (double.TryParse(String, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                        return n;
                    break;
            }

            throw new InterpreterException($"Cannot convert value '{this}' to number.");
        }

        public bool IsTruthy()
        {
            return Kind switch
            {
                ValueKind.Boolean => Bool == BooleanState.True || Bool == BooleanState.Maybe,
                ValueKind.Null => false,
                ValueKind.Undefined => false,
                ValueKind.Number => Math.Abs(Number) > double.Epsilon,
                ValueKind.String => !string.IsNullOrEmpty(String),
                ValueKind.Array => Array != null && Array.Count > 0,
                ValueKind.Object => Object != null,
                ValueKind.Method => Method != null,
                _ => false
            };
        }

        public bool StrictEquals(Value other)
        {
            if (Kind != other.Kind)
                return false;

            return Kind switch
            {
                ValueKind.Number => Math.Abs(Number - other.Number) < 1e-9,
                ValueKind.String => string.Equals(String, other.String, StringComparison.Ordinal),
                ValueKind.Boolean => Bool == other.Bool,
                ValueKind.Null => true,
                ValueKind.Undefined => true,
                ValueKind.Array => ReferenceEquals(Array, other.Array),
                ValueKind.Object => ReferenceEquals(Object, other.Object),
                ValueKind.Method => ReferenceEquals(Method, other.Method),
                _ => false
            };
        }

        public bool VeryStrictEquals(Value other)
        {
            if (!StrictEquals(other))
                return false;

            if (Kind == ValueKind.Number)
            {
                string sa = Number.ToString("R", CultureInfo.InvariantCulture);
                string sb = other.Number.ToString("R", CultureInfo.InvariantCulture);
                return string.Equals(sa, sb, StringComparison.Ordinal);
            }

            return true;
        }

        public bool LooseEquals(Value other)
        {
            if (Kind == other.Kind)
                return StrictEquals(other);

            try
            {
                return Math.Abs(ToNumber() - other.ToNumber()) < 1e-9;
            }
            catch
            {
                return false;
            }
        }

        public bool VeryLooseEquals(Value other)
        {
            return string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);
        }

        public override string ToString()
        {
            return Kind switch
            {
                ValueKind.Number => Number.ToString("G", CultureInfo.InvariantCulture),
                ValueKind.String => String ?? string.Empty,
                ValueKind.Boolean => Bool switch
                {
                    BooleanState.True => "true",
                    BooleanState.False => "false",
                    BooleanState.Maybe => "maybe",
                    _ => "false"
                },
                ValueKind.Null => "null",
                ValueKind.Undefined => "undefined",
                ValueKind.Array => "[" + (Array == null ? "" : string.Join(", ", ArrayValuesInIndexOrder())) + "]",
                ValueKind.Object => Object == null ? "[object]" : $"[{Object.Name} instance]",
                ValueKind.Method => Method == null ? "[method]" : $"[{Method.Target.Name}.{Method.Name}]",
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
                yield return Array[k].ToString();
        }
    }
}

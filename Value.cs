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

        private Value(
            ValueKind kind,
            double number,
            string? str,
            BooleanState boolVal,
            IReadOnlyDictionary<double, Value>? array)
        {
            Kind = kind;
            Number = number;
            String = str;
            Bool = boolVal;
            Array = array;
        }

        public static Value FromNumber(double number) =>
            new Value(ValueKind.Number, number, null, BooleanState.False, null);

        public static Value FromString(string str) =>
            new Value(ValueKind.String, 0, str ?? string.Empty, BooleanState.False, null);

        public static Value FromBoolean(bool b) =>
            new Value(ValueKind.Boolean, 0, null, b ? BooleanState.True : BooleanState.False, null);

        public static Value FromBooleanState(BooleanState state) =>
            new Value(ValueKind.Boolean, 0, null, state, null);

        /// <summary>
        /// Literal 'maybe'.
        /// </summary>
        public static Value Maybe =>
            new Value(ValueKind.Boolean, 0, null, BooleanState.Maybe, null);

        public static Value FromArray(IReadOnlyDictionary<double, Value> array) =>
            new Value(ValueKind.Array, 0, null, BooleanState.False, array);

        public static Value Null =>
            new Value(ValueKind.Null, 0, null, BooleanState.False, null);

        public static Value Undefined =>
            new Value(ValueKind.Undefined, 0, null, BooleanState.False, null);

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

        public bool UltraLooseEquals(Value other)
        {
            // Najluźniejsza równość '='.
            // Najpierw próbujemy porównać liczby po zaokrągleniu (Round).
            // Przykład ze specyfikacji: 3 = 3.14  -> true (oba roundują do 3).
            if (TryToUltraLooseNumber(this, out double a) && TryToUltraLooseNumber(other, out double b))
            {
                long ra = (long)Math.Round(a, MidpointRounding.AwayFromZero);
                long rb = (long)Math.Round(b, MidpointRounding.AwayFromZero);
                return ra == rb;
            }

            // Fallback: porównanie tekstowe (żeby np. "Luke" = "Luke" też działało).
            return string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);
        }

        private static bool TryToUltraLooseNumber(Value v, out double number)
        {
            switch (v.Kind)
            {
                case ValueKind.Number:
                    number = v.Number;
                    return true;

                case ValueKind.Boolean:
                    number = v.Bool switch
                    {
                        BooleanState.True => 1.0,
                        BooleanState.False => 0.0,
                        BooleanState.Maybe => 0.5,
                        _ => 0.0
                    };
                    return true;

                case ValueKind.Null:
                    number = 0.0;
                    return true;

                case ValueKind.String:
                    if (double.TryParse(v.String ?? string.Empty, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                        return true;

                    number = 0.0;
                    return false;

                default:
                    number = 0.0;
                    return false;
            }
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

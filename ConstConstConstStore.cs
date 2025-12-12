using System;
using System.Collections.Generic;

namespace DreamberdInterpreter
{
    public interface IConstConstConstStore
    {
        void Define(string name, Value value);
        bool TryGet(string name, out Value value);
    }

    public sealed class InMemoryConstConstConstStore : IConstConstConstStore
    {
        private readonly Dictionary<string, Value> _values =
            new(StringComparer.Ordinal);

        public void Define(string name, Value value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            _values[name] = value;
        }

        public bool TryGet(string name, out Value value)
        {
            return _values.TryGetValue(name, out value);
        }
    }
}

using System;
using System.Collections.Generic;

namespace DreamberdInterpreter
{
    public sealed class VariableStore
    {
        private sealed class VariableHistory
        {
            public List<Value> Values = new();
            public int Index;
        }

        private readonly struct LifetimeInfo
        {
            public LifetimeSpecifier Lifetime
            {
                get;
            }
            public int DeclarationIndex
            {
                get;
            }
            public DateTime CreatedAtUtc
            {
                get;
            }

            public LifetimeInfo(LifetimeSpecifier lifetime, int declarationIndex, DateTime createdAtUtc)
            {
                Lifetime = lifetime;
                DeclarationIndex = declarationIndex;
                CreatedAtUtc = createdAtUtc;
            }
        }

        private sealed class Entry
        {
            public Mutability Mutability = Mutability.VarVar;
            public int Priority;
            public Value CurrentValue;
            public VariableHistory History = new();
            public LifetimeInfo? Lifetime;
        }

        private readonly Dictionary<string, Entry> _entries =
            new(StringComparer.Ordinal);

        private const int MaxHistory = 100;

        public void Declare(
            string name,
            Mutability mutability,
            Value initialValue,
            int priority,
            LifetimeSpecifier lifetime,
            int declarationIndex)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));

            if (_entries.TryGetValue(name, out var existing))
            {
                if (priority < existing.Priority)
                    return;
            }

            var entry = new Entry
            {
                Mutability = mutability,
                Priority = priority,
                CurrentValue = initialValue,
                History = new VariableHistory()
            };

            entry.History.Values.Add(initialValue);
            entry.History.Index = 0;

            if (!lifetime.IsNone)
            {
                entry.Lifetime = new LifetimeInfo(lifetime, declarationIndex, DateTime.UtcNow);
            }

            _entries[name] = entry;
        }

        public bool TryGet(string name, out Value value)
        {
            if (_entries.TryGetValue(name, out var entry))
            {
                value = entry.CurrentValue;
                return true;
            }

            value = default;
            return false;
        }

        public Value Get(string name)
        {
            if (!_entries.TryGetValue(name, out var entry))
                throw new InterpreterException($"Variable '{name}' is not defined.");

            return entry.CurrentValue;
        }

        public void Assign(string name, Value newValue, int statementIndex)
        {
            if (!_entries.TryGetValue(name, out var entry))
                throw new InterpreterException($"Variable '{name}' is not defined.");

            if (entry.Mutability == Mutability.ConstConst ||
                entry.Mutability == Mutability.ConstVar)
            {
                throw new InterpreterException($"Variable '{name}' is not assignable.");
            }

            entry.CurrentValue = newValue;
            UpdateHistory(entry, newValue);
        }

        public void Delete(string name)
        {
            _entries.Remove(name);
        }

        public void ExpireLifetimes(int currentStatementIndex, DateTime nowUtc)
        {
            var toRemove = new List<string>();

            foreach (var pair in _entries)
            {
                string name = pair.Key;
                var entry = pair.Value;

                if (!entry.Lifetime.HasValue)
                    continue;

                var info = entry.Lifetime.Value;
                var lifetime = info.Lifetime;

                switch (lifetime.Kind)
                {
                    case LifetimeKind.Lines:
                        {
                            if (lifetime.Value > 0)
                            {
                                int lines = (int)lifetime.Value;
                                int lastIndex = info.DeclarationIndex + lines - 1;
                                if (currentStatementIndex > lastIndex)
                                    toRemove.Add(name);
                            }
                            break;
                        }

                    case LifetimeKind.Seconds:
                        {
                            if (lifetime.Value > 0)
                            {
                                double secs = lifetime.Value;
                                double elapsed = (nowUtc - info.CreatedAtUtc).TotalSeconds;
                                if (elapsed > secs)
                                    toRemove.Add(name);
                            }
                            break;
                        }

                    case LifetimeKind.Infinity:
                    case LifetimeKind.None:
                    default:
                        break;
                }
            }

            foreach (var name in toRemove)
            {
                _entries.Remove(name);
            }
        }

        public bool TryGetHistory(string name, out IReadOnlyList<Value> values, out int currentIndex)
        {
            if (_entries.TryGetValue(name, out var entry) &&
                entry.History != null &&
                entry.History.Values.Count > 0)
            {
                values = entry.History.Values;
                currentIndex = entry.History.Index;
                return true;
            }

            values = Array.Empty<Value>();
            currentIndex = -1;
            return false;
        }

        public bool TryPrevious(string name, out Value newValue, out bool changed)
        {
            newValue = default;
            changed = false;

            if (!_entries.TryGetValue(name, out var entry))
                return false;

            var hist = entry.History;
            if (hist == null || hist.Values.Count == 0)
                return false;

            if (hist.Index <= 0)
            {
                newValue = hist.Values[hist.Index];
                return true;
            }

            hist.Index--;
            newValue = hist.Values[hist.Index];
            entry.CurrentValue = newValue;
            changed = true;
            return true;
        }

        public bool TryNext(string name, out Value newValue, out bool changed)
        {
            newValue = default;
            changed = false;

            if (!_entries.TryGetValue(name, out var entry))
                return false;

            var hist = entry.History;
            if (hist == null || hist.Values.Count == 0)
                return false;

            if (hist.Index >= hist.Values.Count - 1)
            {
                newValue = hist.Values[hist.Index];
                return true;
            }

            hist.Index++;
            newValue = hist.Values[hist.Index];
            entry.CurrentValue = newValue;
            changed = true;
            return true;
        }

        private void UpdateHistory(Entry entry, Value newValue)
        {
            var hist = entry.History ??= new VariableHistory();

            if (hist.Values.Count == 0)
            {
                hist.Values.Add(newValue);
                hist.Index = 0;
                return;
            }

            var current = hist.Values[hist.Index];

            if (current.StrictEquals(newValue))
                return;

            if (hist.Index < hist.Values.Count - 1)
            {
                hist.Values.RemoveRange(hist.Index + 1, hist.Values.Count - hist.Index - 1);
            }

            hist.Values.Add(newValue);
            hist.Index = hist.Values.Count - 1;

            if (hist.Values.Count > MaxHistory)
            {
                hist.Values.RemoveAt(0);
                hist.Index--;
                if (hist.Index < 0) hist.Index = 0;
            }
        }
    }
}
